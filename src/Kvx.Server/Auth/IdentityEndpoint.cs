using Azure.Core;
using Azure.Identity;
using Kvx.Server.Api;
using Kvx.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

public sealed record IdentityToken(string AccessToken, long ExpiresOn, string Resource, string TokenType);

/// <summary>
/// The App Service managed-identity protocol, so an unmodified DefaultAzureCredential gets tokens from
/// IDENTITY_ENDPOINT and IDENTITY_HEADER alone: kvx's own token for kvx, the <see cref="Upstream"/> for anything else.
/// Errors are 400/401 only: the client retries 404, 408, 429 and 5xx.
/// </summary>
public static class IdentityEndpoint
{
    public const string Path = "/kvx/identity/token";

    /// <summary>Reported lifetime; the token itself lives for the login session, so this only paces refreshes.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private static readonly System.Text.Json.JsonSerializerOptions IdentityJson =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };

    public static void MapIdentity(this IEndpointRouteBuilder app) =>
        app.MapGet(Path, Token).AllowAnonymous();

    private static async Task<IResult> Token(HttpContext context, DevTokens tokens, Upstream upstream,
        SecretRepository store, PrincipalCredentials principals,
        IOptions<KvxOptions> options, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var header = context.Request.Headers["X-IDENTITY-HEADER"].ToString();
        if (header.Length == 0 || !tokens.IsValid(header))
            return Errors.Result(401, "Unauthorized", "X-IDENTITY-HEADER is missing or is not this login session's kvx token.");

        var kvx = $"https://{options.Value.Domain}";
        var resource = context.Request.Query["resource"].ToString().TrimEnd('/');
        if (string.Equals(resource, kvx, StringComparison.OrdinalIgnoreCase))
            return Issue(tokens.Current, time.GetUtcNow().Add(Lifetime), kvx);

        if (resource.Length == 0)
            return Errors.Result(400, "BadParameter", "The 'resource' query parameter is required.");

        var log = loggers.CreateLogger(typeof(IdentityEndpoint));
        var query = context.Request.Query;
        if (new[] { "mi_res_id", "msi_res_id", "object_id", "principal_id" }.FirstOrDefault(k => query[k].ToString().Length > 0) is { } other)
            return Errors.Result(400, "BadParameter",
                $"kvx picks an identity only by client_id, not by {other}. Name the identity by its client id " +
                "(AZURE_CLIENT_ID for DefaultAzureCredential) and store a service principal for it with `kvx principal set`.");

        var clientId = query["client_id"].ToString() is { Length: > 0 } id ? id : query["clientid"].ToString();
        if (clientId.Length > 0)
        {
            // An app that names its identity gets exactly that one or an error, never kvx's passthrough in its place.
            if (store.FindPrincipal(clientId) is not { } principal)
            {
                log.LogWarning("Refused a token for {Resource}: no service principal is stored for client id {ClientId}.", resource, clientId);
                return Errors.Result(400, "BadParameter",
                    $"This app asked for the identity with client id {clientId} (DefaultAzureCredential sends AZURE_CLIENT_ID), " +
                    $"and kvx holds no service principal for it. Run `kvx principal set --tenant <tenant id> --client-id {clientId}`, " +
                    $"or add `--for {clientId}` to the principal that stands in for it; or unset AZURE_CLIENT_ID to use kvx's passthrough.");
            }
            return await Pass(principals.For(principal), $"service principal {principal.ClientId}",
                "`kvx doctor` checks it, and `kvx principal set` replaces a wrong or expired secret.");
        }

        if (upstream.Credential is null)
        {
            // The app will fail on a non-Key Vault Azure client; say so where the developer running kvx looks too.
            log.LogWarning("Refused a token for {Resource}: passthrough is off (Kvx:Passthrough is empty).", resource);
            return Errors.Result(400, "BadParameter",
                $"kvx only issues tokens for {kvx}, not '{resource}', because passthrough is off (Kvx:Passthrough is empty). " +
                "This app gets its DefaultAzureCredential tokens from kvx (IDENTITY_ENDPOINT is set), so it cannot reach other Azure services.");
        }

        return await Pass(upstream.Credential, upstream.From, upstream.Fix);

        async Task<IResult> Pass(TokenCredential credential, string from, string fix)
        {
            try
            {
                var token = await credential.GetTokenAsync(new TokenRequestContext([resource + "/.default"]), ct);
                log.LogInformation("Passed a token request for {Resource} through to {From}.", resource, from);
                return Issue(token.Token, token.ExpiresOn, resource);
            }
            catch (Exception e) when (e is CredentialUnavailableException or AuthenticationFailedException)
            {
                log.LogWarning("Could not pass through a token for {Resource}: {Reason}", resource, e.Message);
                return Errors.Result(400, "BadParameter",
                    $"kvx could not get a token for '{resource}' from {from}. {fix} Details: {e.Message}");
            }
        }
    }

    private static IResult Issue(string token, DateTimeOffset expiresOn, string resource) =>
        Results.Json(new IdentityToken(token, expiresOn.ToUnixTimeSeconds(), resource, "Bearer"), IdentityJson);
}
