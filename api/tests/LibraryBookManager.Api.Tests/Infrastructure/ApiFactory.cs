using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryBookManager.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LibraryBookManager.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in-process against a private in-memory SQLite database, with a clock the
/// test controls. One factory per test: every test starts from an empty system.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "correct horse battery staple";

    /// <summary>
    /// Production uses 600,000 PBKDF2 iterations (tested separately); tests use fewer so that
    /// hundreds of sign-ins stay fast. The format and verification path are identical.
    /// </summary>
    public const int TestHashIterations = 1_000;

    private readonly string _connectionString =
        $"Data Source=lbm-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // Keeps the shared in-memory database alive for the factory's lifetime.
    private readonly SqliteConnection _keepAlive;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public ApiFactory(IReadOnlyDictionary<string, string>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string>();
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        // Callers arrive over HTTPS; plaintext is only ever redirected (REQ-SEC-011).
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Library", _connectionString);
        builder.UseSetting("Passwords:Iterations", TestHashIterations.ToString());
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>A client carrying a real session for <paramref name="user"/>, obtained by signing in.</summary>
    public HttpClient ClientAs(User user, string password = Password)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SignIn(user.Email, password));
        return client;
    }

    public string SignIn(string email, string password = Password)
    {
        var response = CreateClient().PostAsJsonAsync("/v1/sessions", new { email, password }).GetAwaiter().GetResult();
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException(
                $"sign-in as {email} failed: {(int)response.StatusCode} {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");
        return response.Read<SessionView>().GetAwaiter().GetResult().Token;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient AnonymousClient() => CreateClient();

    public T Service<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Seed(Action<LibraryDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        seed(db);
        db.SaveChanges();
    }

    public T Query<T>(Func<LibraryDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return query(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
