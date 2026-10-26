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
/// The factory is keyed by a kvx type rather than Program, which the sample AppHost's entry point also is.
/// </summary>
public sealed class KvxHost(TempDir dir, TimeProvider? time = null, Upstream? upstream = null,
    Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Kvx.Server.KvxOptions>
{
    /// <summary>For <c>configure</c>: ServicePrincipal passthrough signing in through <see cref="FakePrincipal"/>.</summary>
    public static void PrincipalPassthrough(IWebHostBuilder builder)
    {
        builder.UseSetting("Kvx:Passthrough", Upstream.ServicePrincipal);
        builder.ConfigureTestServices(FakePrincipal.Register);
    }

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
        configure?.Invoke(builder);
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
    public static Upstream Upstream { get; } = new(new FakeLogin(), "your fake login", "Sign in with `fake login` and retry.");
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

/// <summary>
/// Stands in for Entra signing in a service principal: a token naming the principal and scope when the secret is
/// <see cref="GoodSecret"/>, and Entra's invalid-secret error otherwise. No test reaches a real tenant.
/// </summary>
public sealed class FakePrincipal(Kvx.Server.Storage.ServicePrincipal principal) : TokenCredential
{
    public const string GoodSecret = "kvx-principal-canary-9c1e";
    public static string TokenFor(string clientId, string scope) => $"principal-token-for {clientId} {scope}";

    /// <summary>Makes kvx's real ServicePrincipal upstream sign in here; registered last, so it wins.</summary>
    public static void Register(IServiceCollection services)
    {
        RegisterFactory(services);
        services.AddSingleton(Upstream.Create);
    }

    /// <summary>Only the sign-in, for apps that ask for a principal by client id; passthrough stays as configured.</summary>
    public static void RegisterFactory(IServiceCollection services) =>
        services.AddSingleton<PrincipalCredentialFactory>(p => new FakePrincipal(p));

    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        principal.Secret == GoodSecret
            ? new(TokenFor(principal.ClientId, context.Scopes[0]), DateTimeOffset.UtcNow.AddHours(1))
            : throw new Azure.Identity.AuthenticationFailedException(
                "ClientSecretCredential authentication failed: AADSTS7000215: Invalid client secret provided.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        new(GetToken(context, ct));
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("kvx-test-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
