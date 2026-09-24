using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Book;

/// <summary>Capability BOOK — Book records. One test (at least) per acceptance criterion.</summary>
public sealed class BookRecordsTests : IDisposable
{
    private const string Hobbit = "9780261102217";
    private readonly ApiFactory _api = new();
    private readonly User _librarian;
    private readonly User _member;

    public BookRecordsTests()
    {
        _librarian = _api.User(Role.Librarian);
        _member = _api.User(Role.Member);
    }

    public void Dispose() => _api.Dispose();

    private static object NewTitle(string isbn, object? publicationYear) => new
    {
        isbn,
        title = "The Hobbit",
        author = "J. R. R. Tolkien",
        publicationYear,
    };

    private async Task<TitleDetail> GetTitle(string isbn)
    {
        var response = await _api.ClientAs(_librarian).GetAsync($"/v1/titles/{isbn}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Read<TitleDetail>();
    }

    private async Task<SearchPage> Search(User who, string query)
    {
        var response = await _api.ClientAs(who).GetAsync($"/v1/titles?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Read<SearchPage>();
    }

    // ---------------------------------------------------------------- REQ-BOOK-001

    [Fact(DisplayName = "AC-BOOK-001-1: adding a new ISBN puts the title in the catalogue with zero copies, findable by ISBN")]
    public async Task AC_BOOK_001_1()
    {
        var response = await _api.ClientAs(_librarian).PostAsJsonAsync("/v1/titles", NewTitle(Hobbit, 1937));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var found = await Search(_librarian, $"isbn={Hobbit}");
        var title = Assert.Single(found.Items);
        Assert.Equal(1, found.Total);
        Assert.Equal(Hobbit, title.Isbn);
        Assert.Equal("The Hobbit", title.Title);
        Assert.Equal("J. R. R. Tolkien", title.Author);
        Assert.Equal(1937, title.PublicationYear);
        Assert.Equal(0, title.CopyCount);
    }

    [Fact(DisplayName = "AC-BOOK-001-2: adding an ISBN already in the catalogue is rejected with ISBN_ALREADY_EXISTS and nothing changes")]
    public async Task AC_BOOK_001_2()
    {
        _api.Title(Hobbit, copies: 1, title: "Original", author: "Original Author", year: 1937);

        var response = await _api.ClientAs(_librarian).PostAsJsonAsync("/v1/titles",
            new { isbn = Hobbit, title = "Replacement", author = "Someone Else", publicationYear = 2001 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ISBN_ALREADY_EXISTS", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(1, _api.Query(db => db.Titles.Count()));
        var title = await GetTitle(Hobbit);
        Assert.Equal("Original", title.Title);
        Assert.Equal("Original Author", title.Author);
        Assert.Equal(1937, title.PublicationYear);
        Assert.Equal(1, title.CopyCount);
    }

    public static TheoryData<string> EmptyYearBodies => new()
    {
        """{"isbn":"9780000000002","title":"T","author":"A","publicationYear":null}""",
        """{"isbn":"9780000000002","title":"T","author":"A","publicationYear":""}""",
        """{"isbn":"9780000000002","title":"T","author":"A"}""",
    };

    [Theory(DisplayName = "AC-BOOK-001-3: adding a title with the publication year left empty is rejected with FIELD_REQUIRED naming publicationYear")]
    [MemberData(nameof(EmptyYearBodies))]
    public async Task AC_BOOK_001_3(string body)
    {
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _api.ClientAs(_librarian).PostAsync("/v1/titles", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.Equal("FIELD_REQUIRED", error.Error);
        Assert.Equal("publicationYear", error.Field);
        Assert.Equal(0, _api.Query(db => db.Titles.Count()));
    }

    // ---------------------------------------------------------------- REQ-BOOK-002

    [Fact(DisplayName = "AC-BOOK-002-1: adding a third copy makes the title report three copies, the new barcode held by no other copy")]
    public async Task AC_BOOK_002_1()
    {
        _api.Title(Hobbit, copies: 2);
        _api.Title("9780000000019", copies: 3); // copies of another title also count toward "no other copy"

        var response = await _api.ClientAs(_librarian).PostAsJsonAsync($"/v1/titles/{Hobbit}/copies", new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Read<CopyView>();
        Assert.False(string.IsNullOrWhiteSpace(created.Barcode));
        var title = await GetTitle(Hobbit);
        Assert.Equal(3, title.CopyCount);
        Assert.Contains(title.Copies, c => c.Barcode == created.Barcode);
        Assert.Equal(1, _api.Query(db => db.Copies.Count(c => c.Barcode == created.Barcode)));
        Assert.Equal(6, _api.Query(db => db.Copies.Select(c => c.Barcode).Distinct().Count()));
    }

    [Fact(DisplayName = "AC-BOOK-002-2: adding a copy with a barcode already in use is rejected with BARCODE_IN_USE")]
    public async Task AC_BOOK_002_2()
    {
        _api.Title("9780000000019", copies: 1); // holds barcode 9780000000019-1
        _api.Title(Hobbit, copies: 1);

        var response = await _api.ClientAs(_librarian)
            .PostAsJsonAsync($"/v1/titles/{Hobbit}/copies", new { barcode = "9780000000019-1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BARCODE_IN_USE", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(1, (await GetTitle(Hobbit)).CopyCount);
        Assert.Equal(1, _api.Query(db => db.Copies.Count(c => c.Barcode == "9780000000019-1")));
    }

    // ---------------------------------------------------------------- REQ-BOOK-003

    [Fact(DisplayName = "AC-BOOK-003-1: correcting the author keeps the ISBN, all three copies and the open loan")]
    public async Task AC_BOOK_003_1()
    {
        _api.Title(Hobbit, copies: 3, author: "J. R. R. Tolkein");
        var loan = _api.OpenLoan($"{Hobbit}-2", _member);

        var response = await _api.ClientAs(_librarian)
            .PatchAsJsonAsync($"/v1/titles/{Hobbit}", new { author = "J. R. R. Tolkien" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var title = await GetTitle(Hobbit);
        Assert.Equal("J. R. R. Tolkien", title.Author);
        Assert.Equal(Hobbit, title.Isbn);
        Assert.Equal(3, title.CopyCount);
        Assert.True(title.Copies.Single(c => c.Barcode == $"{Hobbit}-2").OnLoan);
        var stored = _api.Query(db => db.Loans.Single(l => l.Id == loan.Id));
        Assert.Null(stored.ReturnedOn);
        Assert.Equal(loan.CopyId, stored.CopyId);
        Assert.Equal(_member.Id, stored.MemberId);
        Assert.Equal(loan.DueOn, stored.DueOn);
    }

    [Fact(DisplayName = "AC-BOOK-003-2: attempting to change a title's ISBN is rejected with ISBN_IMMUTABLE")]
    public async Task AC_BOOK_003_2()
    {
        _api.Title(Hobbit, copies: 1);

        var response = await _api.ClientAs(_librarian)
            .PatchAsJsonAsync($"/v1/titles/{Hobbit}", new { isbn = "9780000000019", author = "Changed" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ISBN_IMMUTABLE", (await response.Read<ErrorBody>()).Error);
        var title = await GetTitle(Hobbit);
        Assert.Equal("J. R. R. Tolkien", title.Author); // the whole request is refused, not half-applied
        Assert.Equal(404, (int)(await _api.ClientAs(_librarian).GetAsync("/v1/titles/9780000000019")).StatusCode);
    }

    // ---------------------------------------------------------------- REQ-BOOK-004

    [Fact(DisplayName = "AC-BOOK-004-1: removing a copy on the shelf takes it out of the catalogue; the title reports one fewer copy")]
    public async Task AC_BOOK_004_1()
    {
        _api.Title(Hobbit, copies: 2);

        var response = await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var title = await GetTitle(Hobbit);
        Assert.Equal(1, title.CopyCount);
        Assert.DoesNotContain(title.Copies, c => c.Barcode == $"{Hobbit}-1");
        Assert.Equal(HttpStatusCode.NotFound,
            (await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1")).StatusCode);
    }

    [Fact(DisplayName = "AC-BOOK-004-2: removing a copy on loan is rejected with COPY_ON_LOAN and the loan is unchanged")]
    public async Task AC_BOOK_004_2()
    {
        _api.Title(Hobbit, copies: 1);
        var loan = _api.OpenLoan($"{Hobbit}-1", _member);

        var response = await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("COPY_ON_LOAN", (await response.Read<ErrorBody>()).Error);
        var stored = _api.Query(db => db.Loans.Single(l => l.Id == loan.Id));
        Assert.Null(stored.ReturnedOn);
        Assert.Equal(loan.DueOn, stored.DueOn);
        Assert.Equal(_member.Id, stored.MemberId);
        var title = await GetTitle(Hobbit);
        Assert.Equal(1, title.CopyCount);
        Assert.True(title.Copies.Single().OnLoan);
    }

    [Fact(DisplayName = "AC-BOOK-004-3: removing a title's last copy leaves the title listed with zero copies and marked unavailable")]
    public async Task AC_BOOK_004_3()
    {
        _api.Title(Hobbit, copies: 1);

        var response = await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var found = await Search(_member, $"isbn={Hobbit}");
        var title = Assert.Single(found.Items);
        Assert.Equal(0, title.CopyCount);
        Assert.Equal(0, title.AvailableCopies);
        Assert.False(title.Available);
    }

    // ---------------------------------------------------------------- REQ-BOOK-005

    private void GivenLeGuinCatalogue()
    {
        _api.Seed(db =>
        {
            for (var i = 1; i <= 120; i++)
                db.Titles.Add(new Title
                {
                    Isbn = $"978000{i:D7}",
                    Name = $"Earthsea volume {i:D3}",
                    Author = "Le Guin",
                    PublicationYear = 1968,
                });
            // Titles that must not match an author search for "Le Guin".
            db.Titles.Add(new Title { Isbn = "9789990000001", Name = "Le Guin: a biography", Author = "Someone Else", PublicationYear = 2020 });
            db.Titles.Add(new Title { Isbn = "9789990000002", Name = "Dune", Author = "Frank Herbert", PublicationYear = 1965 });
        });
    }

    [Fact(DisplayName = "AC-BOOK-005-1: an author search without a page returns the first 50 of 120 matches and a total of 120")]
    public async Task AC_BOOK_005_1()
    {
        GivenLeGuinCatalogue();

        var page = await Search(_member, "author=Le%20Guin");

        Assert.Equal(120, page.Total);
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.All(page.Items, t => Assert.Equal("Le Guin", t.Author));
        Assert.Equal(
            Enumerable.Range(1, 50).Select(i => $"Earthsea volume {i:D3}"),
            page.Items.Select(t => t.Title));
    }

    [Fact(DisplayName = "AC-BOOK-005-2: the third page of the same search returns matches 101 to 120 and a total of 120")]
    public async Task AC_BOOK_005_2()
    {
        GivenLeGuinCatalogue();

        var page = await Search(_member, "author=Le%20Guin&page=3");

        Assert.Equal(120, page.Total);
        Assert.Equal(3, page.Page);
        Assert.Equal(
            Enumerable.Range(101, 20).Select(i => $"Earthsea volume {i:D3}"),
            page.Items.Select(t => t.Title));
        // The three pages together hold every match exactly once.
        var second = await Search(_member, "author=Le%20Guin&page=2");
        var first = await Search(_member, "author=Le%20Guin&page=1");
        Assert.Equal(120, first.Items.Concat(second.Items).Concat(page.Items).Select(t => t.Isbn).Distinct().Count());
    }

    [Theory(DisplayName = "AC-BOOK-005-3: a search with no match returns an empty result with a total of 0, not an error")]
    [InlineData("title=Nonexistent")]
    [InlineData("author=Nobody%20At%20All")]
    [InlineData("isbn=9789999999999")]
    public async Task AC_BOOK_005_3(string query)
    {
        GivenLeGuinCatalogue();

        var page = await Search(_member, query);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact(DisplayName = "REQ-BOOK-005: search by title matches on the title field")]
    public async Task REQ_BOOK_005_title_search()
    {
        GivenLeGuinCatalogue();

        var page = await Search(_member, "title=Dune");

        Assert.Equal("9789990000002", Assert.Single(page.Items).Isbn);
    }

    [Theory(DisplayName = "REQ-BOOK-005: every authenticated role may search")]
    [InlineData(Role.Member)]
    [InlineData(Role.Librarian)]
    [InlineData(Role.Administrator)]
    public async Task REQ_BOOK_005_any_authenticated_user(Role role)
    {
        _api.Title(Hobbit);
        var page = await Search(_api.User(role), $"isbn={Hobbit}");
        Assert.Equal(1, page.Total);
    }

    [Fact(DisplayName = "REQ-BOOK-005: an unauthenticated search is rejected")]
    public async Task REQ_BOOK_005_unauthenticated()
    {
        _api.Title(Hobbit);
        var response = await _api.AnonymousClient().GetAsync($"/v1/titles?isbn={Hobbit}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-BOOK-005: no page is ever larger than 50")]
    public async Task REQ_BOOK_005_page_cap()
    {
        GivenLeGuinCatalogue();
        var response = await _api.ClientAs(_member).GetAsync("/v1/titles?author=Le%20Guin&pageSize=500");
        Assert.True((await response.Read<SearchPage>()).Items.Count <= 50);
    }

    // ---------------------------------------------------------------- REQ-BOOK-006

    [Fact(DisplayName = "AC-BOOK-006-1: a title with three copies, two on loan, is reported as one available of three")]
    public async Task AC_BOOK_006_1()
    {
        _api.Title(Hobbit, copies: 3);
        _api.OpenLoan($"{Hobbit}-1", _member);
        _api.OpenLoan($"{Hobbit}-3", _api.User(Role.Member));

        var title = Assert.Single((await Search(_member, $"isbn={Hobbit}")).Items);

        Assert.Equal(3, title.CopyCount);
        Assert.Equal(1, title.AvailableCopies);
        Assert.True(title.Available);
    }

    [Fact(DisplayName = "AC-BOOK-006-2: a title whose every copy is on loan is still listed, with zero available")]
    public async Task AC_BOOK_006_2()
    {
        _api.Title(Hobbit, copies: 2);
        _api.OpenLoan($"{Hobbit}-1", _member);
        _api.OpenLoan($"{Hobbit}-2", _member);

        var title = Assert.Single((await Search(_member, "author=Tolkien")).Items);

        Assert.Equal(Hobbit, title.Isbn);
        Assert.Equal(2, title.CopyCount);
        Assert.Equal(0, title.AvailableCopies);
        Assert.False(title.Available);
    }

    [Fact(DisplayName = "REQ-BOOK-006: a returned loan no longer counts against availability")]
    public async Task REQ_BOOK_006_returned_loan()
    {
        _api.Title(Hobbit, copies: 1);
        var loan = _api.OpenLoan($"{Hobbit}-1", _member);
        _api.Seed(db => db.Loans.Single(l => l.Id == loan.Id).ReturnedOn = new DateOnly(2026, 3, 5));

        var title = Assert.Single((await Search(_member, $"isbn={Hobbit}")).Items);

        Assert.Equal(1, title.AvailableCopies);
    }
}
