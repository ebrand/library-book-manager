using System.Net;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;
using LibraryBookManager.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryBookManager.Api.Tests.Auth;

/// <summary>Capability AUTH — REQ-AUTH-006 audit of sign-in, sign-out and role change.</summary>
public sealed class AuditTests
{
    private static List<AuditRecord> Audit(ApiFactory api) =>
        api.Query(db => db.AuditRecords.OrderBy(a => a.Id).ToList());

    [Fact(DisplayName = "AC-AUTH-006-1: an administrator changing a member to librarian leaves a record naming the administrator, the member, old role, new role and time")]
    public async Task AC_AUTH_006_1()
    {
        using var api = new ApiFactory();
        var admin = api.User(Role.Administrator);
        var member = api.User(Role.Member);
        var client = api.ClientAs(admin);
        api.Clock.Advance(TimeSpan.FromMinutes(7));
        var when = api.Clock.GetUtcNow();

        var response = await client.PutAsJsonAsync($"/v1/users/{member.PublicId}/role", new { role = "librarian" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(Audit(api), a => a.Event == AuditEvent.RoleChanged);
        Assert.Equal(admin.Id, record.ActorUserId);
        Assert.Equal(member.Id, record.AffectedUserId);
        Assert.Equal(Role.Member, record.OldRole);
        Assert.Equal(Role.Librarian, record.NewRole);
        Assert.Equal(when, record.OccurredAt);
    }

    public static TheoryData<string> RetentionSettings => new() { "default", "purge-enabled" };

    [Theory(DisplayName = "AC-AUTH-006-2: a record written two years ago less a day is still present after retention runs")]
    [MemberData(nameof(RetentionSettings))]
    public async Task AC_AUTH_006_2(string setting)
    {
        using var api = new ApiFactory(setting == "purge-enabled"
            ? new Dictionary<string, string> { ["Audit:PurgeAfterRetention"] = "true" }
            : null);
        var member = api.User(Role.Member);
        var writtenAt = api.Clock.GetUtcNow().AddYears(-2).AddDays(1);
        api.Seed(db => db.AuditRecords.Add(new AuditRecord
        {
            Event = AuditEvent.SignIn, OccurredAt = writtenAt, ActorUserId = member.Id, AffectedUserId = member.Id,
        }));

        await api.Service<AuditRetention>().RunAsync(CancellationToken.None);

        Assert.Contains(Audit(api), a => a.OccurredAt == writtenAt);
    }

    [Theory(DisplayName = "REQ-AUTH-006 (Q-AUTH-05): records past two years are deleted only when purging is switched on")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purge_only_when_enabled(bool purge)
    {
        using var api = new ApiFactory(purge ? new Dictionary<string, string> { ["Audit:PurgeAfterRetention"] = "true" } : null);
        var member = api.User(Role.Member);
        var old = api.Clock.GetUtcNow().AddYears(-2).AddDays(-1);
        api.Seed(db => db.AuditRecords.Add(new AuditRecord
        {
            Event = AuditEvent.SignIn, OccurredAt = old, ActorUserId = member.Id, AffectedUserId = member.Id,
        }));

        await api.Service<AuditRetention>().RunAsync(CancellationToken.None);

        Assert.Equal(!purge, Audit(api).Any(a => a.OccurredAt == old));
    }

    [Fact(DisplayName = "REQ-AUTH-006: sign-in and sign-out are recorded with the acting user, the affected user and the time")]
    public async Task Sign_in_and_out_recorded()
    {
        using var api = new ApiFactory();
        var member = api.User(Role.Member);
        var signedInAt = api.Clock.GetUtcNow();
        var client = api.ClientAs(member);
        api.Clock.Advance(TimeSpan.FromHours(1));

        await client.DeleteAsync("/v1/sessions/current");

        var records = Audit(api);
        var signIn = Assert.Single(records, a => a.Event == AuditEvent.SignIn);
        Assert.Equal((member.Id, member.Id, signedInAt), (signIn.ActorUserId, signIn.AffectedUserId, signIn.OccurredAt));
        var signOut = Assert.Single(records, a => a.Event == AuditEvent.SignOut);
        Assert.Equal((member.Id, member.Id, signedInAt.AddHours(1)), (signOut.ActorUserId, signOut.AffectedUserId, signOut.OccurredAt));
    }

    [Fact(DisplayName = "REQ-AUTH-006 (Q-AUTH-04): refused sign-ins are recorded too, never with the password tried")]
    public async Task Failed_sign_ins_recorded()
    {
        using var api = new ApiFactory();
        var member = api.User(Role.Member);

        await api.AnonymousClient().PostAsJsonAsync("/v1/sessions", new { email = member.Email, password = "guess-one" });
        await api.AnonymousClient().PostAsJsonAsync("/v1/sessions", new { email = "nobody@example.test", password = "guess-two" });

        var failed = Audit(api).Where(a => a.Event == AuditEvent.SignInRefused).ToList();
        Assert.Equal(2, failed.Count);
        Assert.Equal(member.Id, failed[0].AffectedUserId);
        Assert.Null(failed[0].ActorUserId);
        Assert.Null(failed[1].AffectedUserId);
        Assert.Equal("nobody@example.test", failed[1].AttemptedEmail);
        var everyText = failed.SelectMany(r => typeof(AuditRecord).GetProperties()
            .Select(p => p.GetValue(r)?.ToString() ?? ""));
        Assert.DoesNotContain(everyText, v => v.Contains("guess", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "REQ-AUTH-006: the retention job is scheduled to run in the service")]
    public void Retention_is_hosted()
    {
        using var api = new ApiFactory();
        Assert.Contains(api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is AuditRetentionHostedService);
    }
}
