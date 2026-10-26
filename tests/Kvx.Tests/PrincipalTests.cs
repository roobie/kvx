using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kvx.Server;
using Kvx.Server.Api;
using Kvx.Server.Auth;
using Kvx.Server.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kvx.Tests;

/// <summary>Passthrough signing in as a service principal held in kvx's store, at the HTTP level.</summary>
public sealed class PrincipalTests : IDisposable
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string ClientA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string ClientB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string Storage = "https://storage.azure.com";

    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private const string ManagedIdentity = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

    private KvxHost Host() => new(_dir, configure: KvxHost.PrincipalPassthrough);

    /// <summary>Login passthrough, with principals signing in through <see cref="FakePrincipal"/> for apps that name one.</summary>
    private KvxHost LoginHost() => new(_dir, configure: b => b.ConfigureTestServices(FakePrincipal.RegisterFactory));

    private static Task<HttpResponseMessage> SetPrincipal(KvxHost kvx, string clientId, string secret) =>
        SetPrincipal(kvx, new PrincipalRequest(Tenant, clientId, secret, Default: true));

    private static async Task<HttpResponseMessage> SetPrincipal(KvxHost kvx, PrincipalRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, PrincipalApi.Path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", kvx.Token);
        return await kvx.CreateClient().SendAsync(request);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Token(KvxHost kvx, string resource, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{IdentityEndpoint.Path}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}{query}");
        request.Headers.Add("X-IDENTITY-HEADER", kvx.Token);
        var response = await kvx.CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string AccessToken(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;

    private static string ErrorMessage(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("message").GetString()!;

    [Fact]
    public async Task Other_resources_get_the_stored_principals_token_and_kvx_keeps_its_own()
    {
        using var kvx = Host();
        Assert.Equal(HttpStatusCode.OK, (await SetPrincipal(kvx, ClientA, FakePrincipal.GoodSecret)).StatusCode);

        var (status, body) = await Token(kvx, Storage);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(FakePrincipal.TokenFor(ClientA, Storage + "/.default"), AccessToken(body));

        var own = await Token(kvx, "https://dev.localhost");
        Assert.Equal(kvx.Token, AccessToken(own.Body));
    }

    [Fact]
    public async Task A_new_principal_takes_effect_without_a_restart()
    {
        using var kvx = Host();
        await SetPrincipal(kvx, ClientA, FakePrincipal.GoodSecret);
        Assert.Equal(FakePrincipal.TokenFor(ClientA, Storage + "/.default"), AccessToken((await Token(kvx, Storage)).Body));

        await SetPrincipal(kvx, ClientB, FakePrincipal.GoodSecret);
        Assert.Equal(FakePrincipal.TokenFor(ClientB, Storage + "/.default"), AccessToken((await Token(kvx, Storage)).Body));
    }

    [Fact]
    public async Task Without_a_principal_other_resources_are_400_saying_how_to_set_one()
    {
        using var kvx = Host();
        var (status, body) = await Token(kvx, Storage);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("kvx principal set", ErrorMessage(body));
    }

    [Fact]
    public async Task A_principal_that_cannot_sign_in_is_400_naming_it_and_the_fix()
    {
        using var kvx = Host();
        await SetPrincipal(kvx, ClientA, "wrong-secret");
        var (status, body) = await Token(kvx, Storage);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var message = ErrorMessage(body);
        Assert.Contains(ClientA, message);
        Assert.Contains(Tenant, message);
        Assert.Contains("AADSTS7000215", message);
        Assert.Contains("kvx principal set", message);
    }

    [Theory]
    [InlineData("not a tenant!", ClientA, "s")]
    [InlineData(Tenant, "not-a-guid", "s")]
    [InlineData(Tenant, ClientA, "")]
    public async Task An_invalid_principal_is_refused(string tenant, string clientId, string secret)
    {
        using var kvx = Host();
        var request = new HttpRequestMessage(HttpMethod.Put, PrincipalApi.Path)
        {
            Content = JsonContent.Create(new PrincipalRequest(tenant, clientId, secret)),
        };
        request.Headers.Authorization = new("Bearer", kvx.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await kvx.CreateClient().SendAsync(request)).StatusCode);
    }

    private static async Task<PrincipalState> State(KvxHost kvx)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PrincipalApi.Path);
        request.Headers.Authorization = new("Bearer", kvx.Token);
        return (await (await kvx.CreateClient().SendAsync(request)).Content.ReadFromJsonAsync<PrincipalState>())!;
    }

    [Fact]
    public async Task The_serving_kvx_says_whether_passthrough_uses_the_default_principal()
    {
        using (var kvx = Host())
        {
            var empty = await State(kvx);
            Assert.True(empty.DefaultUsed);
            Assert.Empty(empty.Principals);
        }

        using var other = new KvxHost(_dir);   // login passthrough
        await SetPrincipal(other, ClientA, FakePrincipal.GoodSecret);
        var state = await State(other);
        Assert.False(state.DefaultUsed);
        Assert.Equal(ClientA, Assert.Single(state.Principals).ClientId);
    }

    [Theory]
    [InlineData(ClientA)]
    [InlineData(ManagedIdentity)]   // an alias: production's managed identity, standing in under its client id
    public async Task An_app_naming_a_client_id_gets_that_principal_in_any_mode(string asked)
    {
        using var kvx = LoginHost();
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientA, FakePrincipal.GoodSecret, [ManagedIdentity]));
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientB, FakePrincipal.GoodSecret));

        var (status, body) = await Token(kvx, Storage, $"&client_id={asked}");
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Equal(FakePrincipal.TokenFor(ClientA, Storage + "/.default"), AccessToken(body));

        // Naming no identity still gets the developer's login; kvx's own resource still gets kvx's token.
        Assert.Equal(FakeLogin.TokenFor(Storage + "/.default"), AccessToken((await Token(kvx, Storage)).Body));
        Assert.Equal(kvx.Token, AccessToken((await Token(kvx, "https://dev.localhost", $"&client_id={asked}")).Body));
    }

    [Fact]
    public async Task With_principal_passthrough_a_named_principal_wins_over_the_default()
    {
        using var kvx = Host();
        await SetPrincipal(kvx, ClientA, FakePrincipal.GoodSecret);   // the default
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientB, FakePrincipal.GoodSecret));

        Assert.Equal(FakePrincipal.TokenFor(ClientB, Storage + "/.default"),
            AccessToken((await Token(kvx, Storage, $"&client_id={ClientB}")).Body));
        Assert.Equal(FakePrincipal.TokenFor(ClientA, Storage + "/.default"), AccessToken((await Token(kvx, Storage)).Body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_client_id_is_400_naming_it_and_never_falls_back(bool principalPassthrough)
    {
        using var kvx = principalPassthrough ? Host() : LoginHost();
        await SetPrincipal(kvx, ClientA, FakePrincipal.GoodSecret);

        var (status, body) = await Token(kvx, Storage, $"&client_id={ClientB}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var message = ErrorMessage(body);
        Assert.Contains(ClientB, message);
        Assert.Contains("--for", message);
        Assert.Contains("AZURE_CLIENT_ID", message);
    }

    [Theory]
    [InlineData("mi_res_id")]
    [InlineData("object_id")]
    public async Task Other_ways_of_naming_an_identity_are_400(string parameter)
    {
        using var kvx = LoginHost();
        var (status, body) = await Token(kvx, Storage, $"&{parameter}=x");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("client_id", ErrorMessage(body));
    }

    [Fact]
    public async Task An_id_another_principal_answers_to_is_409()
    {
        using var kvx = LoginHost();
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientA, FakePrincipal.GoodSecret, [ManagedIdentity]));
        var conflict = await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientB, FakePrincipal.GoodSecret, [ManagedIdentity]));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains(ClientA, await conflict.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rotating_a_secret_keeps_aliases_and_the_default()
    {
        using var kvx = Host();
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientA, "old", [ManagedIdentity], Default: true));
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientA, FakePrincipal.GoodSecret));

        var view = Assert.Single((await State(kvx)).Principals);
        Assert.True(view.Default);
        Assert.Equal([ManagedIdentity], view.Aliases);
        Assert.Equal(HttpStatusCode.OK, (await Token(kvx, Storage)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Token(kvx, Storage, $"&client_id={ManagedIdentity}")).Status);
    }

    [Fact]
    public async Task Clear_removes_one_principal_by_client_id()
    {
        using var kvx = LoginHost();
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientA, FakePrincipal.GoodSecret));
        await SetPrincipal(kvx, new PrincipalRequest(Tenant, ClientB, FakePrincipal.GoodSecret));

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"{PrincipalApi.Path}/{ClientA}");
        delete.Headers.Authorization = new("Bearer", kvx.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await kvx.CreateClient().SendAsync(delete)).StatusCode);
        Assert.Equal(ClientB, Assert.Single((await State(kvx)).Principals).ClientId);
    }

    [Fact]
    public async Task A_store_with_the_single_principal_of_before_loads_it_as_the_default()
    {
        using (var kvx = Host())
            kvx.Services.GetRequiredService<SealedStore>().Save(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Principal = new { TenantId = Tenant, ClientId = ClientA, Secret = FakePrincipal.GoodSecret, Updated = 1L },
            }));

        using var restarted = Host();
        var view = Assert.Single((await State(restarted)).Principals);
        Assert.Equal(ClientA, view.ClientId);
        Assert.True(view.Default);
        Assert.Equal(FakePrincipal.TokenFor(ClientA, Storage + "/.default"), AccessToken((await Token(restarted, Storage)).Body));
    }

    [Fact]
    public async Task The_principal_endpoint_needs_the_session_token()
    {
        using var kvx = Host();
        var response = await kvx.CreateClient().GetAsync(PrincipalApi.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_secret_is_in_no_response_and_no_file()
    {
        string dataDir;
        using (var kvx = Host())
        {
            var set = await SetPrincipal(kvx, ClientA, FakePrincipal.GoodSecret);
            Assert.DoesNotContain(FakePrincipal.GoodSecret, await set.Content.ReadAsStringAsync());

            var show = new HttpRequestMessage(HttpMethod.Get, PrincipalApi.Path);
            show.Headers.Authorization = new("Bearer", kvx.Token);
            var shown = await (await kvx.CreateClient().SendAsync(show)).Content.ReadAsStringAsync();
            Assert.Contains(ClientA, shown);
            Assert.DoesNotContain(FakePrincipal.GoodSecret, shown);
            dataDir = kvx.DataDir;
        }

        using (var restarted = Host())
            Assert.Equal(HttpStatusCode.OK, (await Token(restarted, Storage)).Status);   // the principal survives a restart

        foreach (var file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(FakePrincipal.GoodSecret)) < 0, $"secret found in {file}");
        }
    }

    [Fact]
    public async Task Kvx_refuses_to_start_with_the_principal_mixed_with_a_login()
    {
        using var dir = new TempDir();
        var result = await LiveKvx.Cli([
            $"--urls=http://127.0.0.1:{LiveKvx.FreePort()}",
            $"--Kvx:DataDir={Path.Combine(dir.Path, "data")}",
            $"--Kvx:TokenFile={Path.Combine(dir.Path, "run", "dev-token")}",
            "--Kvx:Dns=off",
            "--Kvx:Passthrough=ServicePrincipal,AzureCli",
        ]);

        Assert.True(result.ExitCode == 1, result.ToString());
        Assert.Contains("only entry", result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);   // a message, not a stack trace
    }
}

