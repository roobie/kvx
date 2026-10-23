using Azure.Core;
using Azure.Identity;

namespace Kvx.Server.Auth;

/// <summary>
/// The developer's own Azure login, used for every resource that is not kvx. Never DefaultAzureCredential:
/// if kvx itself ran with IDENTITY_ENDPOINT set, it would ask itself.
/// </summary>
public sealed record Upstream(TokenCredential? Credential, string Description)
{
    private static readonly Dictionary<string, (Func<TokenCredential> Create, string Login)> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AzureCli"] = (() => new AzureCliCredential(), "az login"),
            ["AzureDeveloperCli"] = (() => new AzureDeveloperCliCredential(), "azd auth login"),
            ["VisualStudio"] = (() => new VisualStudioCredential(), "sign in to Visual Studio"),
            ["AzurePowerShell"] = (() => new AzurePowerShellCredential(), "Connect-AzAccount"),
        };

    public static Upstream FromConfig(string names)
    {
        var picked = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = picked.Where(n => !Known.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"Kvx:Passthrough has unknown credential(s) {string.Join(", ", unknown)}; known: {string.Join(", ", Known.Keys)}.");
        if (picked.Length == 0) return new Upstream(null, "");

        var credentials = picked.Select(n => Known[n].Create()).ToArray();
        return new Upstream(credentials.Length == 1 ? credentials[0] : new ChainedTokenCredential(credentials),
            string.Join(" or ", picked.Select(n => $"`{Known[n].Login}`")));
    }
}
