using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Auth;

/// <summary>Capability AUTH — REQ-AUTH-004 roles.</summary>
public sealed class AuthorizationTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static object Hobbit => new { isbn = "9780261102217", title = "The Hobbit", author = "J. R. R. Tolkien", publicationYear = 1937 };

    [Fact(DisplayName = "AC-AUTH-004-1: a member adding a title is rejected with FORBIDDEN and the catalogue is unchanged")]
    public async Task AC_AUTH_004_1()
    {
        _api.Title("9780000000002", copies: 1);
        var member = _api.User(Role.Member);

        var response = await _api.ClientAs(member).PostAsJsonAsync("/v1/titles", Hobbit);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(["9780000000002"], _api.Query(db => db.Titles.Select(t => t.Isbn).ToList()));
        Assert.Equal(1, _api.Query(db => db.Copies.Count()));
    }

    [Fact(DisplayName = "AC-AUTH-004-2: a librarian adding a title succeeds")]
    public async Task AC_AUTH_004_2()
    {
        var librarian = _api.User(Role.Librarian);

        var response = await _api.ClientAs(librarian).PostAsJsonAsync("/v1/titles", Hobbit);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("The Hobbit", _api.Query(db => db.Titles.Single(t => t.Isbn == "9780261102217").Name));
    }

    [Fact(DisplayName = "AC-AUTH-004-3: a librarian changing another user's role is rejected with FORBIDDEN")]
    public async Task AC_AUTH_004_3()
    {
        var librarian = _api.User(Role.Librarian);
        var member = _api.User(Role.Member);

        var response = await _api.ClientAs(librarian)
            .PutAsJsonAsync($"/v1/users/{member.PublicId}/role", new { role = "librarian" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(Role.Member, _api.Query(db => db.Users.Single(u => u.Id == member.Id).Role));
    }

    [Fact(DisplayName = "AC-AUTH-004-4: an administrator changing another user's role changes it, with effect on that user's next request")]
    public async Task AC_AUTH_004_4()
    {
        var admin = _api.User(Role.Administrator);
        var member = _api.User(Role.Member);
        var membersClient = _api.ClientAs(member); // signed in before the change

        var response = await _api.ClientAs(admin)
            .PutAsJsonAsync($"/v1/users/{member.PublicId}/role", new { role = "librarian" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = await response.Read<UserView>();
        Assert.Equal(member.PublicId, changed.UserId);
        Assert.Equal("librarian", changed.Role);
        Assert.Equal(Role.Librarian, _api.Query(db => db.Users.Single(u => u.Id == member.Id).Role));
        Assert.Equal(HttpStatusCode.Created, (await membersClient.PostAsJsonAsync("/v1/titles", Hobbit)).StatusCode);
    }

    [Fact(DisplayName = "REQ-AUTH-004: a demotion takes effect on the demoted user's existing session")]
    public async Task Demotion_is_immediate()
    {
        var admin = _api.User(Role.Administrator);
        var librarian = _api.User(Role.Librarian);
        var librariansClient = _api.ClientAs(librarian);

        await _api.ClientAs(admin).PutAsJsonAsync($"/v1/users/{librarian.PublicId}/role", new { role = "member" });

        Assert.Equal(HttpStatusCode.Forbidden, (await librariansClient.PostAsJsonAsync("/v1/titles", Hobbit)).StatusCode);
    }

    [Theory(DisplayName = "REQ-AUTH-004: the user list is for administrators only")]
    [InlineData(Role.Member)]
    [InlineData(Role.Librarian)]
    public async Task User_list_is_admin_only(Role role)
    {
        var response = await _api.ClientAs(_api.User(role)).GetAsync("/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-AUTH-004: an administrator lists every user with exactly one role each")]
    public async Task Admin_lists_users()
    {
        var admin = _api.User(Role.Administrator);
        var member = _api.User(Role.Member);
        var librarian = _api.User(Role.Librarian);

        var users = await (await _api.ClientAs(admin).GetAsync("/v1/users")).Read<List<UserView>>();

        Assert.Equal(3, users.Count);
        Assert.Equal("member", users.Single(u => u.UserId == member.PublicId).Role);
        Assert.Equal("librarian", users.Single(u => u.UserId == librarian.PublicId).Role);
        Assert.Equal("administrator", users.Single(u => u.UserId == admin.PublicId).Role);
    }

    [Theory(DisplayName = "REQ-AUTH-004 (Q-AUTH-07): a role outside member, librarian and administrator is refused with INVALID_FIELD")]
    [InlineData("\"superuser\"")]
    [InlineData("\"\"")]
    [InlineData("2")]
    public async Task Unknown_role_refused(string role)
    {
        var admin = _api.User(Role.Administrator);
        var member = _api.User(Role.Member);

        var response = await _api.ClientAs(admin).PutAsync($"/v1/users/{member.PublicId}/role",
            new StringContent($$"""{"role":{{role}}}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Read<ErrorBody>();
        Assert.True(error.Error is "INVALID_FIELD" or "FIELD_REQUIRED", error.Error);
        Assert.Equal("role", error.Field);
        Assert.Equal(Role.Member, _api.Query(db => db.Users.Single(u => u.Id == member.Id).Role));
    }

    [Fact(DisplayName = "REQ-AUTH-004 (Q-AUTH-06): an administrator cannot change their own role")]
    public async Task Own_role_refused()
    {
        var admin = _api.User(Role.Administrator);

        var response = await _api.ClientAs(admin).PutAsJsonAsync($"/v1/users/{admin.PublicId}/role", new { role = "member" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Read<ErrorBody>()).Error);
        Assert.Equal(Role.Administrator, _api.Query(db => db.Users.Single(u => u.Id == admin.Id).Role));
    }

    [Fact(DisplayName = "REQ-AUTH-004 (Q-SYS-05): changing the role of a user who does not exist is refused with USER_NOT_FOUND")]
    public async Task Unknown_user()
    {
        var admin = _api.User(Role.Administrator);

        var response = await _api.ClientAs(admin).PutAsJsonAsync($"/v1/users/{Guid.NewGuid()}/role", new { role = "member" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("USER_NOT_FOUND", (await response.Read<ErrorBody>()).Error);
    }
}
