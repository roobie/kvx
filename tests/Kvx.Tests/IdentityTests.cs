using System.Net;
using System.Text.Json;
using Kvx.Server;
using Kvx.Server.Auth;

namespace Kvx.Tests;

/// <summary>The managed-identity endpoint and the login-session token, at the HTTP level.</summary>
public sealed class IdentityTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static HttpRequestMessage TokenRequest(string? header, string resource = "https://dev.localhost")
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{IdentityEndpoint.Path}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}");
        if (header is not null) request.Headers.Add("X-IDENTITY-HEADER", header);
        return request;
    }

    [Fact]
    public async Task Issues_the_session_token_in_the_app_service_shape()
    {
        using var kvx = new KvxHost(_dir);
        var response = await kvx.CreateClient().SendAsync(TokenRequest(kvx.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(kvx.Token, body.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal("https://dev.localhost", body.RootElement.GetProperty("resource").GetString());
        var expiresOn = DateTimeOffset.FromUnixTimeSeconds(body.RootElement.GetProperty("expires_on").GetInt64());
        Assert.True(expiresOn > DateTimeOffset.UtcNow.AddHours(2), "a lifetime over 2h makes MSAL refresh at half-life instead of hammering");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-token")]
    public async Task Missing_or_wrong_header_is_401(string? header)
    {
        using var kvx = new KvxHost(_dir);
        var response = await kvx.CreateClient().SendAsync(TokenRequest(header));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Other_resources_pass_through_to_the_developer_login()
    {
        using var kvx = new KvxHost(_dir);
        var response = await kvx.CreateClient().SendAsync(TokenRequest(kvx.Token, "https://management.azure.com/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(FakeLogin.TokenFor("https://management.azure.com/.default"), body.RootElement.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task Passthrough_needs_the_session_token_too()
    {
        using var kvx = new KvxHost(_dir);
        var response = await kvx.CreateClient().SendAsync(TokenRequest("not-the-token", "https://management.azure.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_developer_login_other_resources_are_400_saying_how_to_sign_in()
    {
        using var kvx = new KvxHost(_dir, upstream: Upstream.FromConfig("AzureCli") with { Credential = new NoLogin() });
        var response = await kvx.CreateClient().SendAsync(TokenRequest(kvx.Token, "https://storage.azure.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("az login", body.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task With_passthrough_off_other_resources_are_400_with_a_reason()
    {
        using var kvx = new KvxHost(_dir, upstream: Upstream.FromConfig(""));
        var response = await kvx.CreateClient().SendAsync(TokenRequest(kvx.Token, "https://management.azure.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("management.azure.com", message);
        Assert.Contains("Kvx:Passthrough", message);
    }

    [Fact]
    public void Passthrough_config_rejects_unknown_credentials()
    {
        Assert.Null(Upstream.FromConfig(" ").Credential);
        Assert.NotNull(Upstream.FromConfig("AzureCli, AzureDeveloperCli").Credential);
        Assert.Contains("Nope", Assert.Throws<ConfigurationException>(() => Upstream.FromConfig("AzureCli,Nope")).Message);
    }

    [Fact]
    public void The_service_principal_is_the_only_passthrough_source_when_listed()
    {
        var message = Assert.Throws<ConfigurationException>(() =>
            Upstream.FromConfig("ServicePrincipal, AzureCli", () => new FakeLogin())).Message;
        Assert.Contains("AzureCli", message);
        Assert.Contains("only entry", message);
    }

    [Fact]
    public async Task Token_survives_restart_and_is_replaced_when_the_session_file_is_gone()
    {
        string first;
        using (var kvx = new KvxHost(_dir)) first = kvx.Token;

        using (var restarted = new KvxHost(_dir))
        {
            Assert.Equal(first, restarted.Token);
            var ok = await restarted.SecretClient("https://v.dev.localhost", first).SetSecretAsync("s", "v");
            Assert.Equal("v", ok.Value.Value);
        }

        File.Delete(Path.Combine(_dir.Path, "run", "dev-token"));
        using var newSession = new KvxHost(_dir);
        Assert.NotEqual(first, newSession.Token);
        var rejected = await Assert.ThrowsAsync<Azure.RequestFailedException>(() =>
            newSession.SecretClient("https://v.dev.localhost", first).GetSecretAsync("s"));
        Assert.Equal(401, rejected.Status);
    }

    [Fact]
    public void A_token_written_before_the_last_boot_is_replaced()
    {
        string first;
        using (var kvx = new KvxHost(_dir)) first = kvx.Token;
        var boot = Kvx.Server.MachineBoot.Time();
        Assert.NotNull(boot);   // every OS kvx supports says when it booted
        File.SetLastWriteTimeUtc(Path.Combine(_dir.Path, "run", "dev-token"), boot.Value.AddMinutes(-1));

        using var afterReboot = new KvxHost(_dir);
        Assert.NotEqual(first, afterReboot.Token);
    }
}