/// <summary>
/// `kvx principal` and `kvx doctor` as their own processes, against a kvx with principal passthrough. Only the server
/// is given Kvx:Passthrough, so the commands must learn the mode from it.
/// </summary>
public sealed class PrincipalCliTests : IAsyncLifetime
{
    private const string Tenant = "contoso.onmicrosoft.com";
    private const string Client = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    private readonly LiveKvx _kvx = LiveKvx.WithPrincipalPassthrough();
    public Task InitializeAsync() => _kvx.StartAsync();
    public Task DisposeAsync() => _kvx.DisposeAsync().AsTask();

    private Task<LiveKvx.Result> Principal(string[] args, string? stdin = null) =>
        LiveKvx.Cli(["principal", .. args, .. _kvx.ClientSettings], stdin: stdin);

    private Task<LiveKvx.Result> Doctor() =>
        LiveKvx.Cli(["doctor", .. _kvx.ClientSettings, .. _kvx.CertificateSettings],
            psi => psi.Environment["SSL_CERT_DIR"] = _kvx.TrustDir);

    private static string Line(LiveKvx.Result result, string check) =>
        result.Stdout.Split('\n').FirstOrDefault(l => l.Length > 6 && l[6..].StartsWith(check)) ?? $"<no {check} line>\n{result}";

