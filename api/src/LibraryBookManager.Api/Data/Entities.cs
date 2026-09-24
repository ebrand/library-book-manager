namespace LibraryBookManager.Api.Data;

public enum Role
{
    Member,
    Librarian,
    Administrator,
}

public sealed class User
{
    public int Id { get; set; }

    /// <summary>The identifier published in the API. The integer key is never exposed.</summary>
    public Guid PublicId { get; set; }

    /// <summary>Normalised: trimmed and lower-case.</summary>
    public required string Email { get; set; }

    public Role Role { get; set; }

    /// <summary>"pbkdf2-sha256$iterations$salt$hash". Never the password; never returned by the API.</summary>
    public required string PasswordHash { get; set; }
}

/// <summary>A signed-in session. Only a hash of its bearer token is stored.</summary>
public sealed class Session
{
    public int Id { get; set; }
    public required string TokenHash { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// Consecutive failed sign-ins per email address, whether or not an account exists, so that
/// lockout behaves the same for both (QUESTIONS.md Q-AUTH-02).
/// </summary>
public sealed class SignInThrottle
{
    public required string Email { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public enum AuditEvent
{
    SignIn,
    SignInRefused,
    SignOut,
    RoleChanged,
    UserCreated,
    PasswordSet,
}

/// <summary>REQ-AUTH-006: who did what to whom, and when.</summary>
public sealed class AuditRecord
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AuditEvent Event { get; set; }

    /// <summary>The user who acted; null when nobody was signed in (a refused sign-in, an operator command).</summary>
    public int? ActorUserId { get; set; }

    public int? AffectedUserId { get; set; }

    /// <summary>For a refused sign-in: the email as typed (normalised). Never the password.</summary>
    public string? AttemptedEmail { get; set; }

    /// <summary>For a refused sign-in: the error code returned.</summary>
    public string? Outcome { get; set; }

    public Role? OldRole { get; set; }
    public Role? NewRole { get; set; }
}

public sealed class Title
{
    public int Id { get; set; }
    public required string Isbn { get; set; }
    public required string Name { get; set; }
    public required string Author { get; set; }
    public int PublicationYear { get; set; }
    public List<Copy> Copies { get; set; } = [];
}

public sealed class Copy
{
    public int Id { get; set; }
    public int TitleId { get; set; }
    public Title? Title { get; set; }
    public required string Barcode { get; set; }

    /// <summary>
    /// Set when a librarian removes the copy. The row is kept so the copy's loan history
    /// survives and its barcode is never reassigned.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    public List<Loan> Loans { get; set; } = [];
}

public sealed class Loan
{
    public int Id { get; set; }
    public int CopyId { get; set; }
    public Copy? Copy { get; set; }
    public int MemberId { get; set; }
    public User? Member { get; set; }
    public DateOnly CheckedOutOn { get; set; }
    public DateOnly DueOn { get; set; }
    public DateOnly? ReturnedOn { get; set; }

    /// <summary>Set at return when the copy came back after its due date (AC-LEND-004-3).</summary>
    public bool ReturnedLate { get; set; }
}
