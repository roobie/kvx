using System.Text.RegularExpressions;
using Kvx.Server.Auth;
using Kvx.Server.Storage;

namespace Kvx.Server.Api;

public sealed record PrincipalRequest(string? TenantId, string? ClientId, string? Secret);

/// <summary>What kvx tells about the principal: never its secret.</summary>
public sealed record PrincipalView(string TenantId, string ClientId, long Updated);

/// <summary>
/// The stored principal, and whether this kvx's passthrough signs in as it. The serving kvx answers the second part
/// because only its own settings count: a client command run without the server's overrides would guess wrong.
/// </summary>
public sealed record PrincipalState(bool Used, PrincipalView? Principal);

/// <summary>
/// The service principal passthrough signs in as, for `kvx principal`. Like the vault API it needs the session token;
/// unlike it, it never returns the secret, so apps under kvx can get tokens from the principal but not its credential.
/// </summary>
public static partial class PrincipalApi
{
    public const string Path = "/kvx/principal";

    public static void MapPrincipal(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (SecretRepository store, Upstream upstream) => State(upstream, store.Principal));

        app.MapPut(Path, (PrincipalRequest body, SecretRepository store, Upstream upstream) =>
        {
            if (body.TenantId is null || !TenantId().IsMatch(body.TenantId))
                return Errors.Result(400, "BadParameter", "tenantId must be a tenant id (a GUID) or a domain such as contoso.onmicrosoft.com.");
            if (!Guid.TryParse(body.ClientId, out _))
                return Errors.Result(400, "BadParameter", "clientId must be the app registration's application (client) id, a GUID.");
            if (string.IsNullOrEmpty(body.Secret))
                return Errors.Result(400, "BadParameter", "secret must be the app registration's client secret value.");
            return State(upstream, store.SetPrincipal(body.TenantId, body.ClientId!, body.Secret));
        });

        app.MapDelete(Path, (SecretRepository store) => store.ClearPrincipal()
            ? Results.NoContent()
            : Errors.Result(404, "NotFound", "No service principal is set."));
    }

    private static IResult State(Upstream upstream, ServicePrincipal? p) =>
        Results.Json(new PrincipalState(upstream.UsesPrincipal, p is null ? null : new(p.TenantId, p.ClientId, p.Updated)));

    /// <summary>What Azure.Identity accepts as a tenant id.</summary>
    [GeneratedRegex("^[0-9a-zA-Z.-]{1,256}$")]
    private static partial Regex TenantId();
}
