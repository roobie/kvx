using System.Text.RegularExpressions;
using Kvx.Server.Storage;

namespace Kvx.Server.Api;

public sealed record PrincipalRequest(string? TenantId, string? ClientId, string? Secret);

/// <summary>What kvx tells about the principal: never its secret.</summary>
public sealed record PrincipalView(string TenantId, string ClientId, long Updated);

/// <summary>
/// The service principal passthrough signs in as, for `kvx principal`. Like the vault API it needs the session token;
/// unlike it, it never returns the secret, so apps under kvx can get tokens from the principal but not its credential.
/// </summary>
public static partial class PrincipalApi
{
    public const string Path = "/kvx/principal";

    public static void MapPrincipal(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (SecretRepository store) =>
            store.Principal is { } p ? Results.Json(View(p)) : NotSet());

        app.MapPut(Path, (PrincipalRequest body, SecretRepository store) =>
        {
            if (body.TenantId is null || !TenantId().IsMatch(body.TenantId))
                return Errors.Result(400, "BadParameter", "tenantId must be a tenant id (a GUID) or a domain such as contoso.onmicrosoft.com.");
            if (!Guid.TryParse(body.ClientId, out _))
                return Errors.Result(400, "BadParameter", "clientId must be the app registration's application (client) id, a GUID.");
            if (string.IsNullOrEmpty(body.Secret))
                return Errors.Result(400, "BadParameter", "secret must be the app registration's client secret value.");
            return Results.Json(View(store.SetPrincipal(body.TenantId, body.ClientId!, body.Secret)));
        });

        app.MapDelete(Path, (SecretRepository store) => store.ClearPrincipal() ? Results.NoContent() : NotSet());
    }

    private static PrincipalView View(ServicePrincipal p) => new(p.TenantId, p.ClientId, p.Updated);

    private static IResult NotSet() => Errors.Result(404, "NotFound", "No service principal is set.");

    /// <summary>What Azure.Identity accepts as a tenant id.</summary>
    [GeneratedRegex("^[0-9a-zA-Z.-]{1,256}$")]
    private static partial Regex TenantId();
}
