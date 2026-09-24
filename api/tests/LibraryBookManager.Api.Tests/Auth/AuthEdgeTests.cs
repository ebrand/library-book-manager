using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Auth;

/// <summary>
/// AUTH behaviour the criteria leave open. Each decision is recorded in QUESTIONS.md; these
/// tests hold the conservative choice in place until it is answered.
/// </summary>
public sealed class AuthEdgeTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private Task<HttpResponseMessage> SignIn(string email, string password) =>
        _api.AnonymousClient().PostAsJsonAsync("/v1/sessions", new { email, password });

    private async Task<string> Outcome(string email, string password)
    {
        var response = await SignIn(email, password);
        return response.StatusCode == HttpStatusCode.Created ? "OK" : (await response.Read<ErrorBody>()).Error;
    }

    [Fact(DisplayName = "REQ-AUTH-003 (Q-AUTH-02): an email with no account locks after five failures exactly as a real account does")]
    public async Task Unknown_email_locks_identically()
    {
        var member = _api.User(Role.Member);
        var real = new List<string>();
        var unknown = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            real.Add(await Outcome(member.Email, "wrong"));
            unknown.Add(await Outcome("nobody@example.test", "wrong"));
        }

        Assert.Equal(real, unknown);
        Assert.Equal(["INVALID_CREDENTIALS", "INVALID_CREDENTIALS", "INVALID_CREDENTIALS", "INVALID_CREDENTIALS", "ACCOUNT_LOCKED", "ACCOUNT_LOCKED"], real);
    }

    [Fact(DisplayName = "REQ-AUTH-003 (Q-AUTH-03): attempts while locked are refused but neither counted nor extend the lock; the count restarts afterwards")]
    public async Task Attempts_during_lock()
    {
        var member = _api.User(Role.Member);
        for (var i = 0; i < 5; i++) await Outcome(member.Email, "wrong");

        _api.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("ACCOUNT_LOCKED", await Outcome(member.Email, "wrong"));
        _api.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal("INVALID_CREDENTIALS", await Outcome(member.Email, "wrong")); // not locked again: count restarted
        Assert.Equal("OK", await Outcome(member.Email, ApiFactory.Password));
    }

    [Fact(DisplayName = "REQ-AUTH-001 (Q-AUTH-08): email addresses are matched without regard to case or surrounding space")]
    public async Task Email_case_insensitive()
    {
        var member = _api.User(Role.Member, email: "reader@example.test");
        Assert.Equal("OK", await Outcome("  Reader@Example.TEST ", ApiFactory.Password));
    }

    [Theory(DisplayName = "REQ-AUTH-001 (Q-AUTH-09): an empty email or password is FIELD_REQUIRED and is not counted as a failed attempt")]
    [InlineData("", "x", "email")]
    [InlineData("reader@example.test", "", "password")]
    public async Task Empty_fields(string email, string password, string field)
    {
        _api.User(Role.Member, email: "reader@example.test");

        for (var i = 0; i < 6; i++)
        {
            var response = await SignIn(email, password);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Read<ErrorBody>();
            Assert.Equal(("FIELD_REQUIRED", field), (error.Error, error.Field));
        }
        Assert.Equal("OK", await Outcome("reader@example.test", ApiFactory.Password));
    }

    [Fact(DisplayName = "REQ-AUTH-005 (Q-AUTH-01): the session token is not stored; only a hash of it is")]
    public async Task Token_not_stored()
    {
        var member = _api.User(Role.Member);
        var token = _api.SignIn(member.Email);

        var stored = _api.Query(db => db.Sessions.Single());
        var everyText = typeof(Session).GetProperties().Select(p => p.GetValue(stored)?.ToString() ?? "");
        Assert.DoesNotContain(everyText, v => v.Contains(token, StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Theory(DisplayName = "REQ-AUTH-005: a credential that is not a session we issued is rejected with SESSION_INVALID")]
    [InlineData("Bearer", "made-up-token")]
    [InlineData("Basic", "dXNlcjpwYXNz")]
    [InlineData("Bearer", "")]
    public async Task Unknown_credential(string scheme, string value)
    {
        var client = _api.AnonymousClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{scheme} {value}".Trim());

        var response = await client.GetAsync("/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_INVALID", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-AUTH-005: with no credential at all the request is refused with UNAUTHENTICATED")]
    public async Task No_credential()
    {
        var response = await _api.AnonymousClient().GetAsync("/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHENTICATED", (await response.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-AUTH-005: signing out ends only that session, not the user's others")]
    public async Task Sign_out_is_per_session()
    {
        var member = _api.User(Role.Member);
        var first = _api.ClientWithToken(_api.SignIn(member.Email));
        var second = _api.ClientWithToken(_api.SignIn(member.Email));

        await first.DeleteAsync("/v1/sessions/current");

        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/v1/users/me")).StatusCode);
    }

    [Fact(DisplayName = "REQ-AUTH-002: production hashing uses PBKDF2-SHA256 with 600,000 iterations unless configured otherwise")]
    public void Production_iterations()
    {
        var hasher = new PasswordHasher(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var hash = hasher.Hash("x");
        Assert.Equal("600000", hash.Split('$')[1]);
        Assert.True(hasher.Verify("x", hash));
        Assert.False(hasher.Verify("y", hash));
    }

    [Fact(DisplayName = "REQ-AUTH-002: a hash made with a different iteration count still verifies")]
    public void Old_hashes_verify()
    {
        var old = _api.Service<PasswordHasher>().Hash("x");
        var production = new PasswordHasher(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.True(production.Verify("x", old));
    }
}
