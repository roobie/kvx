using Azure.Core;
using Azure.Identity;
using Kvx.Server.Api;
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
        if (upstream.Credential is null)
        {
            // The app will fail on a non-Key Vault Azure client; say so where the developer running kvx looks too.
            log.LogWarning("Refused a token for {Resource}: passthrough is off (Kvx:Passthrough is empty).", resource);
            return Errors.Result(400, "BadParameter",
                $"kvx only issues tokens for {kvx}, not '{resource}', because passthrough is off (Kvx:Passthrough is empty). " +
                "This app gets its DefaultAzureCredential tokens from kvx (IDENTITY_ENDPOINT is set), so it cannot reach other Azure services.");
        }

        try
        {
            var token = await upstream.Credential.GetTokenAsync(new TokenRequestContext([resource + "/.default"]), ct);
            log.LogInformation("Passed a token request for {Resource} through to {From}.", resource, upstream.From);
            return Issue(token.Token, token.ExpiresOn, resource);
        }
        catch (Exception e) when (e is CredentialUnavailableException or AuthenticationFailedException)
        {
            log.LogWarning("Could not pass through a token for {Resource}: {Reason}", resource, e.Message);
            return Errors.Result(400, "BadParameter",
                $"kvx could not get a token for '{resource}' from {upstream.From}. {upstream.Fix} Details: {e.Message}");
        }
    }

    private static IResult Issue(string token, DateTimeOffset expiresOn, string resource) =>
        Results.Json(new IdentityToken(token, expiresOn.ToUnixTimeSeconds(), resource, "Bearer"), IdentityJson);
}
