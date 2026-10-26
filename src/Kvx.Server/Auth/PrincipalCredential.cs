using Azure.Core;
using Azure.Identity;
using Kvx.Server.Storage;

namespace Kvx.Server.Auth;

public delegate TokenCredential PrincipalCredentialFactory(ServicePrincipal principal);

/// <summary>
/// Signs in as the service principal in kvx's store, read on every request so `kvx principal set` takes effect without
/// a restart. The underlying credential is rebuilt only when the principal changes, which keeps MSAL's in-memory
/// token cache; nothing is cached on disk.
/// </summary>
public sealed class PrincipalCredential(SecretRepository store, PrincipalCredentialFactory create) : TokenCredential
{
    private readonly Lock _lock = new();
    private (ServicePrincipal Principal, TokenCredential Credential)? _current;

    public static TokenCredential ClientSecret(ServicePrincipal p) => new ClientSecretCredential(p.TenantId, p.ClientId, p.Secret);

    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        GetTokenAsync(context, ct).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct)
    {
        var (principal, credential) = Current();
        try
        {
            return await credential.GetTokenAsync(context, ct);
        }
        catch (AuthenticationFailedException e)
        {
            throw new AuthenticationFailedException(
                $"service principal {principal.ClientId} in tenant {principal.TenantId} could not sign in: {e.Message}", e);
        }
    }

    private (ServicePrincipal, TokenCredential) Current()
    {
        var principal = store.Principal ?? throw new CredentialUnavailableException(
            "no service principal is set; run `kvx principal set --tenant <tenant id> --client-id <client id>` and give it the client secret.");
        lock (_lock)
        {
            if (_current is not { } current || current.Principal != principal)
                _current = current = (principal, create(principal));
            return current;
        }
    }
}
