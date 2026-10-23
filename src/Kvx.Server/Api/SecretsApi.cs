using System.Buffers.Text;
using System.Text.RegularExpressions;
using Kvx.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Api;

public static partial class SecretsApi
{
    [GeneratedRegex("^[0-9a-zA-Z-]{1,127}$")]
    private static partial Regex SecretName();

    private const string VaultItem = "kvx.vault";

    public static void MapSecrets(this IEndpointRouteBuilder app)
    {
        var vault = app.MapGroup("").AddEndpointFilter(ResolveAndMapErrors);

        vault.MapGet("/secrets", List);
        vault.MapPut("/secrets/{name}", Set);
        vault.MapGet("/secrets/{name}/versions", Versions);
        vault.MapGet("/secrets/{name}/{version?}", Get);
        vault.MapPatch("/secrets/{name}/{version}", Update);
        vault.MapDelete("/secrets/{name}", Delete);
        vault.MapPost("/secrets/{name}/backup", Backup);
        vault.MapPost("/secrets/restore", Restore);

        vault.MapGet("/deletedsecrets", ListDeleted);
        vault.MapGet("/deletedsecrets/{name}", GetDeleted);
        vault.MapDelete("/deletedsecrets/{name}", Purge);
        vault.MapPost("/deletedsecrets/{name}/recover", Recover);
    }

    // --- live secrets ---

    private static IResult Set(string name, SetSecretRequest body, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        if (body.Value is null) return Errors.Result(400, "BadParameter", "The secret value is required.");
        var (n, v) = repo.Set(context.Vault(), name, body.Value, body.ContentType, body.Tags,
            body.Attributes?.Enabled ?? true, body.Attributes?.Nbf, body.Attributes?.Exp);
        return Results.Ok(Bundle(context, options.Value, n, v, withValue: true));
    }

    private static IResult Get(string name, string? version, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        var (n, v) = repo.Get(context.Vault(), name, version);
        if (!v.Enabled)
            return Errors.Result(403, "Forbidden", "Operation get is not allowed on a disabled secret.", "SecretDisabled");
        return Results.Ok(Bundle(context, options.Value, n, v, withValue: true));
    }

    private static IResult List(HttpContext context, SecretRepository repo, IOptions<KvxOptions> options) =>
        Results.Ok(Paging.Page(context, repo.List(context.Vault()), s => s.Name,
            s => Item(options.Value, SecretUri(context, s.Name), s.Latest)));

    private static IResult Versions(string name, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        var (n, versions) = repo.Versions(context.Vault(), name);
        return Results.Ok(Paging.Page(context, versions, v => v.Version,
            v => Item(options.Value, SecretUri(context, n, v.Version), v)));
    }

    private static IResult Update(string name, string version, UpdateSecretRequest body, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        var (n, v) = repo.Update(context.Vault(), name, version, body.ContentType, body.Tags,
            body.Attributes?.Enabled, body.Attributes?.Nbf, body.Attributes?.Exp);
        return Results.Ok(Bundle(context, options.Value, n, v, withValue: false));
    }

    private static IResult Delete(string name, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options) =>
        Results.Ok(DeletedBody(context, options.Value, repo.Delete(context.Vault(), name), withVersion: true));

    private static IResult Backup(string name, HttpContext context, SecretRepository repo) =>
        Results.Ok(new BackupBody(Base64Url.EncodeToString(repo.Backup(context.Vault(), name))));

    private static IResult Restore(BackupBody body, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        byte[] blob;
        try { blob = Base64Url.DecodeFromChars(body.Value); }
        catch (FormatException) { return Errors.Result(400, "BadParameter", "The backup value is not base64url."); }
        var (n, v) = repo.Restore(context.Vault(), blob);
        return Results.Ok(Bundle(context, options.Value, n, v, withValue: false));
    }

    // --- deleted secrets ---

    private static IResult ListDeleted(HttpContext context, SecretRepository repo, IOptions<KvxOptions> options) =>
        Results.Ok(Paging.Page(context, repo.ListDeleted(context.Vault()), d => d.Name,
            d => DeletedBody(context, options.Value, d, withVersion: false)));

