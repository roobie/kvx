using System.Text.RegularExpressions;
using Kvx.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Api;

public sealed record SetSecretRequest(
    string? Value, string? ContentType, Dictionary<string, string>? Tags, SetSecretAttributes? Attributes);

public sealed record SetSecretAttributes(bool? Enabled, long? Nbf, long? Exp);

public sealed record SecretAttributes(
    bool Enabled, long? Nbf, long? Exp, long Created, long Updated, string RecoveryLevel, int RecoverableDays);

public sealed record SecretBundle(
    string Value, string Id, string? ContentType, Dictionary<string, string>? Tags, SecretAttributes Attributes);

public static partial class SecretsApi
{
    [GeneratedRegex("^[0-9a-zA-Z-]{1,127}$")]
    private static partial Regex SecretName();

    public static void MapSecrets(this IEndpointRouteBuilder app)
    {
        app.MapPut("/secrets/{name}", Set);
        app.MapGet("/secrets/{name}/{version?}", Get);
    }

    private static IResult Set(
        string name, SetSecretRequest body, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        if (ResolveVault(context, options.Value) is not { } vault) return UnknownVault(context);
        if (!SecretName().IsMatch(name)) return BadName(name);
        if (body.Value is null) return Errors.Result(400, "BadParameter", "The secret value is required.");

        var (secret, version) = repo.Set(vault, name, body.Value, body.ContentType, body.Tags,
            body.Attributes?.Enabled ?? true, body.Attributes?.Nbf, body.Attributes?.Exp);
        return Results.Ok(Bundle(context, options.Value, secret, version));
    }

    private static IResult Get(
        string name, string? version, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        if (ResolveVault(context, options.Value) is not { } vault) return UnknownVault(context);
        if (!SecretName().IsMatch(name)) return BadName(name);

        if (repo.Get(vault, name, version) is not var (secret, found))
            return Errors.Result(404, "SecretNotFound",
                $"A secret with (name/id) {name}{(string.IsNullOrEmpty(version) ? "" : "/" + version)} was not found in this key vault.");
        if (!found.Enabled)
            return Errors.Result(403, "Forbidden", "Operation get is not allowed on a disabled secret.", "SecretDisabled");

        return Results.Ok(Bundle(context, options.Value, secret, found));
    }

    /// <summary>The vault is the single label in front of the configured domain.</summary>
    private static string? ResolveVault(HttpContext context, KvxOptions options)
    {
        var host = context.Request.Host.Host;
        var suffix = "." + options.Domain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var vault = host[..^suffix.Length];
        return vault.Length > 0 && !vault.Contains('.') ? vault.ToLowerInvariant() : null;
    }

    private static IResult UnknownVault(HttpContext context) =>
        Errors.Result(400, "BadParameter", $"Host '{context.Request.Host.Host}' does not name a kvx vault.");

    private static IResult BadName(string name) =>
        Errors.Result(400, "BadParameter", $"Secret name '{name}' is invalid; use 1-127 letters, digits and dashes.");

    private static SecretBundle Bundle(HttpContext context, KvxOptions options, SecretDocument secret, SecretVersion v) =>
        new(v.Value,
            $"https://{context.Request.Host}/secrets/{secret.Name}/{v.Version}",
            v.ContentType,
            v.Tags,
            new SecretAttributes(v.Enabled, v.NotBefore, v.Expires, v.Created, v.Updated,
                options.RecoveryLevel, options.RecoverableDays));
}
