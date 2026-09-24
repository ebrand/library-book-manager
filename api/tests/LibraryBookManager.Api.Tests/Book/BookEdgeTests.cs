using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Book;

/// <summary>
/// Behaviour the BOOK criteria do not pin down. Each decision here is recorded in
/// QUESTIONS.md; these tests hold the conservative choice in place until it is answered.
/// </summary>
public sealed class BookEdgeTests : IDisposable
{
    private const string Hobbit = "9780261102217";
    private readonly ApiFactory _api = new();
    private readonly User _librarian;

    public BookEdgeTests() => _librarian = _api.User(Role.Librarian);

    public void Dispose() => _api.Dispose();

    public static TheoryData<Role, string, string, string?> Mutations => new()
    {
        { Role.Member, "POST", "/v1/titles", """{"isbn":"9780000000002","title":"T","author":"A","publicationYear":2000}""" },
        { Role.Member, "PATCH", $"/v1/titles/{Hobbit}", """{"author":"X"}""" },
        { Role.Member, "POST", $"/v1/titles/{Hobbit}/copies", "{}" },
        { Role.Member, "DELETE", $"/v1/copies/{Hobbit}-1", null },
        { Role.Administrator, "POST", "/v1/titles", """{"isbn":"9780000000002","title":"T","author":"A","publicationYear":2000}""" },
        { Role.Administrator, "PATCH", $"/v1/titles/{Hobbit}", """{"author":"X"}""" },
        { Role.Administrator, "POST", $"/v1/titles/{Hobbit}/copies", "{}" },
        { Role.Administrator, "DELETE", $"/v1/copies/{Hobbit}-1", null },
    };

    [Theory(DisplayName = "REQ-BOOK-001..004 (Q-BOOK-01): only a librarian may change the catalogue")]
    [MemberData(nameof(Mutations))]
    public async Task Only_librarians_change_the_catalogue(Role role, string method, string path, string? body)
    {
        _api.Title(Hobbit, copies: 1);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await _api.ClientAs(_api.User(role)).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(1, _api.Query(db => db.Titles.Count()));
        Assert.Equal(1, _api.Query(db => db.Copies.Count(c => c.RemovedAt == null)));
        Assert.Equal("J. R. R. Tolkien", _api.Query(db => db.Titles.Single().Author));
    }

