using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Http;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Identity;

public sealed record UserResponse(Guid UserId, string Email, Role Role)
{
    public static UserResponse From(User user) => new(user.PublicId, user.Email, user.Role);
}

public sealed record SessionResponse(string Token, UserResponse User);

/// <summary>Capability AUTH — sign-in, sign-out, users and roles.</summary>
public static class AuthEndpoints
{
    public const int FailuresBeforeLock = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static void MapAuth(this IEndpointRouteBuilder v1)
    {
        v1.MapPost("/sessions", SignIn);
        v1.MapDelete("/sessions/current", SignOut).RequireActor();
        v1.MapGet("/users/me", Me).RequireActor();
        v1.MapGet("/users", ListUsers).RequireActor(Role.Administrator);
        v1.MapPut("/users/{userId}/role", ChangeRole).RequireActor(Role.Administrator);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // REQ-AUTH-001, REQ-AUTH-003
    private static async Task<IResult> SignIn(HttpRequest request, LibraryDbContext db, PasswordHasher hasher, TimeProvider clock)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();
        if (body.IsEmpty("email") || body.Text("email") is null) return ApiError.FieldRequired("email");
        if (!body.TryGetProperty("password", out var passwordElement)
            || passwordElement.ValueKind != System.Text.Json.JsonValueKind.String
            || passwordElement.GetString() is not { Length: > 0 } password)
            return ApiError.FieldRequired("password");

        var email = NormalizeEmail(body.Text("email")!);
        var now = clock.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        var throttle = await db.SignInThrottles.SingleOrDefaultAsync(t => t.Email == email);
        if (throttle?.LockedUntil is { } lockedUntil)
        {
            if (now < lockedUntil) return await Refuse(db, now, email, user, "ACCOUNT_LOCKED");
            // The lock has run out: the count starts again.
            throttle.LockedUntil = null;
            throttle.ConsecutiveFailures = 0;
            await db.SaveChangesAsync();
        }

        bool verified;
        if (user is null)
        {
            hasher.VerifyDecoy(password);
            verified = false;
        }
        else
        {
            verified = hasher.Verify(password, user.PasswordHash);
        }

        if (!verified)
        {
            var failures = await CountFailure(db, email);
            if (failures < FailuresBeforeLock) return await Refuse(db, now, email, user, "INVALID_CREDENTIALS");
            await db.SignInThrottles.Where(t => t.Email == email && t.LockedUntil == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LockedUntil, now + LockDuration));
            return await Refuse(db, now, email, user, "ACCOUNT_LOCKED");
        }

        await db.SignInThrottles.Where(t => t.Email == email).ExecuteDeleteAsync();
        var token = SessionTokens.NewToken();
        db.Sessions.Add(new Session { TokenHash = SessionTokens.HashOf(token), UserId = user!.Id, CreatedAt = now, LastUsedAt = now });
        db.AuditRecords.Add(new AuditRecord { Event = AuditEvent.SignIn, OccurredAt = now, ActorUserId = user.Id, AffectedUserId = user.Id });
        await db.SaveChangesAsync();
        return Results.Json(new SessionResponse(token, UserResponse.From(user)), statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Adds one failure in a single statement, so concurrent attempts are all counted.</summary>
    private static async Task<int> CountFailure(LibraryDbContext db, string email)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO SignInThrottles (Email, ConsecutiveFailures) VALUES ({email}, 1)
            ON CONFLICT (Email) DO UPDATE SET ConsecutiveFailures = ConsecutiveFailures + 1
            """);
        return await db.SignInThrottles.Where(t => t.Email == email).Select(t => t.ConsecutiveFailures).SingleAsync();
    }

    /// <summary>
    /// Every refusal returns the same body for a given code, whether or not the account
    /// exists (AC-AUTH-001-3).
    /// </summary>
    private static async Task<IResult> Refuse(LibraryDbContext db, DateTimeOffset now, string email, User? user, string code)
    {
        db.AuditRecords.Add(new AuditRecord
        {
            Event = AuditEvent.SignInRefused, OccurredAt = now, AffectedUserId = user?.Id, AttemptedEmail = email, Outcome = code,
        });
        await db.SaveChangesAsync();
        return code == "ACCOUNT_LOCKED"
            ? ApiError.Unauthenticated(code, "Too many failed sign-ins. Try again in 15 minutes.")
            : ApiError.Unauthenticated(code, "The email address or password is not correct.");
    }

    // REQ-AUTH-005
    private static async Task<IResult> SignOut(HttpContext context, LibraryDbContext db, TimeProvider clock)
    {
        var actor = context.CurrentActor();
        var now = clock.GetUtcNow();
        await db.Sessions.Where(s => s.Id == actor.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now));
        db.AuditRecords.Add(new AuditRecord { Event = AuditEvent.SignOut, OccurredAt = now, ActorUserId = actor.UserId, AffectedUserId = actor.UserId });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext context, LibraryDbContext db) =>
        Results.Ok(UserResponse.From(await db.Users.SingleAsync(u => u.Id == context.CurrentActor().UserId)));

    private static async Task<IResult> ListUsers(LibraryDbContext db) =>
        Results.Ok((await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync()).Select(UserResponse.From));

    // REQ-AUTH-004, REQ-AUTH-006
    private static async Task<IResult> ChangeRole(string userId, HttpRequest request, HttpContext context, LibraryDbContext db, TimeProvider clock)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();
        if (body.IsEmpty("role")) return ApiError.FieldRequired("role");
        if (!TryParseRole(body.Text("role"), out var role))
            return ApiError.InvalidField("role", "role must be member, librarian or administrator.");

        var target = Guid.TryParse(userId, out var publicId)
            ? await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId)
            : null;
        if (target is null) return ApiError.NotFound("USER_NOT_FOUND", "No user has this id.");

        var actor = context.CurrentActor();
        if (target.Id == actor.UserId) return ApiError.Forbidden();

        if (target.Role != role)
        {
            db.AuditRecords.Add(new AuditRecord
            {
                Event = AuditEvent.RoleChanged, OccurredAt = clock.GetUtcNow(),
                ActorUserId = actor.UserId, AffectedUserId = target.Id, OldRole = target.Role, NewRole = role,
            });
            target.Role = role;
            await db.SaveChangesAsync();
        }
        return Results.Ok(UserResponse.From(target));
    }

    public static bool TryParseRole(string? text, out Role role)
    {
        role = default;
        switch (text)
        {
            case "member": role = Role.Member; return true;
            case "librarian": role = Role.Librarian; return true;
            case "administrator": role = Role.Administrator; return true;
            default: return false;
        }
    }
}
