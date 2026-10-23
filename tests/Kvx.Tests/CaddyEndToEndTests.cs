using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Kvx.Server;
using Kvx.Server.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Kvx.Tests;

/// <summary>
/// Real sockets, real TLS, real name resolution: kvx on loopback behind Caddy, used by an
/// unmodified app in its own process. Needs *.localhost resolution (nss-myhostname or
/// systemd-resolved) and caddy on PATH.
/// </summary>
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
        ], b => b.Services.AddSingleton(FakeLogin.Upstream));
        await _kvx.StartAsync();

        var psi = Redirected("caddy", "run", "--adapter", "caddyfile", "--config", Path.Combine(AppContext.BaseDirectory, "Caddyfile"));
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
    public async Task Unmodified_app_round_trips_through_caddy()
    {
        var vault = $"https://test.kvx.localhost:{_httpsPort}";
        var host = new Uri(vault).Host;
        Assert.True((await Dns.GetHostAddressesAsync(host)).Any(IPAddress.IsLoopback),
            $"{host} does not resolve to loopback; enable nss-myhostname or systemd-resolved.");

        // SSL_CERT_FILE makes this run's Caddy CA the trust root for the child, as `caddy trust` would.
        var psi = Redirected("dotnet", Path.Combine(AppContext.BaseDirectory, "Kvx.SampleApp.dll"),
            vault, "conn-string", "Server=db;Password=hunter2");
        psi.Environment["SSL_CERT_FILE"] = await CaddyRoot();

        // The developer's shell may carry credentials that DefaultAzureCredential would try first.
        foreach (var key in psi.Environment.Keys.ToList())
            if (key.StartsWith("AZURE_") || key.StartsWith("IDENTITY_") || key.StartsWith("MSI_") || key == "IMDS_ENDPOINT")
                psi.Environment.Remove(key);
        // All the app is told: where kvx's identity endpoint is, and this login session's token.
        psi.Environment["IDENTITY_ENDPOINT"] = $"http://127.0.0.1:{_kvxPort}{IdentityEndpoint.Path}";
        psi.Environment["IDENTITY_HEADER"] = File.ReadAllText(TokenFile).Trim();

        using var app = Process.Start(psi)!;
        var stdout = app.StandardOutput.ReadToEndAsync();
        var stderr = app.StandardError.ReadToEndAsync();
        await app.WaitForExitAsync();
        Assert.True(app.ExitCode == 0, await stderr);

        using var result = JsonDocument.Parse(await stdout);
        Assert.StartsWith($"{vault}/secrets/conn-string/", result.RootElement.GetProperty("id").GetString());
        Assert.Equal("Server=db;Password=hunter2", result.RootElement.GetProperty("latest").GetString());
        Assert.Equal("Server=db;Password=hunter2", result.RootElement.GetProperty("byVersion").GetString());
        // Same process, same credential: a non-Key Vault resource is served by the developer's login, not refused.
        Assert.Equal(FakeLogin.TokenFor("https://storage.azure.com/.default"), result.RootElement.GetProperty("storageToken").GetString());
    }

    private async Task<string> CaddyRoot()
    {
        var path = Path.Combine(CaddyHome, "caddy", "pki", "authorities", "local", "root.crt");
        for (var i = 0; i < 100; i++)
        {
            if (_caddy!.HasExited) break;
            if (File.Exists(path) && await Listening(_httpsPort)) return path;
            await Task.Delay(100);
        }
        lock (_caddyLog) throw new TimeoutException("Caddy did not come up:\n" + string.Join('\n', _caddyLog));
    }

    private static ProcessStartInfo Redirected(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
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
