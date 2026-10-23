using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Kvx.Tests;

/// <summary>
/// Real sockets, real TLS, real name resolution: kvx on loopback behind Caddy, used by an
/// unmodified app started with `kvx run`. Needs *.localhost resolution (nss-myhostname or
/// systemd-resolved) and caddy on PATH.
/// </summary>
[Trait("Category", "E2E")]
public sealed class CaddyEndToEndTests : IAsyncLifetime
{
    private readonly LiveKvx _kvx = new();
    private readonly int _httpsPort = LiveKvx.FreePort();
    private Process? _caddy;
    private readonly List<string> _caddyLog = [];

    private string CaddyHome => Path.Combine(_kvx.Dir.Path, "caddy-home");
    private static string SampleApp => Path.Combine(AppContext.BaseDirectory, "Kvx.SampleApp.dll");

    public async Task InitializeAsync()
    {
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
        psi.Environment["KVX_UPSTREAM"] = $"127.0.0.1:{_kvx.Port}";

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
        await _kvx.DisposeAsync();
    }

    [Fact]
    public async Task Unmodified_app_round_trips_through_caddy()
    {
        var vault = await Vault("test");
        var result = await RunApp(SampleApp, vault, "conn-string", "Server=db;Password=hunter2");

        using var json = JsonDocument.Parse(result.Stdout);
        Assert.StartsWith($"{vault}/secrets/conn-string/", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("Server=db;Password=hunter2", json.RootElement.GetProperty("latest").GetString());
        Assert.Equal("Server=db;Password=hunter2", json.RootElement.GetProperty("byVersion").GetString());
        // Same process, same credential: a non-Key Vault resource is served by the developer's login, not refused.
        Assert.Equal(FakeLogin.TokenFor("https://storage.azure.com/.default"), json.RootElement.GetProperty("storageToken").GetString());
    }

    [Fact]
    public async Task Imported_user_secrets_read_back_through_the_configuration_provider()
    {
        var vault = await Vault("imported");
        var file = Path.Combine(_kvx.Dir.Path, "secrets.json");
        File.WriteAllText(file, """{ "ConnectionStrings": { "Db": "Server=db;Password=hunter2" }, "ApiKey": "k1" }""");
        var import = await LiveKvx.Cli(["import", "--vault", "imported", "--file", file, .. _kvx.ClientSettings]);
        Assert.True(import.ExitCode == 0, import.ToString());

        var result = await RunApp(SampleApp, "config", vault, "ConnectionStrings:Db", "ApiKey");

        Assert.Equal(new Dictionary<string, string> { ["ConnectionStrings:Db"] = "Server=db;Password=hunter2", ["ApiKey"] = "k1" },
            JsonSerializer.Deserialize<Dictionary<string, string>>(result.Stdout));
    }

    /// <summary>`kvx run -- dotnet app…`, from a shell that carries a service principal DefaultAzureCredential would otherwise pick.</summary>
    private async Task<LiveKvx.Result> RunApp(params string[] app)
    {
        var caddyRoot = await CaddyRoot();
        var result = await LiveKvx.Cli(["run", .. _kvx.ClientSettings, "--", "dotnet", .. app], psi =>
        {
            foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("AZURE_") || k.StartsWith("IDENTITY_")).ToList())
                psi.Environment.Remove(key);
            psi.Environment["AZURE_TENANT_ID"] = "00000000-0000-0000-0000-000000000000";
            psi.Environment["AZURE_CLIENT_ID"] = "00000000-0000-0000-0000-000000000000";
            psi.Environment["AZURE_CLIENT_SECRET"] = "a-service-principal-secret";
            // This run's Caddy CA as the trust root for the app, as `caddy trust` would make it.
            psi.Environment["SSL_CERT_FILE"] = caddyRoot;
        });
        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("unset AZURE_CLIENT_SECRET", result.Stderr);
        return result;
    }

    private async Task<string> Vault(string name)
    {
        var vault = $"https://{name}.kvx.localhost:{_httpsPort}";
        var host = new Uri(vault).Host;
        Assert.True((await Dns.GetHostAddressesAsync(host)).Any(IPAddress.IsLoopback),
            $"{host} does not resolve to loopback; enable nss-myhostname or systemd-resolved.");
        return vault;
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

    private static async Task<bool> Listening(int port)
    {
        try { using var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, port); return true; }
        catch (SocketException) { return false; }
    }
}
