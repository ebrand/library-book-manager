using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Security;

/// <summary>org-sec-004 REQ-SEC-011 — TLS 1.2 or later; plaintext redirected to HTTPS.</summary>
public sealed class TransportSecurityTests : IDisposable
{
    private readonly KestrelHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory(DisplayName = "AC-SEC-011-1: a plaintext HTTP request to a public endpoint is answered with a 301 redirect to its HTTPS equivalent")]
    [InlineData("GET", "/v1/titles?author=Le%20Guin&page=2")]
    [InlineData("POST", "/v1/titles")]
    [InlineData("PATCH", "/v1/titles/9780261102217")]
    [InlineData("DELETE", "/v1/copies/LBM0000000001")]
    [InlineData("GET", "/no/such/route")]
    public async Task AC_SEC_011_1(string method, string pathAndQuery)
    {
        using var client = _host.RawClient();
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri(_host.HttpAddress, pathAndQuery));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(new Uri(_host.HttpsAddress, pathAndQuery), response.Headers.Location);
    }

    [Fact(DisplayName = "AC-SEC-011-2: a TLS handshake offering only TLS 1.1 is rejected")]
    public async Task AC_SEC_011_2()
    {
        // Control: prove this openssl client can complete a TLS 1.1-only handshake at all,
        // against a server that allows it. Without this, a rejection could just mean the
        // client refused to speak TLS 1.1.
        var controlPort = FreePort();
        using (var control = StartProcess("openssl", "s_server", "-accept", controlPort.ToString(),
                   "-cert", _host.CertificatePemPath, "-key", _host.KeyPemPath,
                   "-tls1_1", "-cipher", "DEFAULT:@SECLEVEL=0", "-quiet"))
        {
            await WaitForListener(controlPort);
            var controlResult = await Handshake(controlPort, "-tls1_1");
            control.Kill();
            Assert.True(controlResult.Succeeded, "control: openssl could not complete a TLS 1.1 handshake at all\n" + controlResult.Output);
        }

        var tls11 = await Handshake(_host.HttpsAddress.Port, "-tls1_1");
        Assert.False(tls11.Succeeded, "the API accepted a TLS 1.1 handshake\n" + tls11.Output);

        // And the API does still complete a TLS 1.2 handshake. (TLS 1.3 is not asserted: whether
        // Kestrel can offer it depends on the host OS's TLS stack — see QUESTIONS.md Q-SEC-03.)
        var tls12 = await Handshake(_host.HttpsAddress.Port, "-tls1_2");
        Assert.True(tls12.Succeeded, tls12.Output);
    }

    private sealed record HandshakeResult(bool Succeeded, string Output);

    private static async Task<HandshakeResult> Handshake(int port, string versionFlag)
    {
        using var process = StartProcess("openssl", "s_client", "-connect", $"127.0.0.1:{port}",
            versionFlag, "-cipher", "DEFAULT:@SECLEVEL=0", "-servername", "localhost");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;
        // A completed handshake reports the negotiated cipher; a refused one reports cipher "(NONE)" or none at all.
        var negotiated = output.Contains("Cipher is ", StringComparison.Ordinal) && !output.Contains("Cipher is (NONE)", StringComparison.Ordinal);
        return new HandshakeResult(process.ExitCode == 0 && negotiated, output);
    }

    private static Process StartProcess(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}; openssl is required for this test");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForListener(int port)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50);
            }
        }
        throw new TimeoutException($"control server never listened on {port}");
    }
}
