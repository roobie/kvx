using System.Net;
using System.Text.Json;

namespace Kvx.Tests;

/// <summary>
/// Real sockets, real TLS served by kvx itself, real name resolution: an unmodified app started with
/// `kvx run`, which is all the app is given. Needs *.localhost resolution (nss-myhostname or systemd-resolved).
/// </summary>
[Trait("Category", "E2E")]
public sealed class TlsEndToEndTests : IAsyncLifetime
{
    /// <summary>The client id the app's shell carries, as production's managed identity would be named.</summary>
    private const string AppIdentity = "00000000-0000-0000-0000-000000000000";
    private const string AppPrincipal = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    private readonly LiveKvx _kvx = new(services: FakePrincipal.RegisterFactory);
    private static string SampleApp => Path.GetFullPath(Path.Combine(
        typeof(TlsEndToEndTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "SampleAppDir").Value!,
        "Kvx.SampleApp.dll"));

    public Task InitializeAsync() => _kvx.StartAsync();
    public Task DisposeAsync() => _kvx.DisposeAsync().AsTask();

    [Fact]
    public async Task Unmodified_app_round_trips_over_kvx_tls()
    {
        var vault = await Vault("test");
        var set = await LiveKvx.Cli(["principal", "set", "--tenant", "contoso.onmicrosoft.com", "--client-id", AppPrincipal,
            "--for", AppIdentity, .. _kvx.ClientSettings], stdin: FakePrincipal.GoodSecret);
        Assert.True(set.ExitCode == 0, set.ToString());

        var result = await RunApp(SampleApp, vault, "conn-string", "Server=db;Password=hunter2");

        using var json = JsonDocument.Parse(result.Stdout);
        Assert.StartsWith($"{vault}/secrets/conn-string/", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("Server=db;Password=hunter2", json.RootElement.GetProperty("latest").GetString());
        Assert.Equal("Server=db;Password=hunter2", json.RootElement.GetProperty("byVersion").GetString());
        // Same process, same credential: a non-Key Vault resource is served by the principal standing in for the app's
        // AZURE_CLIENT_ID, not by the developer's login.
        Assert.Equal(FakePrincipal.TokenFor(AppPrincipal, "https://storage.azure.com/.default"),
            json.RootElement.GetProperty("storageToken").GetString());
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

    /// <summary>
    /// `kvx run -- dotnet app…`, from a shell that carries a service principal DefaultAzureCredential would otherwise
    /// pick and no trust settings of its own: trusting kvx's certificate is up to `kvx run`.
    /// </summary>
    private async Task<LiveKvx.Result> RunApp(params string[] app)
    {
        var result = await LiveKvx.Cli(["run", .. _kvx.ClientSettings, "--", "dotnet", .. app], psi =>
        {
            foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("AZURE_") || k.StartsWith("IDENTITY_") || k.StartsWith("SSL_CERT_")).ToList())
                psi.Environment.Remove(key);
            psi.Environment["AZURE_TENANT_ID"] = "00000000-0000-0000-0000-000000000000";
            psi.Environment["AZURE_CLIENT_ID"] = AppIdentity;
            psi.Environment["AZURE_CLIENT_SECRET"] = "a-service-principal-secret";
        });
        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("unset AZURE_CLIENT_SECRET", result.Stderr);
        return result;
    }

    private async Task<string> Vault(string name)
    {
        var vault = _kvx.VaultUri(name);
        var host = new Uri(vault).Host;
        Assert.True((await Dns.GetHostAddressesAsync(host)).Any(IPAddress.IsLoopback),
            $"{host} does not resolve to loopback; enable nss-myhostname or systemd-resolved.");
        return vault;
    }
}