    private static IResult GetDeleted(string name, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options) =>
        Results.Ok(DeletedBody(context, options.Value, repo.GetDeleted(context.Vault(), name), withVersion: true));

    private static IResult Purge(string name, HttpContext context, SecretRepository repo)
    {
        repo.Purge(context.Vault(), name);
        return Results.NoContent();
    }

    private static IResult Recover(string name, HttpContext context, SecretRepository repo, IOptions<KvxOptions> options)
    {
        var (n, v) = repo.Recover(context.Vault(), name);
        return Results.Ok(Bundle(context, options.Value, n, v, withValue: false));
    }

    // --- shared ---

    /// <summary>Resolves the vault from the host, validates the name, and turns store problems into Key Vault errors.</summary>
    private static async ValueTask<object?> ResolveAndMapErrors(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var options = context.RequestServices.GetRequiredService<IOptions<KvxOptions>>().Value;
        if (ResolveVault(context, options) is not { } vault)
            return Errors.Result(400, "BadParameter", $"Host '{context.Request.Host.Host}' does not name a kvx vault.");
        if (context.Request.RouteValues.TryGetValue("name", out var name) && !SecretName().IsMatch(name as string ?? ""))
            return Errors.Result(400, "BadParameter", $"Secret name '{name}' is invalid; use 1-127 letters, digits and dashes.");

        context.Items[VaultItem] = vault;
        try
        {
            return await next(invocation);
        }
        catch (StoreException e)
        {
            return e.Problem switch
            {
                StoreProblem.NotFound => Errors.Result(404, "SecretNotFound", e.Message),
                StoreProblem.DeletedButRecoverable => Errors.Result(409, "Conflict", e.Message, "ObjectIsDeletedButRecoverable"),
                StoreProblem.AlreadyExists => Errors.Result(409, "Conflict", e.Message),
                StoreProblem.InvalidBackup => Errors.Result(400, "BadParameter", e.Message),
                _ => throw new InvalidOperationException($"Unmapped store problem {e.Problem}", e),
            };
        }
    }

    private static string Vault(this HttpContext context) => (string)context.Items[VaultItem]!;

    /// <summary>The vault is the single label in front of the configured domain.</summary>
    private static string? ResolveVault(HttpContext context, KvxOptions options)
    {
        var host = context.Request.Host.Host;
        var suffix = "." + options.Domain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var vault = host[..^suffix.Length];
        return vault.Length > 0 && !vault.Contains('.') ? vault.ToLowerInvariant() : null;
    }

    // The SDK derives name and version from ids, and expects /secrets/ ids even for deleted secrets.
    private static string SecretUri(HttpContext context, string name, string? version = null) =>
        $"https://{context.Request.Host}/secrets/{name}{(version is null ? "" : "/" + version)}";

    private static SecretAttributes Attributes(KvxOptions options, SecretVersion v) =>
        new(v.Enabled, v.NotBefore, v.Expires, v.Created, v.Updated, options.RecoveryLevel, options.RecoverableDays);

    private static SecretBundle Bundle(HttpContext context, KvxOptions options, string name, SecretVersion v, bool withValue) =>
        new(withValue ? v.Value : null, SecretUri(context, name, v.Version), v.ContentType, v.Tags, Attributes(options, v));

    private static SecretItem Item(KvxOptions options, string id, SecretVersion v) =>
        new(id, v.ContentType, v.Tags, Attributes(options, v));

    private static DeletedSecretBody DeletedBody(HttpContext context, KvxOptions options, DeletedSecret d, bool withVersion) =>
        new(SecretUri(context, d.Name, withVersion ? d.Latest.Version : null),
            d.Latest.ContentType, d.Latest.Tags, Attributes(options, d.Latest),
            $"https://{context.Request.Host}/deletedsecrets/{d.Name}", d.DeletedDate, d.ScheduledPurgeDate);
}
