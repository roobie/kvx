using Azure.Core;
using Azure.Identity;
using Kvx.Server.Storage;

namespace Kvx.Server.Auth;

public delegate TokenCredential PrincipalCredentialFactory(ServicePrincipal principal);

/// <summary>
/// Signs in as the service principals in kvx's store. Each is read from the store per request, so `kvx principal set`
/// takes effect without a restart; its credential is rebuilt only when its tenant or secret changes, which keeps
/// MSAL's in-memory token cache. Nothing is cached on disk.
/// </summary>
public sealed class PrincipalCredentials(SecretRepository store, PrincipalCredentialFactory create)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (ServicePrincipal Principal, TokenCredential Credential)> _built =
        new(StringComparer.OrdinalIgnoreCase);

    public static TokenCredential ClientSecret(ServicePrincipal p) => new ClientSecretCredential(p.TenantId, p.ClientId, p.Secret);

    /// <summary>For Kvx:Passthrough=ServicePrincipal: the default principal, for apps that name no identity.</summary>
    public TokenCredential Default => field ??= new Signer(this, () => store.DefaultPrincipal ?? throw new CredentialUnavailableException(
        "no default service principal is set; run `kvx principal set --tenant <tenant id> --client-id <client id> --default` " +
        "and give it the client secret."));

    /// <summary>For an app that asked for <paramref name="principal"/> by its client id.</summary>
    public TokenCredential For(ServicePrincipal principal) => new Signer(this, () => principal);

    public static bool IsDefault(TokenCredential? credential) => credential is Signer { IsDefault: true };

    private async ValueTask<AccessToken> GetTokenAsync(ServicePrincipal principal, TokenRequestContext context, CancellationToken ct)
    {
        try
        {
            return await Built(principal).GetTokenAsync(context, ct);
        }
        catch (AuthenticationFailedException e)
        {
            throw new AuthenticationFailedException(
                $"service principal {principal.ClientId} in tenant {principal.TenantId} could not sign in: {e.Message}", e);
        }
    }

    private TokenCredential Built(ServicePrincipal principal)
    {
        lock (_lock)
        {
            if (!_built.TryGetValue(principal.ClientId, out var built)
                || built.Principal.TenantId != principal.TenantId || built.Principal.Secret != principal.Secret)
                _built[principal.ClientId] = built = (principal, create(principal));
            return built.Credential;
        }
    }

    private sealed class Signer(PrincipalCredentials all, Func<ServicePrincipal> principal) : TokenCredential
    {
        public bool IsDefault => ReferenceEquals(this, all.Default);

        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
            GetTokenAsync(context, ct).AsTask().GetAwaiter().GetResult();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
            all.GetTokenAsync(principal(), context, ct);
    }
}
