using System.Net.Http.Headers;
using System.Text;
using LibraryBookManager.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibraryBookManager.Api.Tests.Security;

/// <summary>org-sec-004 REQ-SEC-013 — no secrets, credentials or cardholder data in logs.</summary>
public sealed class LogRedactionTests : IDisposable
{
    private const string CardNumber = "4111111111111111";
    private const string BearerSecret = "s3cr3t-bearer-value-7f2c";
    private const string PasswordSecret = "pa55word-never-logged-91";
    private readonly KestrelHost _host = new();

    public void Dispose() => _host.Dispose();

    public static TheoryData<string, string, string> Requests => new()
    {
        { "POST", "/v1/titles", $$"""{"isbn":"9780261102217","title":"T","author":"A","publicationYear":1937,"card":"{{CardNumber}}"}""" },
        { "PATCH", "/v1/titles/9780261102217", $$"""{"author":"{{CardNumber}}"}""" },
        { "POST", "/v1/titles/9780261102217/copies", $$"""{"barcode":"{{CardNumber}}"}""" },
        { "POST", "/v1/titles", $$"""{ this is not json {{CardNumber}}""" },
        { "POST", "/no/such/route", $$"""{"card":"{{CardNumber}}"}""" },
        { "POST", "/v1/sessions", $$"""{"email":"{{CardNumber}}@example.test","password":"{{PasswordSecret}}"}""" },
        { "POST", "/v1/sessions", $$"""{"email":"","password":"{{PasswordSecret}}","card":"{{CardNumber}}"}""" },
        { "DELETE", "/v1/sessions/current", $$"""{"card":"{{CardNumber}}","password":"{{PasswordSecret}}"}""" },
        { "PUT", "/v1/users/00000000-0000-0000-0000-000000000000/role", $$"""{"role":"{{CardNumber}}"}""" },
    };

    [Theory(DisplayName = "AC-SEC-013-1: a request with a card number in its body leaves no card number, password or authorization header in the logs")]
    [MemberData(nameof(Requests))]
    public async Task AC_SEC_013_1(string method, string path, string body)
    {
        using var client = _host.RawClient();
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri(_host.HttpsAddress, path))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerSecret);

        await client.SendAsync(request);
        // Plaintext too: the redirect path must not log the payload either.
        await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri(_host.HttpAddress, path))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", BearerSecret) },
        });

        var lines = _host.Logs.Lines;
        Assert.NotEmpty(lines); // the capture is live at Trace level, so silence would mean a broken test
        Assert.DoesNotContain(lines, l => l.Contains(CardNumber, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(BearerSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(PasswordSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Authorization: Bearer", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LogCaptureControlTests : IDisposable
{
    private readonly KestrelHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact(DisplayName = "REQ-SEC-013 (control): the log capture used by AC-SEC-013-1 does see values that are logged")]
    public void Capture_sees_logged_values()
    {
        var logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("control");
        logger.LogInformation("value {Value}", "marker-4111");
        Assert.Contains(_host.Logs.Lines, l => l.Contains("marker-4111", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "REQ-SEC-013 (control): the capture includes SQL command logs and request logs at Trace")]
    public async Task Capture_sees_framework_logs()
    {
        using var client = _host.RawClient();
        await client.PostAsync(new Uri(_host.HttpsAddress, "/v1/sessions"),
            new StringContent("""{"email":"someone@example.test","password":"x"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Contains(_host.Logs.Lines, l => l.StartsWith("Microsoft.EntityFrameworkCore.Database.Command", StringComparison.Ordinal)
                                               && l.Contains("SELECT", StringComparison.Ordinal));
        Assert.Contains(_host.Logs.Lines, l => l.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics", StringComparison.Ordinal));
    }
}
