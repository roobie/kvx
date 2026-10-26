using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

/// <summary>
/// Where tokens for resources other than kvx come from: the developer's own Azure login, or the service principal kvx
/// holds (<see cref="PrincipalCredential"/>). Never DefaultAzureCredential: if kvx itself ran with IDENTITY_ENDPOINT
/// set, it would ask itself. <paramref name="From"/> and <paramref name="Fix"/> complete the message an app gets
/// when the upstream fails.
/// </summary>
public sealed record Upstream(TokenCredential? Credential, string From, string Fix)
{
    public const string ServicePrincipal = "ServicePrincipal";

    private static readonly Dictionary<string, (Func<TokenCredential> Create, string Login)> Logins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AzureCli"] = (() => new AzureCliCredential(), "az login"),
            ["AzureDeveloperCli"] = (() => new AzureDeveloperCliCredential(), "azd auth login"),
            ["VisualStudio"] = (() => new VisualStudioCredential(), "sign in to Visual Studio"),
            ["AzurePowerShell"] = (() => new AzurePowerShellCredential(), "Connect-AzAccount"),
        };

    public static Upstream Create(IServiceProvider services) =>
        FromConfig(services.GetRequiredService<IOptions<KvxOptions>>().Value.Passthrough,
            services.GetRequiredService<PrincipalCredential>);

    public static Upstream FromConfig(string names, Func<TokenCredential>? principal = null)
    {
        var picked = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (picked.Length == 0) return new Upstream(null, "", "");

        if (picked.Contains(ServicePrincipal, StringComparer.OrdinalIgnoreCase))
        {
            // A chain moves on when a credential is unavailable, so a principal that isn't set would quietly
            // become the developer: the very thing this mode is chosen to avoid.
            if (picked.Length > 1)
                throw new ConfigurationException(
                    $"Kvx:Passthrough lists {ServicePrincipal} with {string.Join(", ", picked.Where(n => !n.Equals(ServicePrincipal, StringComparison.OrdinalIgnoreCase)))}; " +
                    $"{ServicePrincipal} must be the only entry, so that apps never fall back to acting as you.");
            return new Upstream(principal?.Invoke() ?? throw new ConfigurationException($"{ServicePrincipal} passthrough needs kvx's store."),
                "the service principal kvx signs in as",
                "`kvx doctor` checks it, and `kvx principal set` replaces a wrong or expired secret.");
        }

        var unknown = picked.Where(n => !Logins.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
            throw new ConfigurationException(
                $"Kvx:Passthrough has unknown credential(s) {string.Join(", ", unknown)}; known: {string.Join(", ", Logins.Keys.Append(ServicePrincipal))}.");

        var credentials = picked.Select(n => Logins[n].Create()).ToArray();
        return new Upstream(credentials.Length == 1 ? credentials[0] : new ChainedTokenCredential(credentials),
            "your own Azure login",
            $"Sign in with {string.Join(" or ", picked.Select(n => $"`{Logins[n].Login}`"))} and retry.");
    }
}
