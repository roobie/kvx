using System.Net;
using System.Text;
using Azure;

namespace Kvx.Tests;

public sealed class SecretsTests : IDisposable
{
    private const string Vault = "https://test.kvx.localhost";
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Set_then_get_latest_and_by_version()
    {
        using var host = new KvxHost(_dir);
        var client = host.SecretClient(Vault);

        var first = (await client.SetSecretAsync("db-password", "one")).Value;
        var second = (await client.SetSecretAsync("db-password", "two")).Value;

        Assert.Equal("two", (await client.GetSecretAsync("db-password")).Value.Value);
        Assert.Equal("one", (await client.GetSecretAsync("db-password", first.Properties.Version)).Value.Value);
        Assert.NotEqual(first.Properties.Version, second.Properties.Version);
        Assert.Equal(new Uri($"{Vault}/secrets/db-password/{second.Properties.Version}"), second.Id);
    }

    [Fact]
    public async Task Missing_secret_is_404_SecretNotFound()
    {
        using var host = new KvxHost(_dir);
        var e = await Assert.ThrowsAsync<RequestFailedException>(() => host.SecretClient(Vault).GetSecretAsync("nope"));
        Assert.Equal(404, e.Status);
        Assert.Equal("SecretNotFound", e.ErrorCode);
    }

    [Fact]
    public async Task Survives_restart_and_leaves_no_plaintext_on_disk()
    {
        const string name = "kvx-canary-name";
        const string value = "kvx-canary-value-7f3a";
        using (var first = new KvxHost(_dir))
            await first.SecretClient(Vault).SetSecretAsync(name, value);

        using var second = new KvxHost(_dir);
        Assert.Equal(value, (await second.SecretClient(Vault).GetSecretAsync(name)).Value.Value);

        var files = Directory.GetFiles(second.DataDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            foreach (var needle in new[] { name, value })
                Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) < 0, $"{needle} found in {file}");
        }
    }

    [Fact]
    public async Task Token_from_a_previous_run_is_rejected()
    {
        string stale;
        using (var first = new KvxHost(_dir))
        {
            stale = first.Token;
            await first.SecretClient(Vault).SetSecretAsync("s", "v");
        }

        using var second = new KvxHost(_dir);
        var e = await Assert.ThrowsAsync<RequestFailedException>(
            () => second.SecretClient(Vault, token: stale).GetSecretAsync("s"));
        Assert.Equal(401, e.Status);
    }

    [Fact]
    public async Task Request_without_token_gets_key_vault_challenge()
    {
        using var host = new KvxHost(_dir);
        var response = await host.CreateClient().GetAsync($"{Vault}/secrets/s/?api-version=7.6");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource=\"https://kvx.localhost\"", challenge);
        Assert.Contains("authorization=\"https://login.kvx.localhost/kvx\"", challenge);
    }

    [Fact]
    public async Task Sdk_verifies_challenge_resource_against_vault_host()
    {
        // A host outside the challenge resource's domain must be refused by the SDK itself,
        // which proves the verification runs (and therefore passes) for *.kvx.localhost.
        using var host = new KvxHost(_dir);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.SecretClient("https://test.elsewhere.localhost").GetSecretAsync("s"));
        Assert.Contains("DisableChallengeResourceVerification", e.Message);
    }

    [Fact]
    public void Token_file_is_private_to_the_user()
    {
        using var host = new KvxHost(_dir);
        _ = host.Server;
        Assert.Equal(host.Token, File.ReadAllText(host.TokenFile));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.TokenFile));
    }
}
