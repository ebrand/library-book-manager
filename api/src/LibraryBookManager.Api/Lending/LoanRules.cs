using LibraryBookManager.Api.Data;

namespace LibraryBookManager.Api.Lending;

public static class LoanRules
{
    /// <summary>REQ-LEND-001: due 21 days from the day of checkout.</summary>
    public const int LoanPeriodDays = 21;

    /// <summary>REQ-LEND-002: a member may hold at most ten copies.</summary>
    public const int MaxOpenLoans = 10;

    /// <summary>REQ-LEND-005: overdue from the day after the due date until the copy is returned.</summary>
    public static bool IsOverdue(Loan loan, DateOnly today) => loan.ReturnedOn is null && today > loan.DueOn;
}

/// <summary>
/// "The day" in the lending rules is the library's calendar day, in the time zone set by
/// Library:TimeZone (an IANA id; UTC when unset — QUESTIONS.md Q-LEND-01).
/// </summary>
public sealed class LibraryCalendar
{
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;

    public LibraryCalendar(TimeProvider clock, IConfiguration configuration)
    {
        _clock = clock;
        var id = configuration["Library:TimeZone"];
        _zone = string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _zone).DateTime);
}
