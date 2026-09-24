using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibraryBookManager.Api.Tests.Infrastructure;

/// <summary>
/// Runs the real Kestrel server over real sockets, configured exactly as production is
/// (Program.cs), with a throwaway self-signed certificate. Used where the in-memory
/// TestServer would bypass the behaviour under test: TLS negotiation and plaintext listeners.
/// </summary>
public sealed class KestrelHost : WebApplicationFactory<Program>
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lbm-kestrel-").FullName;
    private readonly string _connectionString = $"Data Source=lbm-k-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public CapturingLoggerProvider Logs { get; } = new();
    public string CertificatePemPath => Path.Combine(_dir, "cert.pem");
    public string KeyPemPath => Path.Combine(_dir, "key.pem");
    public Uri HttpsAddress { get; private set; } = null!;
    public Uri HttpAddress { get; private set; } = null!;

    public KestrelHost()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        WriteCertificate();
        UseKestrel();
        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
            .Select(a => new Uri(a)).ToList();
        HttpsAddress = addresses.Single(a => a.Scheme == "https");
        HttpAddress = addresses.Single(a => a.Scheme == "http");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Library", _connectionString);
        builder.UseSetting("Kestrel:Endpoints:Https:Url", "https://127.0.0.1:0");
        builder.UseSetting("Kestrel:Endpoints:Http:Url", "http://127.0.0.1:0");
        builder.UseSetting("Kestrel:Certificates:Default:Path", Path.Combine(_dir, "cert.pfx"));
        builder.UseSetting("Kestrel:Certificates:Default:Password", "test-only");
        // appsettings.json quietens some categories; the log tests must see everything.
        foreach (var category in new[] { "Default", "Microsoft", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore.Database.Command" })
            builder.UseSetting($"Logging:LogLevel:{category}", "Trace");
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });
    }

    /// <summary>A client that trusts the throwaway certificate and does not follow redirects.</summary>
    public HttpClient RawClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler);
    }

    private void WriteCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(Path.Combine(_dir, "cert.pfx"), certificate.Export(X509ContentType.Pfx, "test-only"));
        File.WriteAllText(CertificatePemPath, certificate.ExportCertificatePem());
        File.WriteAllText(KeyPemPath, rsa.ExportPkcs8PrivateKeyPem());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _keepAlive.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) return _lines.ToList(); }
    }

    public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

    public void Dispose() { }

    private sealed class Capturing(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            lock (owner._lines) owner._lines.Add($"{category} scope: {Flatten(state)}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
                owner._lines.Add($"{category} [{logLevel}] {formatter(state, exception)} | {Flatten(state)} | {exception}");
        }

        // Structured state can carry values the formatted message omits; capture those too.
        private static string Flatten<T>(T state) =>
            state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString() ?? "";
    }
}
