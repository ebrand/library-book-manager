using LibraryBookManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Identity;

/// <summary>REQ-AUTH-001, REQ-AUTH-005: a bearer session token identifies the caller.</summary>
public sealed class SessionActorResolver(LibraryDbContext db, TimeProvider clock) : IActorResolver
{
    private const string Prefix = "Bearer ";

    public async Task<ActorResolution> ResolveAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.Length == 0) return ActorResolution.Anonymous;
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || header.Length == Prefix.Length)
            return ActorResolution.Rejected("SESSION_INVALID");

        var hash = SessionTokens.HashOf(header[Prefix.Length..].Trim());
        var session = await db.Sessions.Include(s => s.User).SingleOrDefaultAsync(s => s.TokenHash == hash);
        if (session?.User is null || session.EndedAt is not null) return ActorResolution.Rejected("SESSION_INVALID");

        var now = clock.GetUtcNow();
        if (now - session.LastUsedAt >= SessionTokens.IdleLimit) return ActorResolution.Rejected("SESSION_EXPIRED");

        session.LastUsedAt = now;
        await db.SaveChangesAsync();
        return ActorResolution.For(new Actor(session.User.Id, session.User.PublicId, session.User.Role, session.Id));
    }
}