    [Theory(DisplayName = "REQ-BOOK-001 (Q-BOOK-02): each required field left empty is named in FIELD_REQUIRED")]
    [InlineData("""{"title":"T","author":"A","publicationYear":2000}""", "isbn")]
    [InlineData("""{"isbn":"9780000000002","title":"  ","author":"A","publicationYear":2000}""", "title")]
    [InlineData("""{"isbn":"9780000000002","title":"T","author":"","publicationYear":2000}""", "author")]
    public async Task Every_required_field_is_named(string body, string field)
    {
        var response = await _api.ClientAs(_librarian).PostAsync("/v1/titles",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.Equal("FIELD_REQUIRED", error.Error);
        Assert.Equal(field, error.Field);
    }

    [Fact(DisplayName = "REQ-BOOK-001 (Q-BOOK-03): a hyphenated form of an existing ISBN is detected as a duplicate")]
    public async Task Hyphenated_isbn_is_a_duplicate()
    {
        _api.Title(Hobbit);

        var response = await _api.ClientAs(_librarian).PostAsJsonAsync("/v1/titles",
            new { isbn = "978-0-261-10221-7", title = "T", author = "A", publicationYear = 1937 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ISBN_ALREADY_EXISTS", (await response.Read<ErrorBody>()).Error);
    }

    [Theory(DisplayName = "REQ-BOOK-001 (Q-BOOK-03): a value that is not an ISBN-10 or ISBN-13 shape is refused with INVALID_FIELD")]
    [InlineData("abc")]
    [InlineData("97802611022")]
    public async Task Malformed_isbn_is_refused(string isbn)
    {
        var response = await _api.ClientAs(_librarian).PostAsJsonAsync("/v1/titles",
            new { isbn, title = "T", author = "A", publicationYear = 1937 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.Equal("INVALID_FIELD", error.Error);
        Assert.Equal("isbn", error.Field);
    }

    [Fact(DisplayName = "REQ-BOOK-001 (Q-BOOK-04): a publication year that is not a whole number is refused with INVALID_FIELD")]
    public async Task Non_integer_year_is_refused()
    {
        var response = await _api.ClientAs(_librarian).PostAsJsonAsync("/v1/titles",
            new { isbn = Hobbit, title = "T", author = "A", publicationYear = "nineteen" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.Equal("INVALID_FIELD", error.Error);
        Assert.Equal("publicationYear", error.Field);
    }

    [Fact(DisplayName = "REQ-BOOK-002 (Q-BOOK-05): adding a copy to an ISBN not in the catalogue is refused with TITLE_NOT_FOUND")]
    public async Task Copy_for_unknown_title()
    {
        var response = await _api.ClientAs(_librarian).PostAsJsonAsync($"/v1/titles/{Hobbit}/copies", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("TITLE_NOT_FOUND", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-BOOK-002 (Q-BOOK-06): the barcode of a removed copy is never reassigned")]
    public async Task Removed_barcode_stays_reserved()
    {
        _api.Title(Hobbit, copies: 1);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _api.ClientAs(_librarian).DeleteAsync($"/v1/copies/{Hobbit}-1")).StatusCode);

        var response = await _api.ClientAs(_librarian)
            .PostAsJsonAsync($"/v1/titles/{Hobbit}/copies", new { barcode = $"{Hobbit}-1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BARCODE_IN_USE", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-BOOK-003 (Q-BOOK-07): resending the title's own ISBN in an update is not a change and is accepted")]
    public async Task Same_isbn_is_not_a_change()
    {
        _api.Title(Hobbit);

        var response = await _api.ClientAs(_librarian)
            .PatchAsJsonAsync($"/v1/titles/{Hobbit}", new { isbn = Hobbit, title = "The Hobbit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("The Hobbit", (await response.Read<TitleDetail>()).Title);
    }

    [Fact(DisplayName = "REQ-BOOK-003 (Q-BOOK-02): an update may not blank a required field")]
    public async Task Update_cannot_blank_a_field()
    {
        _api.Title(Hobbit);

        var response = await _api.ClientAs(_librarian)
            .PatchAsJsonAsync($"/v1/titles/{Hobbit}", new { author = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.Equal("FIELD_REQUIRED", error.Error);
        Assert.Equal("author", error.Field);
    }

    [Fact(DisplayName = "REQ-BOOK-005 (Q-BOOK-08): a search must name exactly one of title, author or isbn")]
    public async Task Search_needs_exactly_one_field()
    {
        var client = _api.ClientAs(_librarian);
        foreach (var query in new[] { "", "title=a&author=b" })
        {
            var response = await client.GetAsync($"/v1/titles?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("INVALID_SEARCH", (await response.Read<ErrorBody>()).Error);
        }
    }

    [Theory(DisplayName = "REQ-BOOK-005 (Q-BOOK-09): a page number below 1 or not a number is refused with INVALID_SEARCH")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("two")]
    public async Task Bad_page_number(string page)
    {
        var response = await _api.ClientAs(_librarian).GetAsync($"/v1/titles?author=x&page={page}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_SEARCH", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-BOOK-005 (Q-BOOK-10): matching is case-insensitive substring; LIKE wildcards in the term are literal")]
    public async Task Matching_semantics()
    {
        _api.Title("9780000000002", author: "Ursula K. Le Guin");
        _api.Title("9780000000019", author: "100% Pure");

        Assert.Equal(1, (await _api.ClientAs(_librarian).GetAsync("/v1/titles?author=le%20guin")
            .ContinueWith(t => t.Result.Read<SearchPage>()).Unwrap()).Total);
        Assert.Equal(1, (await _api.ClientAs(_librarian).GetAsync("/v1/titles?author=%25")
            .ContinueWith(t => t.Result.Read<SearchPage>()).Unwrap()).Total);
        Assert.Equal(0, (await _api.ClientAs(_librarian).GetAsync("/v1/titles?author=_")
            .ContinueWith(t => t.Result.Read<SearchPage>()).Unwrap()).Total);
    }
}
