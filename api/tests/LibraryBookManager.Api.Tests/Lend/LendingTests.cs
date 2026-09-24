using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Lending;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Lend;

/// <summary>Capability LEND — Lending. The factory clock starts at 09:00 UTC on 1 March 2026.</summary>
public sealed class LendingTests : IDisposable
{
    private const string Hobbit = "9780261102217";
    private static readonly DateOnly March1 = new(2026, 3, 1);
    private readonly ApiFactory _api = new();
    private readonly User _librarian;
    private readonly User _member;

    public LendingTests()
    {
        _librarian = _api.User(Role.Librarian);
        _member = _api.User(Role.Member);
    }

    public void Dispose() => _api.Dispose();

    private void SetDay(DateOnly day, int hour = 9) =>
        _api.Clock.SetUtcNow(new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero));

    private Task<HttpResponseMessage> CheckOut(string barcode, User member) =>
        _api.ClientAs(_librarian).PostAsJsonAsync("/v1/loans", new { barcode, memberId = member.PublicId });

    private Task<HttpResponseMessage> Return(string barcode) =>
        _api.ClientAs(_librarian).PostAsync($"/v1/copies/{barcode}/return", null);

    /// <summary>A title with <paramref name="copies"/> copies, barcodes "{prefix}-1".."{prefix}-n".</summary>
    private void Copies(string isbn, int copies) => _api.Title(isbn, copies);

    private List<Loan> Loans() => _api.Query(db => db.Loans.OrderBy(l => l.Id).ToList());

    // ---------------------------------------------------------------- REQ-LEND-001

    [Fact(DisplayName = "AC-LEND-001-1: checking out an available copy to a member in good standing on 1 March puts it on loan to them, due on 22 March")]
    public async Task AC_LEND_001_1()
    {
        Copies(Hobbit, 1);
        SetDay(March1);

        var response = await CheckOut($"{Hobbit}-1", _member);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = await response.Read<LoanView>();
        Assert.Equal((March1, new DateOnly(2026, 3, 22)), (loan.CheckedOutOn, loan.DueOn));
        Assert.Equal(_member.PublicId, loan.Member.UserId);
        Assert.Null(loan.ReturnedOn);
        var stored = Assert.Single(Loans());
        Assert.Equal((_member.Id, new DateOnly(2026, 3, 22)), (stored.MemberId, stored.DueOn));
        var title = await (await _api.ClientAs(_librarian).GetAsync($"/v1/titles/{Hobbit}")).Read<TitleDetail>();
        Assert.True(title.Copies.Single().OnLoan);
        Assert.Equal(0, title.AvailableCopies);
    }

    [Fact(DisplayName = "AC-LEND-001-2: checking out a copy already on loan to another member is rejected with COPY_ON_LOAN and the existing loan is unchanged")]
    public async Task AC_LEND_001_2()
    {
        Copies(Hobbit, 1);
        var other = _api.User(Role.Member);
        var existing = _api.OpenLoan($"{Hobbit}-1", other, checkedOut: new DateOnly(2026, 2, 20));

        var response = await CheckOut($"{Hobbit}-1", _member);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("COPY_ON_LOAN", (await response.Read<ErrorBody>()).Error);
        var stored = Assert.Single(Loans());
        Assert.Equal(
            (existing.Id, other.Id, existing.CheckedOutOn, existing.DueOn, (DateOnly?)null),
            (stored.Id, stored.MemberId, stored.CheckedOutOn, stored.DueOn, stored.ReturnedOn));
    }

    [Fact(DisplayName = "AC-LEND-001-3: checking out a copy removed from the catalogue is rejected with COPY_NOT_FOUND")]
    public async Task AC_LEND_001_3()
    {
        Copies(Hobbit, 1);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1")).StatusCode);

        var response = await CheckOut($"{Hobbit}-1", _member);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("COPY_NOT_FOUND", (await response.Read<ErrorBody>()).Error);
        Assert.Empty(Loans());
    }

    // ---------------------------------------------------------------- REQ-LEND-002

    [Fact(DisplayName = "AC-LEND-002-1: a member holding nine copies can check out a tenth")]
    public async Task AC_LEND_002_1()
    {
        Copies(Hobbit, 10);
        for (var i = 1; i <= 9; i++) _api.OpenLoan($"{Hobbit}-{i}", _member, checkedOut: March1);

        var response = await CheckOut($"{Hobbit}-10", _member);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, Loans().Count(l => l.MemberId == _member.Id && l.ReturnedOn == null));
    }

    [Fact(DisplayName = "AC-LEND-002-2: a member holding ten copies is refused an eleventh with LOAN_LIMIT_REACHED")]
    public async Task AC_LEND_002_2()
    {
        Copies(Hobbit, 11);
        for (var i = 1; i <= 10; i++) _api.OpenLoan($"{Hobbit}-{i}", _member, checkedOut: March1);

        var response = await CheckOut($"{Hobbit}-11", _member);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LOAN_LIMIT_REACHED", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(10, Loans().Count);
    }

    [Fact(DisplayName = "REQ-LEND-002: returned loans do not count toward the limit of ten")]
    public async Task Returned_loans_do_not_count()
    {
        Copies(Hobbit, 11);
        for (var i = 1; i <= 10; i++) _api.OpenLoan($"{Hobbit}-{i}", _member, checkedOut: March1);
        Assert.Equal(HttpStatusCode.OK, (await Return($"{Hobbit}-1")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await CheckOut($"{Hobbit}-11", _member)).StatusCode);
    }

    // ---------------------------------------------------------------- REQ-LEND-003

    [Fact(DisplayName = "AC-LEND-003-1: a member with one overdue loan is refused another with MEMBER_HAS_OVERDUE_LOAN")]
    public async Task AC_LEND_003_1()
    {
        Copies(Hobbit, 2);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: new DateOnly(2026, 2, 1), due: new DateOnly(2026, 2, 22));
        SetDay(March1);

        var response = await CheckOut($"{Hobbit}-2", _member);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("MEMBER_HAS_OVERDUE_LOAN", (await response.Read<ErrorBody>()).Error);
        Assert.Single(Loans());
    }

    [Fact(DisplayName = "AC-LEND-003-2: a member whose only overdue loan has just been returned can check out again")]
    public async Task AC_LEND_003_2()
    {
        Copies(Hobbit, 2);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: new DateOnly(2026, 2, 1), due: new DateOnly(2026, 2, 22));
        SetDay(March1);
        Assert.Equal("MEMBER_HAS_OVERDUE_LOAN", (await (await CheckOut($"{Hobbit}-2", _member)).Read<ErrorBody>()).Error);
        Assert.Equal(HttpStatusCode.OK, (await Return($"{Hobbit}-1")).StatusCode);

        var response = await CheckOut($"{Hobbit}-2", _member);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-LEND-003: the refusal starts on the first overdue day, not later, and not on the due date")]
    public async Task Overdue_boundary_for_checkout()
    {
        Copies(Hobbit, 3);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: March1); // due 22 March

        SetDay(new DateOnly(2026, 3, 22), hour: 23);
        Assert.Equal(HttpStatusCode.Created, (await CheckOut($"{Hobbit}-2", _member)).StatusCode);

        SetDay(new DateOnly(2026, 3, 23), hour: 0);
        var response = await CheckOut($"{Hobbit}-3", _member);
        Assert.Equal("MEMBER_HAS_OVERDUE_LOAN", (await response.Read<ErrorBody>()).Error);
    }

    // ---------------------------------------------------------------- REQ-LEND-004

    [Fact(DisplayName = "AC-LEND-004-1: returning a copy on loan on 10 March closes the loan with that return date and the copy is available")]
    public async Task AC_LEND_004_1()
    {
        Copies(Hobbit, 1);
        SetDay(March1);
        Assert.Equal(HttpStatusCode.Created, (await CheckOut($"{Hobbit}-1", _member)).StatusCode);
        SetDay(new DateOnly(2026, 3, 10), hour: 17);

        var response = await Return($"{Hobbit}-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loan = await response.Read<LoanView>();
        Assert.Equal(new DateOnly(2026, 3, 10), loan.ReturnedOn);
        Assert.False(loan.ReturnedLate);
        Assert.Equal(new DateOnly(2026, 3, 10), Assert.Single(Loans()).ReturnedOn);
        var found = await (await _api.ClientAs(_member).GetAsync($"/v1/titles?isbn={Hobbit}")).Read<SearchPage>();
        Assert.Equal(1, found.Items.Single().AvailableCopies);
    }

    [Fact(DisplayName = "AC-LEND-004-2: returning a copy that is already on the shelf is rejected with COPY_NOT_ON_LOAN")]
    public async Task AC_LEND_004_2()
    {
        Copies(Hobbit, 1);

        var response = await Return($"{Hobbit}-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("COPY_NOT_ON_LOAN", (await response.Read<ErrorBody>()).Error);
        Assert.Empty(Loans());
    }

    [Fact(DisplayName = "AC-LEND-004-3: a copy returned after its due date closes the loan recorded as returned late")]
    public async Task AC_LEND_004_3()
    {
        Copies(Hobbit, 1);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member); // due 22 March
        SetDay(new DateOnly(2026, 3, 23));

        var response = await Return($"{Hobbit}-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loan = await response.Read<LoanView>();
        Assert.Equal((new DateOnly(2026, 3, 23), true), (loan.ReturnedOn, loan.ReturnedLate));
        var stored = Assert.Single(Loans());
        Assert.True(stored.ReturnedLate);
        Assert.Equal(new DateOnly(2026, 3, 23), stored.ReturnedOn);
    }

    [Fact(DisplayName = "REQ-LEND-004: a copy returned on its due date is not recorded as late")]
    public async Task On_due_date_is_not_late()
    {
        Copies(Hobbit, 1);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        SetDay(new DateOnly(2026, 3, 22), hour: 23);

        var loan = await (await Return($"{Hobbit}-1")).Read<LoanView>();

        Assert.False(loan.ReturnedLate);
        Assert.False(Assert.Single(Loans()).ReturnedLate);
    }

    // ---------------------------------------------------------------- REQ-LEND-005

    [Fact(DisplayName = "AC-LEND-005-1: a loan due on 22 March and not returned is overdue when the day becomes 23 March")]
    public async Task AC_LEND_005_1()
    {
        Copies(Hobbit, 1);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        var loans = $"/v1/members/{_member.PublicId}/loans";

        _api.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 22, 23, 59, 59, TimeSpan.Zero));
        Assert.False((await (await _api.ClientAs(_member).GetAsync(loans)).Read<List<LoanView>>()).Single().Overdue);

        _api.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 23, 0, 0, 0, TimeSpan.Zero));
        Assert.True((await (await _api.ClientAs(_member).GetAsync(loans)).Read<List<LoanView>>()).Single().Overdue);

        // And it stays overdue until returned.
        SetDay(new DateOnly(2026, 6, 1));
        var report = await (await _api.ClientAs(_librarian).GetAsync("/v1/loans")).Read<List<LoanView>>();
        Assert.True(report.Single().Overdue);
    }

    [Fact(DisplayName = "AC-LEND-005-2: a loan due on 22 March and returned on 22 March is not overdue when the day becomes 23 March")]
    public async Task AC_LEND_005_2()
    {
        Copies(Hobbit, 2);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        SetDay(new DateOnly(2026, 3, 22));
        var returned = await (await Return($"{Hobbit}-1")).Read<LoanView>();
        Assert.False(returned.Overdue);

        SetDay(new DateOnly(2026, 3, 23));

        var stored = Assert.Single(Loans());
        Assert.False(LoanRules.IsOverdue(stored, new DateOnly(2026, 3, 23)));
        Assert.Empty(await (await _api.ClientAs(_librarian).GetAsync("/v1/loans")).Read<List<LoanView>>());
        // Not being overdue is what lets the member borrow again (REQ-LEND-003).
        Assert.Equal(HttpStatusCode.Created, (await CheckOut($"{Hobbit}-2", _member)).StatusCode);
    }

    // ---------------------------------------------------------------- REQ-LEND-006

    [Fact(DisplayName = "AC-LEND-006-1: a member holding two copies sees both loans with their due dates")]
    public async Task AC_LEND_006_1()
    {
        Copies(Hobbit, 3);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        SetDay(new DateOnly(2026, 3, 5));
        await CheckOut($"{Hobbit}-2", _member);
        await CheckOut($"{Hobbit}-3", _api.User(Role.Member)); // someone else's: not shown

        var response = await _api.ClientAs(_member).GetAsync($"/v1/members/{_member.PublicId}/loans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loans = await response.Read<List<LoanView>>();
        Assert.Equal(
            [($"{Hobbit}-1", new DateOnly(2026, 3, 22)), ($"{Hobbit}-2", new DateOnly(2026, 3, 26))],
            loans.Select(l => (l.Barcode, l.DueOn)).OrderBy(x => x.Barcode).ToList());
        Assert.All(loans, l => Assert.Equal("The Lord of the Rings", l.Title));
    }

    [Fact(DisplayName = "AC-LEND-006-2: a member asking for another member's loans is rejected with FORBIDDEN")]
    public async Task AC_LEND_006_2()
    {
        Copies(Hobbit, 1);
        var other = _api.User(Role.Member);
        _api.OpenLoan($"{Hobbit}-1", other, checkedOut: March1);

        var response = await _api.ClientAs(_member).GetAsync($"/v1/members/{other.PublicId}/loans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("FORBIDDEN", body);
        Assert.DoesNotContain(Hobbit, body);
    }

    [Fact(DisplayName = "AC-LEND-006-3: a librarian asking for a member's loans gets them")]
    public async Task AC_LEND_006_3()
    {
        Copies(Hobbit, 2);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: March1);
        _api.OpenLoan($"{Hobbit}-2", _member, checkedOut: March1);

        var response = await _api.ClientAs(_librarian).GetAsync($"/v1/members/{_member.PublicId}/loans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Read<List<LoanView>>()).Count);
    }

    [Fact(DisplayName = "REQ-LEND-006: returned loans are not among a member's current loans")]
    public async Task Current_loans_only()
    {
        Copies(Hobbit, 2);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        await CheckOut($"{Hobbit}-2", _member);
        await Return($"{Hobbit}-1");

        var loans = await (await _api.ClientAs(_member).GetAsync($"/v1/members/{_member.PublicId}/loans")).Read<List<LoanView>>();

        Assert.Equal($"{Hobbit}-2", Assert.Single(loans).Barcode);
    }

    // ---------------------------------------------------------------- REQ-LEND-007

    [Fact(DisplayName = "AC-LEND-007-1: a librarian gets a report of every book currently lent out and to whom")]
    public async Task AC_LEND_007_1()
    {
        Copies(Hobbit, 4);
        var second = _api.User(Role.Member);
        SetDay(March1);
        await CheckOut($"{Hobbit}-1", _member);
        await CheckOut($"{Hobbit}-2", second);
        await CheckOut($"{Hobbit}-3", second);
        await Return($"{Hobbit}-3"); // returned: not currently lent out

        var response = await _api.ClientAs(_librarian).GetAsync("/v1/loans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Read<List<LoanView>>();
        Assert.Equal(
            [($"{Hobbit}-1", _member.Email), ($"{Hobbit}-2", second.Email)],
            report.Select(l => (l.Barcode, l.Member.Email)).OrderBy(x => x.Barcode).ToList());
        Assert.All(report, l => Assert.Equal(Hobbit, l.Isbn));
    }

    [Theory(DisplayName = "AC-LEND-007-2: a non-librarian asking for the report of lent books is refused with FORBIDDEN")]
    [InlineData(Role.Member)]
    [InlineData(Role.Administrator)]
    public async Task AC_LEND_007_2(Role role)
    {
        Copies(Hobbit, 1);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: March1);

        var response = await _api.ClientAs(_api.User(role)).GetAsync("/v1/loans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
    }
}
