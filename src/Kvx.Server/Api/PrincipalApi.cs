using System.Text.RegularExpressions;
using Kvx.Server.Auth;
using Kvx.Server.Storage;

namespace Kvx.Server.Api;

/// <summary>Null <paramref name="Aliases"/> keeps the stored ones, so rotating a secret needs only the ids.</summary>
public sealed record PrincipalRequest(string? TenantId, string? ClientId, string? Secret, string[]? Aliases = null, bool Default = false);

/// <summary>What kvx tells about a principal: never its secret.</summary>
public sealed record PrincipalView(string TenantId, string ClientId, string[] Aliases, bool Default, long Updated);

/// <summary>
/// The stored principals, and whether this kvx's passthrough signs in as the default one. The serving kvx answers the
/// second part because only its own settings count: a client command run without the server's overrides would guess wrong.
/// </summary>
public sealed record PrincipalState(bool DefaultUsed, PrincipalView[] Principals);

/// <summary>
/// The service principals kvx signs in as, for `kvx principal`. Like the vault API it needs the session token;
/// unlike it, it never returns a secret, so apps under kvx can get tokens from a principal but not its credential.
/// </summary>
public static partial class PrincipalApi
{
    public const string Path = "/kvx/principal";

    public static void MapPrincipal(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (SecretRepository store, Upstream upstream) => State(upstream, store));

        app.MapPut(Path, (PrincipalRequest body, SecretRepository store, Upstream upstream) =>
        {
            if (body.TenantId is null || !TenantId().IsMatch(body.TenantId))
                return Errors.Result(400, "BadParameter", "tenantId must be a tenant id (a GUID) or a domain such as contoso.onmicrosoft.com.");
            if (!Guid.TryParse(body.ClientId, out _))
                return Errors.Result(400, "BadParameter", "clientId must be the app registration's application (client) id, a GUID.");
            if (body.Aliases?.FirstOrDefault(a => !Guid.TryParse(a, out _)) is { } bad)
                return Errors.Result(400, "BadParameter", $"alias '{bad}' must be a client id (a GUID), such as a managed identity's.");
            if (string.IsNullOrEmpty(body.Secret))
                return Errors.Result(400, "BadParameter", "secret must be the app registration's client secret value.");
            try
            {
                store.SetPrincipal(body.TenantId, body.ClientId!, body.Secret, body.Aliases, body.Default);
            }
            catch (StoreException e) when (e.Problem == StoreProblem.AlreadyExists)
            {
                return Errors.Result(409, "Conflict", e.Message);
            }
            return State(upstream, store);
        });

        app.MapDelete(Path + "/{clientId}", (string clientId, SecretRepository store) => store.ClearPrincipal(clientId)
            ? Results.NoContent()
            : Errors.Result(404, "NotFound", $"No service principal with client id {clientId} is stored."));
    }

    private static IResult State(Upstream upstream, SecretRepository store) =>
        Results.Json(new PrincipalState(upstream.UsesPrincipal,
            [.. store.Principals.Select(p => new PrincipalView(p.TenantId, p.ClientId, p.Aliases, p.Default, p.Updated))]));

    /// <summary>What Azure.Identity accepts as a tenant id.</summary>
    [GeneratedRegex("^[0-9a-zA-Z.-]{1,256}$")]
    private static partial Regex TenantId();
}
