using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using Kvx.Server.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kvx.Tests;

/// <summary>
/// kvx in-process over TestServer, isolated to its own data directory and token file, and to a fake developer
/// login so no test ever reaches the real az/azd/Visual Studio session.
/// </summary>
public sealed class KvxHost(TempDir dir, TimeProvider? time = null, Upstream? upstream = null) : WebApplicationFactory<Program>
{
    public string DataDir => Path.Combine(dir.Path, "data");
    public string TokenFile => Path.Combine(dir.Path, "run", "dev-token");
    public string Token => Services.GetRequiredService<DevTokens>().Current;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Kvx:DataDir", DataDir);
        builder.UseSetting("Kvx:TokenFile", TokenFile);
        builder.ConfigureTestServices(s =>
        {
            if (time is not null) s.AddSingleton(time);
            s.AddSingleton(upstream ?? FakeLogin.Upstream);
        });
    }

    /// <summary>A stock SecretClient; only the transport is swapped to reach the in-memory server.</summary>
    public SecretClient SecretClient(string vaultUri, string? token = null) =>
        new(new Uri(vaultUri), new StaticToken(token ?? Token), new SecretClientOptions
        {
            Transport = new HttpClientTransport(CreateClient()),
            Retry = { MaxRetries = 0 },
        });
}

public sealed class StaticToken(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        new(GetToken(context, ct));
}

/// <summary>Stands in for the developer's az login: a recognisable token per scope.</summary>
public sealed class FakeLogin : TokenCredential
{
    public static Upstream Upstream { get; } = new(new FakeLogin(), "`fake login`");
    public static string TokenFor(string scope) => $"developer-token-for {scope}";

    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        new(TokenFor(context.Scopes[0]), DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        new(GetToken(context, ct));
}

/// <summary>A developer who is not signed in anywhere.</summary>
public sealed class NoLogin : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        throw new Azure.Identity.CredentialUnavailableException("Please run 'az login' to set up account.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        new(GetToken(context, ct));
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("kvx-test-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
