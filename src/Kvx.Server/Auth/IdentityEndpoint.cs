using Kvx.Server.Api;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

public sealed record IdentityToken(string AccessToken, long ExpiresOn, string Resource, string TokenType);

/// <summary>
/// The App Service managed-identity protocol, so an unmodified DefaultAzureCredential gets a kvx token from
/// IDENTITY_ENDPOINT and IDENTITY_HEADER alone. Errors are 400/401 only: the client retries 404, 408, 429 and 5xx.
/// </summary>
public static class IdentityEndpoint
{
    public const string Path = "/kvx/identity/token";

    /// <summary>Reported lifetime; the token itself lives for the login session, so this only paces refreshes.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static void MapIdentity(this IEndpointRouteBuilder app) =>
        app.MapGet(Path, Token).AllowAnonymous();

    private static IResult Token(HttpContext context, DevTokens tokens, IOptions<KvxOptions> options, TimeProvider time)
    {
        var header = context.Request.Headers["X-IDENTITY-HEADER"].ToString();
        if (header.Length == 0 || !tokens.IsValid(header))
            return Errors.Result(401, "Unauthorized", "X-IDENTITY-HEADER is missing or is not this login session's kvx token.");

        var expected = $"https://{options.Value.Domain}";
        var resource = context.Request.Query["resource"].ToString().TrimEnd('/');
        if (!string.Equals(resource, expected, StringComparison.OrdinalIgnoreCase))
            return Errors.Result(400, "BadParameter",
                $"kvx only issues tokens for {expected}, not '{resource}'. The app's DefaultAzureCredential is using kvx for every resource.");

        return Results.Json(new IdentityToken(tokens.Current, time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds(), expected, "Bearer"),
            IdentityJson);
    }

    private static readonly System.Text.Json.JsonSerializerOptions IdentityJson =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
}
