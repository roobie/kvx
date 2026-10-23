using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using Kvx.Server;

namespace Kvx.Tests;

/// <summary>Real sockets and real TLS: kvx on loopback behind Caddy, reached by host name.</summary>
[Trait("Category", "E2E")]
public sealed class CaddyEndToEndTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly int _kvxPort = FreePort(), _httpsPort = FreePort();
    private Microsoft.AspNetCore.Builder.WebApplication? _kvx;
    private Process? _caddy;
    private readonly List<string> _caddyLog = [];

    private string CaddyHome => Path.Combine(_dir.Path, "caddy-home");
    private string TokenFile => Path.Combine(_dir.Path, "run", "dev-token");

    public async Task InitializeAsync()
    {
        _kvx = KvxApp.Build([
            $"--urls=http://127.0.0.1:{_kvxPort}",
            $"--Kvx:DataDir={Path.Combine(_dir.Path, "data")}",
            $"--Kvx:TokenFile={TokenFile}",
        ]);
        await _kvx.StartAsync();

        var psi = new ProcessStartInfo("caddy")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "run", "--adapter", "caddyfile", "--config", Path.Combine(AppContext.BaseDirectory, "Caddyfile") })
            psi.ArgumentList.Add(a);
        psi.Environment["XDG_DATA_HOME"] = CaddyHome;
        psi.Environment["XDG_CONFIG_HOME"] = CaddyHome;
        psi.Environment["KVX_HTTPS_PORT"] = _httpsPort.ToString();
        psi.Environment["KVX_UPSTREAM"] = $"127.0.0.1:{_kvxPort}";

        try { _caddy = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException("caddy is not on PATH; run tests via `mise exec -- dotnet test`.", e);
        }
        _caddy.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_caddyLog) _caddyLog.Add(e.Data); };
        _caddy.BeginErrorReadLine();
        _caddy.BeginOutputReadLine();
    }

    public async Task DisposeAsync()
    {
        if (_caddy is { HasExited: false }) { _caddy.Kill(entireProcessTree: true); await _caddy.WaitForExitAsync(); }
        if (_kvx is not null) await _kvx.DisposeAsync();
        _dir.Dispose();
    }

    [Fact]
    public async Task Stock_secret_client_round_trips_through_caddy()
    {
        var root = await CaddyRoot();
        var client = new SecretClient(
            new Uri($"https://test.kvx.localhost:{_httpsPort}"),
            new StaticToken(await File.ReadAllTextAsync(TokenFile)),
            new SecretClientOptions { Transport = new HttpClientTransport(TrustingOnly(root)) });

        var set = (await client.SetSecretAsync("conn-string", "Server=db;Password=hunter2")).Value;
        Assert.Equal(new Uri($"https://test.kvx.localhost:{_httpsPort}/secrets/conn-string/{set.Properties.Version}"), set.Id);
        Assert.Equal("Server=db;Password=hunter2", (await client.GetSecretAsync("conn-string")).Value.Value);
        Assert.Equal("Server=db;Password=hunter2",
            (await client.GetSecretAsync("conn-string", set.Properties.Version)).Value.Value);
    }

    /// <summary>
    /// Stands in for `caddy trust`: the test trusts only this run's Caddy CA rather than
    /// touching the machine's trust store. Hostname validation stays on.
    /// Also stands in for *.localhost name resolution, which glibc only does with
    /// nss-myhostname or systemd-resolved; SNI and Host still carry the vault name.
    /// </summary>
    private static HttpMessageHandler TrustingOnly(X509Certificate2 root) => new SocketsHttpHandler
    {
        ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        },
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, cert, presented, errors) =>
            {
                if (cert is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                    return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (presented is not null)
                    foreach (var element in presented.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                return chain.Build(new X509Certificate2(cert));
            },
        },
    };

    private async Task<X509Certificate2> CaddyRoot()
    {
        var path = Path.Combine(CaddyHome, "caddy", "pki", "authorities", "local", "root.crt");
        for (var i = 0; i < 100; i++)
        {
            if (_caddy!.HasExited) break;
            if (File.Exists(path) && await Listening(_httpsPort)) return X509CertificateLoader.LoadCertificateFromFile(path);
            await Task.Delay(100);
        }
        lock (_caddyLog) throw new TimeoutException("Caddy did not come up:\n" + string.Join('\n', _caddyLog));
    }

    private static async Task<bool> Listening(int port)
    {
        try { using var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, port); return true; }
        catch (SocketException) { return false; }
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }
}
