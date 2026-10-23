using Kvx.Server.Api;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

public static class ChallengeAuth
{
    /// <summary>
    /// Key Vault's challenge flow: an unauthenticated request gets 401 with the resource and
    /// authority to fetch a token for. The SDK requires the vault host to end with ".{resource host}".
    /// </summary>
    public static IApplicationBuilder UseChallengeAuth(this IApplicationBuilder app)
    {
        var tokens = app.ApplicationServices.GetRequiredService<DevTokens>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<KvxOptions>>().Value;
        var challenge = $"Bearer authorization=\"{options.AuthorizationUri}\", resource=\"https://{options.Domain}\"";

        return app.Use(async (context, next) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && tokens.IsValid(header.AsSpan(7)))
            {
                await next(context);
                return;
            }

            context.Response.Headers.WWWAuthenticate = challenge;
            await Errors.Write(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                string.IsNullOrEmpty(header) ? "Request is missing a Bearer token." : "The bearer token is not valid.");
        });
    }
}