    [Fact]
    public async Task Set_reads_the_secret_from_stdin_and_show_never_prints_it()
    {
        var set = await Principal(["set", "--tenant", Tenant, "--client-id", Client, "--default"], FakePrincipal.GoodSecret + "\n");
        Assert.True(set.ExitCode == 0, set.ToString());
        Assert.Contains("get the default one", set.Stdout);

        var show = await Principal(["show"]);
        Assert.True(show.ExitCode == 0, show.ToString());
        Assert.Contains(Client, show.Stdout);
        Assert.Contains(Tenant, show.Stdout);
        Assert.DoesNotContain(FakePrincipal.GoodSecret, show.Stdout + show.Stderr);

        var doctor = await Doctor();
        Assert.StartsWith("ok", Line(doctor, "principal"));

        Assert.Equal(0, (await Principal(["clear", "--client-id", Client])).ExitCode);
        Assert.Equal(1, (await Principal(["show"])).ExitCode);
    }

    [Fact]
    public async Task Show_lists_each_principal_with_the_ids_apps_get_it_by()
    {
        const string other = "dddddddd-dddd-dddd-dddd-dddddddddddd", alias = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
        await Principal(["set", "--tenant", Tenant, "--client-id", Client, "--default"], FakePrincipal.GoodSecret);
        await Principal(["set", "--tenant", Tenant, "--client-id", other, "--for", alias], FakePrincipal.GoodSecret);

        var show = await Principal(["show"]);
        Assert.True(show.ExitCode == 0, show.ToString());
        Assert.Contains($"AZURE_CLIENT_ID={Client}.", show.Stdout);
        Assert.Contains($"AZURE_CLIENT_ID={other} or {alias}", show.Stdout);

        var doctor = await Doctor();
        Assert.Equal(2, doctor.Stdout.Split('\n').Count(l => l.StartsWith("ok") && l.Contains("signs in")));
    }

