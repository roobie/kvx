using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kvx.Server;
using Kvx.Server.Api;
using Kvx.Server.Auth;

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

    private KvxHost Host() => new(_dir, configure: KvxHost.PrincipalPassthrough);

    private static async Task<HttpResponseMessage> SetPrincipal(KvxHost kvx, string clientId, string secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, PrincipalApi.Path)
        {
            Content = JsonContent.Create(new PrincipalRequest(Tenant, clientId, secret)),
        };
        request.Headers.Authorization = new("Bearer", kvx.Token);
        return await kvx.CreateClient().SendAsync(request);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Token(KvxHost kvx, string resource)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{IdentityEndpoint.Path}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}");
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

/// <summary>`kvx principal` and `kvx doctor` as their own processes, against a kvx with principal passthrough.</summary>
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
        var set = await Principal(["set", "--tenant", Tenant, "--client-id", Client], FakePrincipal.GoodSecret + "\n");
        Assert.True(set.ExitCode == 0, set.ToString());
        Assert.Contains("Passthrough signs in as it", set.Stdout);

        var show = await Principal(["show"]);
        Assert.True(show.ExitCode == 0, show.ToString());
        Assert.Contains(Client, show.Stdout);
        Assert.Contains(Tenant, show.Stdout);
        Assert.DoesNotContain(FakePrincipal.GoodSecret, show.Stdout + show.Stderr);

        var doctor = await Doctor();
        Assert.StartsWith("ok", Line(doctor, "principal"));

        Assert.Equal(0, (await Principal(["clear"])).ExitCode);
        Assert.Equal(1, (await Principal(["show"])).ExitCode);
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
        await Principal(["set", "--tenant", Tenant, "--client-id", Client], "wrong-secret");
        var doctor = await Doctor();
        Assert.Equal(1, doctor.ExitCode);
        Assert.StartsWith("FAIL", Line(doctor, "principal"));
        Assert.Contains("AADSTS7000215", doctor.Stdout);
    }
}
