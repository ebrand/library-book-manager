using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryBookManager.Api.Tests.Lend;

/// <summary>
/// LEND behaviour the criteria leave open, the declared gaps, and concurrency. Each decision
/// is recorded in QUESTIONS.md.
/// </summary>
public sealed class LendingEdgeTests : IDisposable
{
    private const string Hobbit = "9780261102217";
    private readonly ApiFactory _api = new();
    private readonly User _librarian;
    private readonly User _member;

    public LendingEdgeTests()
    {
        _librarian = _api.User(Role.Librarian);
        _member = _api.User(Role.Member);
    }

    public void Dispose() => _api.Dispose();

    private Task<HttpResponseMessage> CheckOut(User as_, string barcode, Guid memberId) =>
        _api.ClientAs(as_).PostAsJsonAsync("/v1/loans", new { barcode, memberId });

    [Theory(DisplayName = "REQ-LEND-001 (Q-LEND-02): only a librarian checks out or returns; administrators and members are FORBIDDEN")]
    [InlineData(Role.Member)]
    [InlineData(Role.Administrator)]
    public async Task Only_librarians_lend(Role role)
    {
        _api.Title(Hobbit, 1);
        var who = _api.User(role);

        var checkout = await CheckOut(who, $"{Hobbit}-1", _member.PublicId);
        var giveBack = await _api.ClientAs(who).PostAsync($"/v1/copies/{Hobbit}-1/return", null);

        Assert.Equal(HttpStatusCode.Forbidden, checkout.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, giveBack.StatusCode);
        Assert.Equal(0, _api.Query(db => db.Loans.Count()));
    }

