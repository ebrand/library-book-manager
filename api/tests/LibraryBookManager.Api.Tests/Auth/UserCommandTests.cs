using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Auth;

/// <summary>
/// Operator commands for provisioning accounts (Q-AUTH-10): the specification has users
/// but no way to create one, so accounts are made by whoever runs the service.
/// </summary>
public sealed class UserCommandTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(int Exit, string Out, string Err)> Run(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await UserCommands.RunAsync(args, _api.Services, new StringReader(stdin), output, error);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact(DisplayName = "REQ-AUTH-004 (Q-AUTH-10): create-user makes an account with one role and a password read from standard input")]
    public async Task Create_user()
    {
        var (exit, output, _) = await Run("a long enough password\n", "create-user", "--email", "Head@Library.test", "--role", "administrator");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("a long enough password", output);
        var user = _api.Query(db => db.Users.Single());
        Assert.Equal(("head@library.test", Role.Administrator), (user.Email, user.Role));
        var signIn = await _api.AnonymousClient().PostAsJsonAsync("/v1/sessions",
            new { email = "head@library.test", password = "a long enough password" });
        Assert.Equal(HttpStatusCode.Created, signIn.StatusCode);
        Assert.Contains(_api.Query(db => db.AuditRecords.ToList()), a => a.Event == AuditEvent.UserCreated && a.AffectedUserId == user.Id);
    }

    [Theory(DisplayName = "REQ-AUTH-004 (Q-AUTH-10): create-user refuses bad input and creates nothing")]
    [InlineData("a long enough password\n", "--email", "x@example.test", "--role", "superuser")]
    [InlineData("a long enough password\n", "--email", "", "--role", "member")]
    [InlineData("a long enough password\n", "--email", "not-an-email", "--role", "member")]
    [InlineData("short\n", "--email", "x@example.test", "--role", "member")]
    [InlineData("", "--email", "x@example.test", "--role", "member")]
    [InlineData("a long enough password\n", "--role", "member")]
    public async Task Create_user_refuses(string stdin, params string[] args)
    {
        var (exit, _, error) = await Run(stdin, ["create-user", .. args]);

        Assert.NotEqual(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0, _api.Query(db => db.Users.Count()));
    }

    [Fact(DisplayName = "REQ-AUTH-004 (Q-AUTH-10): create-user refuses an email that already has an account")]
    public async Task Create_user_duplicate()
    {
        _api.User(Role.Member, email: "reader@example.test");

        var (exit, _, error) = await Run("a long enough password\n", "create-user", "--email", "READER@example.test", "--role", "librarian");

        Assert.NotEqual(0, exit);
        Assert.Contains("already", error);
        Assert.Equal(Role.Member, _api.Query(db => db.Users.Single().Role));
    }

    [Fact(DisplayName = "REQ-AUTH-002 (Q-AUTH-10): set-password refuses an unknown email and a short password")]
    public async Task Set_password_refuses()
    {
        _api.User(Role.Member, email: "reader@example.test");
        var before = _api.Query(db => db.Users.Single().PasswordHash);

        Assert.NotEqual(0, (await Run("a long enough password\n", "set-password", "--email", "nobody@example.test")).Exit);
        Assert.NotEqual(0, (await Run("short\n", "set-password", "--email", "reader@example.test")).Exit);
        Assert.Equal(before, _api.Query(db => db.Users.Single().PasswordHash));
    }

    [Fact(DisplayName = "Q-AUTH-10: an unknown command is refused")]
    public async Task Unknown_command()
    {
        Assert.NotEqual(0, (await Run("", "drop-everything")).Exit);
    }
}
