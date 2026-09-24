using System.Data;
using System.Linq.Expressions;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Lending;

public sealed record MemberResponse(Guid UserId, string Email);

public sealed record LoanResponse(
    string Barcode, string Isbn, string Title, MemberResponse Member,
    DateOnly CheckedOutOn, DateOnly DueOn, DateOnly? ReturnedOn, bool ReturnedLate, bool Overdue);

/// <summary>Capability LEND — checkout, return, current loans and the lending report.</summary>
public static class LendingEndpoints
{
    public static void MapLending(this IEndpointRouteBuilder v1)
    {
        v1.MapPost("/loans", CheckOut).RequireActor(Role.Librarian);
        v1.MapPost("/copies/{barcode}/return", Return).RequireActor(Role.Librarian);
        v1.MapGet("/loans", Report).RequireActor(Role.Librarian);
        v1.MapGet("/members", FindMember).RequireActor(Role.Librarian);
        v1.MapGet("/members/{memberId}/loans", MemberLoans).RequireActor();
    }

    /// <summary>
    /// Checkout, return and copy removal each read-then-write; running them in an immediate
    /// (write-locking) transaction serialises them, so two cannot interleave (Q-LEND-06).
    /// </summary>
    public static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginSerialized(LibraryDbContext db) =>
        db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    // REQ-LEND-001, -002, -003
    private static async Task<IResult> CheckOut(HttpRequest request, LibraryDbContext db, LibraryCalendar calendar)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();
        if (body.IsEmpty("barcode")) return ApiError.FieldRequired("barcode");
        if (body.IsEmpty("memberId")) return ApiError.FieldRequired("memberId");
        if (body.Text("barcode") is not { } barcode) return ApiError.InvalidField("barcode", "barcode must be text.");
        if (!Guid.TryParse(body.Text("memberId"), out var memberId))
            return ApiError.InvalidField("memberId", "memberId must be a user id.");

        await using var transaction = await BeginSerialized(db);

        var copy = await db.Copies.SingleOrDefaultAsync(c => c.Barcode == barcode && c.RemovedAt == null);
        if (copy is null) return CopyNotFound();
        var member = await db.Users.SingleOrDefaultAsync(u => u.PublicId == memberId);
        if (member is null) return UserNotFound();
        if (member.Role != Role.Member)
            return ApiError.Conflict("BORROWER_NOT_A_MEMBER", "Copies are lent only to members.");
        if (await db.Loans.AnyAsync(l => l.CopyId == copy.Id && l.ReturnedOn == null)) return CopyOnLoan();

        var today = calendar.Today();
        var open = db.Loans.Where(l => l.MemberId == member.Id && l.ReturnedOn == null);
        if (await open.AnyAsync(l => l.DueOn < today))
            return ApiError.Conflict("MEMBER_HAS_OVERDUE_LOAN", "The member has an overdue loan.");
        if (await open.CountAsync() >= LoanRules.MaxOpenLoans)
            return ApiError.Conflict("LOAN_LIMIT_REACHED", $"The member already has {LoanRules.MaxOpenLoans} copies on loan.");

        var loan = new Loan { CopyId = copy.Id, MemberId = member.Id, CheckedOutOn = today, DueOn = today.AddDays(LoanRules.LoanPeriodDays) };
        db.Loans.Add(loan);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            return CopyOnLoan(); // the one-open-loan-per-copy index caught a concurrent checkout
        }

        return Results.Json((await Loans(db, calendar, l => l.Id == loan.Id)).Single(), statusCode: StatusCodes.Status201Created);
    }

    // REQ-LEND-004
    private static async Task<IResult> Return(string barcode, LibraryDbContext db, LibraryCalendar calendar)
    {
        await using var transaction = await BeginSerialized(db);

        var copy = await db.Copies.SingleOrDefaultAsync(c => c.Barcode == barcode && c.RemovedAt == null);
        if (copy is null) return CopyNotFound();
        var loan = await db.Loans.SingleOrDefaultAsync(l => l.CopyId == copy.Id && l.ReturnedOn == null);
        if (loan is null) return ApiError.Conflict("COPY_NOT_ON_LOAN", "The copy is not on loan.");

        var today = calendar.Today();
        loan.ReturnedOn = today;
        loan.ReturnedLate = today > loan.DueOn;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Ok((await Loans(db, calendar, l => l.Id == loan.Id)).Single());
    }

    // REQ-LEND-007
    private static async Task<IResult> Report(LibraryDbContext db, LibraryCalendar calendar) =>
        Results.Ok(await Loans(db, calendar, l => l.ReturnedOn == null));

    // REQ-LEND-006
    private static async Task<IResult> MemberLoans(string memberId, HttpContext context, LibraryDbContext db, LibraryCalendar calendar)
    {
        var actor = context.CurrentActor();
        var parsed = Guid.TryParse(memberId, out var publicId);
        var self = parsed && publicId == actor.PublicId;
        // Anyone but a librarian may see only their own loans; they are not told whether another id exists.
        if (!self && actor.Role != Role.Librarian) return ApiError.Forbidden();

        var member = parsed ? await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId) : null;
        if (member is null) return UserNotFound();

        return Results.Ok(await Loans(db, calendar, l => l.MemberId == member.Id && l.ReturnedOn == null));
    }

    private static async Task<IResult> FindMember(HttpRequest request, LibraryDbContext db)
    {
        var email = request.Query["email"].ToString().Trim().ToLowerInvariant();
        var found = await db.Users.AsNoTracking()
            .Where(u => u.Email == email && u.Role == Role.Member)
            .Select(u => new MemberResponse(u.PublicId, u.Email))
            .ToListAsync();
        return Results.Ok(found);
    }

    /// <summary>The published view of the loans matching <paramref name="filter"/>, by due date then barcode.</summary>
    private static async Task<List<LoanResponse>> Loans(LibraryDbContext db, LibraryCalendar calendar, Expression<Func<Loan, bool>> filter)
    {
        var today = calendar.Today();
        var rows = await db.Loans.AsNoTracking().Where(filter)
            .Select(l => new
            {
                l.Copy!.Barcode, l.Copy.Title!.Isbn, Title = l.Copy.Title.Name,
                l.Member!.PublicId, l.Member.Email,
                l.CheckedOutOn, l.DueOn, l.ReturnedOn, l.ReturnedLate,
            })
            .ToListAsync();
        return rows
            .OrderBy(r => r.DueOn).ThenBy(r => r.Barcode, StringComparer.Ordinal)
            .Select(r => new LoanResponse(r.Barcode, r.Isbn, r.Title, new MemberResponse(r.PublicId, r.Email),
                r.CheckedOutOn, r.DueOn, r.ReturnedOn, r.ReturnedLate,
                LoanRules.IsOverdue(new Loan { DueOn = r.DueOn, ReturnedOn = r.ReturnedOn }, today)))
            .ToList();
    }

    private static IResult CopyNotFound() => ApiError.NotFound("COPY_NOT_FOUND", "No copy in the catalogue has this barcode.");
    private static IResult CopyOnLoan() => ApiError.Conflict("COPY_ON_LOAN", "The copy is already on loan.");
    private static IResult UserNotFound() => ApiError.NotFound("USER_NOT_FOUND", "No user has this id.");
}