    [Fact]
    public async Task Doctor_fails_when_no_principal_is_the_default()
    {
        await Principal(["set", "--tenant", Tenant, "--client-id", Client], FakePrincipal.GoodSecret);
        var doctor = await Doctor();
        Assert.Equal(1, doctor.ExitCode);
        Assert.Contains("--default", doctor.Stdout);
    }

    [Fact]
    public async Task Set_without_a_secret_is_refused()
    {
        var set = await Principal(["set", "--tenant", Tenant, "--client-id", Client], "");
        Assert.Equal(1, set.ExitCode);
        Assert.Contains("no client secret", set.Stderr);
    }

    [Fact]
    public async Task Doctor_fails_when_no_principal_is_set()
    {
        var doctor = await Doctor();
        Assert.Equal(1, doctor.ExitCode);
        Assert.StartsWith("FAIL", Line(doctor, "principal"));
        Assert.Contains("kvx principal set", doctor.Stdout);
    }

    [Fact]
    public async Task Doctor_fails_when_the_principal_cannot_sign_in()
    {
        await Principal(["set", "--tenant", Tenant, "--client-id", Client, "--default"], "wrong-secret");
        var doctor = await Doctor();
        Assert.Equal(1, doctor.ExitCode);
        Assert.StartsWith("FAIL", Line(doctor, "principal"));
        Assert.Contains("AADSTS7000215", doctor.Stdout);
    }
}