    [Theory(DisplayName = "REQ-LEND-001 (Q-LEND-03): copies are lent only to users whose role is member; others are refused with BORROWER_NOT_A_MEMBER")]
    [InlineData(Role.Librarian)]
    [InlineData(Role.Administrator)]
    public async Task Borrower_must_be_member(Role role)
    {
        _api.Title(Hobbit, 1);

        var response = await CheckOut(_librarian, $"{Hobbit}-1", _api.User(role).PublicId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BORROWER_NOT_A_MEMBER", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-SYS-05): checking out to a user who does not exist is refused with USER_NOT_FOUND")]
    public async Task Unknown_member()
    {
        _api.Title(Hobbit, 1);
        var response = await CheckOut(_librarian, $"{Hobbit}-1", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("USER_NOT_FOUND", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-LEND-001: a barcode that was never in the catalogue is COPY_NOT_FOUND")]
    public async Task Unknown_copy()
    {
        var response = await CheckOut(_librarian, "NO-SUCH-COPY", _member.PublicId);
        Assert.Equal("COPY_NOT_FOUND", (await response.Read<ErrorBody>()).Error);
    }

    [Theory(DisplayName = "REQ-LEND-001 (Q-BOOK-02): barcode and memberId are both required")]
    [InlineData("""{"memberId":"00000000-0000-0000-0000-000000000001"}""", "barcode")]
    [InlineData("""{"barcode":"x"}""", "memberId")]
    public async Task Checkout_fields_required(string body, string field)
    {
        var response = await _api.ClientAs(_librarian).PostAsync("/v1/loans",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        var error = await response.Read<ErrorBody>();
        Assert.Equal(("FIELD_REQUIRED", field), (error.Error, error.Field));
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-LEND-01): 'the day' is the library's calendar day in its configured time zone")]
    public async Task Library_time_zone()
    {
        using var api = new ApiFactory(new Dictionary<string, string> { ["Library:TimeZone"] = "Pacific/Auckland" });
        var librarian = api.User(Role.Librarian);
        var member = api.User(Role.Member);
        api.Title(Hobbit, 1);
        // 12:00 UTC on 1 March is 01:00 on 2 March in Auckland (NZDT, UTC+13).
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

        var loan = await (await api.ClientAs(librarian).PostAsJsonAsync("/v1/loans",
            new { barcode = $"{Hobbit}-1", memberId = member.PublicId })).Read<LoanView>();

        Assert.Equal((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 23)), (loan.CheckedOutOn, loan.DueOn));
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-LEND-01): with no time zone configured, the day is the UTC day")]
    public async Task Default_time_zone_is_utc()
    {
        _api.Title(Hobbit, 1);
        _api.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero));

        var loan = await (await CheckOut(_librarian, $"{Hobbit}-1", _member.PublicId)).Read<LoanView>();

        Assert.Equal(new DateOnly(2026, 3, 1), loan.CheckedOutOn);
    }

    [Fact(DisplayName = "REQ-LEND-006 (Q-LEND-05): a member asking for loans of a user id that does not exist is FORBIDDEN, not told it is missing")]
    public async Task Member_cannot_probe_ids()
    {
        var response = await _api.ClientAs(_member).GetAsync($"/v1/members/{Guid.NewGuid()}/loans");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-LEND-006 (Q-LEND-05): an administrator asking for a member's loans is FORBIDDEN")]
    public async Task Administrator_cannot_see_loans()
    {
        var response = await _api.ClientAs(_api.User(Role.Administrator)).GetAsync($"/v1/members/{_member.PublicId}/loans");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-LEND-04): a librarian finds a member by exact email; members cannot use the lookup")]
    public async Task Member_lookup()
    {
        var found = await _api.ClientAs(_librarian).GetAsync($"/v1/members?email={Uri.EscapeDataString(_member.Email.ToUpperInvariant())}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var member = Assert.Single(await found.Read<List<MemberView>>());
        Assert.Equal(_member.PublicId, member.UserId);

        var partial = await _api.ClientAs(_librarian).GetAsync("/v1/members?email=member");
        Assert.Empty(await partial.Read<List<MemberView>>());
        var staff = await _api.ClientAs(_librarian).GetAsync($"/v1/members?email={Uri.EscapeDataString(_librarian.Email)}");
        Assert.Empty(await staff.Read<List<MemberView>>()); // only role member is a member

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientAs(_member).GetAsync($"/v1/members?email={_member.Email}")).StatusCode);
    }

    [Fact(DisplayName = "REQ-BOOK-003: a returned loan stays in the copy's history after the title is edited and the copy removed")]
    public async Task History_survives()
    {
        _api.Title(Hobbit, 1);
        await CheckOut(_librarian, $"{Hobbit}-1", _member.PublicId);
        await _api.ClientAs(_librarian).PostAsync($"/v1/copies/{Hobbit}-1/return", null);
        await _api.ClientAs(_librarian).PatchAsJsonAsync($"/v1/titles/{Hobbit}", new { author = "Changed" });
        Assert.Equal(HttpStatusCode.NoContent, (await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1")).StatusCode);

        var loan = _api.Query(db => db.Loans.Single());
        Assert.Equal(_member.Id, loan.MemberId);
        Assert.NotNull(loan.ReturnedOn);
    }

    // ---------------------------------------------------------------- declared gaps

    [Fact(DisplayName = "LEND gap (holds and reservations): no route offers holds, reservations or queueing")]
    public void No_holds()
    {
        var routes = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "").ToList();
        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, r => r.Contains("hold", StringComparison.OrdinalIgnoreCase)
                                           || r.Contains("reserv", StringComparison.OrdinalIgnoreCase)
                                           || r.Contains("queue", StringComparison.OrdinalIgnoreCase)
                                           || r.Contains("waitlist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "LEND gap (overdue fines and payment): loans carry no fine, fee, charge or payment, and the contract names none")]
    public async Task No_fines()
    {
        _api.Title(Hobbit, 1);
        _api.OpenLoan($"{Hobbit}-1", _member, checkedOut: new DateOnly(2026, 1, 1), due: new DateOnly(2026, 1, 22));
        var money = new[] { "fine", "fee", "charge", "payment", "amount", "balance", "owed", "penalt" };

        var json = await _api.ClientAs(_librarian).GetStringAsync("/v1/loans");
        var contract = await File.ReadAllTextAsync(Conformance.ApiContractTests.ContractPath);
        var loanColumns = typeof(Loan).GetProperties().Select(p => p.Name);

        Assert.Contains("\"overdue\":true", json);
        foreach (var word in money)
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(loanColumns, c => c.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
        Assert.DoesNotMatch(@"(?i)\b(fine|fines|fee|fees|payment|penalty)\b", contract);
    }

    // ---------------------------------------------------------------- concurrency (Q-LEND-06)

    private static ApiFactory FileBacked(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"lbm-concurrency-{Guid.NewGuid():N}.db");
        return new ApiFactory(new Dictionary<string, string> { ["ConnectionStrings:Library"] = $"Data Source={path}" });
    }

    private static void DeleteDb(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-LEND-06): the database itself refuses a second open loan of one copy")]
    public void One_open_loan_per_copy_in_the_store()
    {
        _api.Title(Hobbit, 1);
        _api.OpenLoan($"{Hobbit}-1", _member);

        var second = Assert.ThrowsAny<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            _api.OpenLoan($"{Hobbit}-1", _api.User(Role.Member)));
        Assert.Contains("UNIQUE", second.InnerException?.Message ?? "");

        // A returned loan does not block the next one.
        _api.Seed(db => db.Loans.Single().ReturnedOn = new DateOnly(2026, 3, 2));
        _api.OpenLoan($"{Hobbit}-1", _api.User(Role.Member));
        Assert.Equal(2, _api.Query(db => db.Loans.Count()));
    }

    [Fact(DisplayName = "REQ-LEND-001 (Q-LEND-06): twenty simultaneous checkouts of one copy produce exactly one loan")]
    public async Task Same_copy_race()
    {
        var api = FileBacked(out var path);
        try
        {
            var librarian = api.User(Role.Librarian);
            api.Title(Hobbit, 1);
            var members = Enumerable.Range(0, 20).Select(_ => api.User(Role.Member)).ToList();
            var client = api.ClientAs(librarian);

            var results = await Task.WhenAll(members.Select(m =>
                client.PostAsJsonAsync("/v1/loans", new { barcode = $"{Hobbit}-1", memberId = m.PublicId })));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(19, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, api.Query(db => db.Loans.Count()));
        }
        finally
        {
            api.Dispose();
            DeleteDb(path);
        }
    }

    [Fact(DisplayName = "REQ-LEND-002 (Q-LEND-06): simultaneous checkouts to a member holding nine produce exactly one tenth loan")]
    public async Task Limit_race()
    {
        var api = FileBacked(out var path);
        try
        {
            var librarian = api.User(Role.Librarian);
            var member = api.User(Role.Member);
            api.Title(Hobbit, 20);
            for (var i = 1; i <= 9; i++) api.OpenLoan($"{Hobbit}-{i}", member);
            var client = api.ClientAs(librarian);

            var results = await Task.WhenAll(Enumerable.Range(10, 11).Select(i =>
                client.PostAsJsonAsync("/v1/loans", new { barcode = $"{Hobbit}-{i}", memberId = member.PublicId })));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(10, api.Query(db => db.Loans.Count(l => l.MemberId == member.Id)));
        }
        finally
        {
            api.Dispose();
            DeleteDb(path);
        }
    }

    [Fact(DisplayName = "REQ-BOOK-004 (Q-BOOK-14): a copy removed and checked out at the same moment is never both removed and on loan")]
    public async Task Remove_versus_checkout_race()
    {
        for (var round = 0; round < 10; round++)
        {
            var api = FileBacked(out var path);
            try
            {
                var librarian = api.User(Role.Librarian);
                var member = api.User(Role.Member);
                api.Title(Hobbit, 1);
                var client = api.ClientAs(librarian);

                var checkout = client.PostAsJsonAsync("/v1/loans", new { barcode = $"{Hobbit}-1", memberId = member.PublicId });
                var remove = client.DeleteAsync($"/v1/copies/{Hobbit}-1");
                var (lent, removed) = (await checkout, await remove);

                Assert.False(lent.StatusCode == HttpStatusCode.Created && removed.StatusCode == HttpStatusCode.NoContent,
                    $"round {round}: both succeeded");
                Assert.True(lent.StatusCode == HttpStatusCode.Created || removed.StatusCode == HttpStatusCode.NoContent,
                    $"round {round}: neither succeeded ({lent.StatusCode}, {removed.StatusCode})");
                var copy = api.Query(db => db.Copies.Single());
                var openLoans = api.Query(db => db.Loans.Count(l => l.ReturnedOn == null));
                Assert.False(copy.RemovedAt is not null && openLoans > 0, $"round {round}: removed copy on loan");
            }
            finally
            {
                api.Dispose();
                DeleteDb(path);
            }
        }
    }
}
