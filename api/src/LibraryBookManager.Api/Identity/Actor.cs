using LibraryBookManager.Api.Data;

namespace LibraryBookManager.Api.Identity;

/// <summary>The authenticated user a request is attributed to, and the session that proved it.</summary>
public sealed record Actor(int UserId, Guid PublicId, Role Role, int SessionId);

/// <summary>
/// Outcome of working out who is calling: nobody, somebody, or a credential that was
/// presented and refused (with the error code to report).
/// </summary>
public sealed record ActorResolution(Actor? Actor, string? RejectionCode)
{
    public static readonly ActorResolution Anonymous = new(null, null);
    public static ActorResolution For(Actor actor) => new(actor, null);
    public static ActorResolution Rejected(string code) => new(null, code);
}

public interface IActorResolver
{
    Task<ActorResolution> ResolveAsync(HttpContext context);
}
