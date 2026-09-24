using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Auth;

/// <summary>Capability AUTH — REQ-AUTH-001, -002, -003, -005.</summary>
public sealed class AuthenticationTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private Task<HttpResponseMessage> SignIn(string email, string password) =>
        _api.AnonymousClient().PostAsJsonAsync("/v1/sessions", new { email, password });

    private async Task FailTimes(string email, int times)
    {
        for (var i = 0; i < times; i++)
            Assert.Equal("INVALID_CREDENTIALS", (await (await SignIn(email, "wrong password")).Read<ErrorBody>()).Error);
    }

    private SignInThrottle? Throttle(string email) =>
        _api.Query(db => db.SignInThrottles.SingleOrDefault(t => t.Email == email));

    // ---------------------------------------------------------------- REQ-AUTH-001

    [Fact(DisplayName = "AC-AUTH-001-1: signing in with a known email and password issues a session, and requests carrying it are attributed to that member")]
    public async Task AC_AUTH_001_1()
    {
        var member = _api.User(Role.Member);

        var response = await SignIn(member.Email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Read<SessionView>();
        Assert.False(string.IsNullOrWhiteSpace(session.Token));
        Assert.Equal(member.PublicId, session.User.UserId);
        Assert.Equal("member", session.User.Role);

        var me = await _api.ClientWithToken(session.Token).GetAsync("/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var attributed = await me.Read<UserView>();
        Assert.Equal(member.PublicId, attributed.UserId);
        Assert.Equal(member.Email, attributed.Email);
    }

    [Fact(DisplayName = "AC-AUTH-001-2: a wrong password is rejected with INVALID_CREDENTIALS and no session is issued")]
    public async Task AC_AUTH_001_2()
    {
        var member = _api.User(Role.Member);

        var response = await SignIn(member.Email, "not the password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("INVALID_CREDENTIALS", JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _api.Query(db => db.Sessions.Count()));
    }

    [Fact(DisplayName = "AC-AUTH-001-3: an email with no account is rejected with INVALID_CREDENTIALS, worded identically to a wrong password")]
    public async Task AC_AUTH_001_3()
    {
        var member = _api.User(Role.Member);

        var wrongPassword = await SignIn(member.Email, "not the password");
        var noAccount = await SignIn("nobody@example.test", "not the password");

        Assert.Equal(wrongPassword.StatusCode, noAccount.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await noAccount.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_CREDENTIALS", (await noAccount.Read<ErrorBody>()).Error);
        Assert.Equal(0, _api.Query(db => db.Sessions.Count()));
    }

    // ---------------------------------------------------------------- REQ-AUTH-002

    [Fact(DisplayName = "AC-AUTH-002-1: a member who has just set a password is stored with a salted hash, not the password")]
    public async Task AC_AUTH_002_1()
    {
        const string password = "a brand new password";
        var member = _api.User(Role.Member);
        var twin = _api.User(Role.Member);
        foreach (var user in new[] { member, twin })
            Assert.Equal(0, await UserCommands.RunAsync(["set-password", "--email", user.Email], _api.Services,
                new StringReader(password + "\n"), TextWriter.Null, TextWriter.Null));

        var stored = _api.Query(db => db.Users.Single(u => u.Id == member.Id));
        var twinStored = _api.Query(db => db.Users.Single(u => u.Id == twin.Id));

        // Nothing stored for the user contains the password.
        var everyText = typeof(User).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(stored) ?? "");
        Assert.DoesNotContain(everyText, v => v.Contains(password, StringComparison.Ordinal));
        // It is a PBKDF2 hash with its own salt: the same password gives a different stored value per user.
        var parts = stored.PasswordHash.Split('$');
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(Convert.FromBase64String(parts[2]).Length >= 16, "salt of at least 128 bits");
        Assert.NotEqual(stored.PasswordHash, twinStored.PasswordHash);
        Assert.NotEqual(parts[2], twinStored.PasswordHash.Split('$')[2]);
        // And it is the password that was set: signing in with it works, the old one does not.
        Assert.Equal(HttpStatusCode.Created, (await SignIn(member.Email, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(member.Email, ApiFactory.Password)).StatusCode);
    }

    [Fact(DisplayName = "AC-AUTH-002-2: no endpoint that returns a user record carries a password or hash field, whichever role calls it")]
    public async Task AC_AUTH_002_2()
    {
        var member = _api.User(Role.Member);
        var librarian = _api.User(Role.Librarian);
        var admin = _api.User(Role.Administrator);
        var hashes = _api.Query(db => db.Users.Select(u => u.PasswordHash).ToList());

        var bodies = new List<(string What, string Json)>();
        foreach (var user in new[] { member, librarian, admin })
        {
            var signIn = await SignIn(user.Email, ApiFactory.Password);
            bodies.Add(($"sign-in as {user.Role}", await signIn.Content.ReadAsStringAsync()));
            var client = _api.ClientAs(user);
            bodies.Add(($"me as {user.Role}", await client.GetStringAsync("/v1/users/me")));
            bodies.Add(($"list as {user.Role}", await (await client.GetAsync("/v1/users")).Content.ReadAsStringAsync()));
            bodies.Add(($"role change as {user.Role}", await (await client.PutAsJsonAsync(
                $"/v1/users/{member.PublicId}/role", new { role = "member" })).Content.ReadAsStringAsync()));
        }

        Assert.Contains(bodies, b => b.What == "list as Administrator" && b.Json.Contains(member.Email));
        foreach (var (what, json) in bodies)
        {
            Assert.DoesNotContain(PropertyNames(JsonDocument.Parse(json).RootElement),
                name => name.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("hash", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("salt", StringComparison.OrdinalIgnoreCase));
            Assert.False(json.Contains(ApiFactory.Password, StringComparison.Ordinal), what);
            Assert.False(hashes.Any(h => json.Contains(h.Split('$')[3], StringComparison.Ordinal)), what);
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    // ---------------------------------------------------------------- REQ-AUTH-003

    [Fact(DisplayName = "AC-AUTH-003-1: after four consecutive failures, a fifth failure locks the account and reports ACCOUNT_LOCKED")]
    public async Task AC_AUTH_003_1()
    {
        var member = _api.User(Role.Member);
        await FailTimes(member.Email, 4);

        var fifth = await SignIn(member.Email, "wrong password");

        Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", (await fifth.Read<ErrorBody>()).Error);
        Assert.Equal(_api.Clock.GetUtcNow().AddMinutes(15), Throttle(member.Email)!.LockedUntil);
        Assert.Equal(0, _api.Query(db => db.Sessions.Count()));
    }

    [Fact(DisplayName = "AC-AUTH-003-2: a locked account is refused with ACCOUNT_LOCKED even with the correct password, throughout the 15 minutes")]
    public async Task AC_AUTH_003_2()
    {
        var member = _api.User(Role.Member);
        await FailTimes(member.Email, 4);
        await SignIn(member.Email, "wrong password"); // fifth: locks

        foreach (var elapsed in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1) })
        {
            var clock = _api.Clock;
            var lockedAt = Throttle(member.Email)!.LockedUntil!.Value.AddMinutes(-15);
            clock.SetUtcNow(lockedAt + elapsed);

            var response = await SignIn(member.Email, ApiFactory.Password);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("ACCOUNT_LOCKED", (await response.Read<ErrorBody>()).Error);
        }
        Assert.Equal(0, _api.Query(db => db.Sessions.Count()));
    }

    [Fact(DisplayName = "AC-AUTH-003-3: after four consecutive failures, a successful fifth attempt issues a session and the failed count returns to zero")]
    public async Task AC_AUTH_003_3()
    {
        var member = _api.User(Role.Member);
        await FailTimes(member.Email, 4);

        var fifth = await SignIn(member.Email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Created, fifth.StatusCode);
        Assert.Equal(0, Throttle(member.Email)?.ConsecutiveFailures ?? 0);
        // The count really restarted: four more failures do not lock.
        await FailTimes(member.Email, 4);
        Assert.Equal(HttpStatusCode.Created, (await SignIn(member.Email, ApiFactory.Password)).StatusCode);
    }

    [Fact(DisplayName = "REQ-AUTH-003: the lock ends 15 minutes after it began")]
    public async Task Lock_lasts_fifteen_minutes()
    {
        var member = _api.User(Role.Member);
        await FailTimes(member.Email, 4);
        await SignIn(member.Email, "wrong password");

        _api.Clock.Advance(TimeSpan.FromMinutes(15));

        Assert.Equal(HttpStatusCode.Created, (await SignIn(member.Email, ApiFactory.Password)).StatusCode);
    }

    // ---------------------------------------------------------------- REQ-AUTH-005

    [Fact(DisplayName = "AC-AUTH-005-1: after signing out, reusing the session is rejected with SESSION_INVALID")]
    public async Task AC_AUTH_005_1()
    {
        var member = _api.User(Role.Member);
        var token = _api.SignIn(member.Email);
        var client = _api.ClientWithToken(token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/users/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/v1/sessions/current")).StatusCode);
        var reuse = await client.GetAsync("/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("SESSION_INVALID", (await reuse.Read<ErrorBody>()).Error);
        var search = await client.GetAsync("/v1/titles?title=x");
        Assert.Equal("SESSION_INVALID", (await search.Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "AC-AUTH-005-2: a session unused for 30 days is rejected with SESSION_EXPIRED")]
    public async Task AC_AUTH_005_2()
    {
        var member = _api.User(Role.Member);
        var client = _api.ClientWithToken(_api.SignIn(member.Email));

        _api.Clock.Advance(TimeSpan.FromDays(30));
        var response = await client.GetAsync("/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_EXPIRED", (await response.Read<ErrorBody>()).Error);
        // Expiry is final: presenting it again does not revive it.
        Assert.Equal("SESSION_EXPIRED", (await (await client.GetAsync("/v1/users/me")).Read<ErrorBody>()).Error);
    }

    [Fact(DisplayName = "REQ-AUTH-005: idle time is measured from last use, so a session used within every 30 days stays valid")]
    public async Task Use_keeps_a_session_alive()
    {
        var member = _api.User(Role.Member);
        var client = _api.ClientWithToken(_api.SignIn(member.Email));

        for (var i = 0; i < 3; i++)
        {
            _api.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/users/me")).StatusCode);
        }
    }
}
