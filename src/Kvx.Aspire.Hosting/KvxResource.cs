using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Kvx;

namespace Aspire.Hosting;

/// <summary>
/// The developer's kvx, as the AppHost sees it: `kvx serve --reuse`, which uses the kvx already serving the
/// developer's store, or serves it itself. Either way the dashboard shows it, with its logs.
/// </summary>
public sealed class KvxResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory)
{
    /// <summary>Settings passed to every kvx command, as `--Key=value`; from the AppHost's Kvx:Settings.</summary>
    public List<string> Settings { get; } = [];

    /// <summary>Known once the AppHost starts; null before, and in publish mode.</summary>
    public KvxDescription? Description { get; internal set; }

    /// <summary>The certificates kvx serves under, trusted by every resource that references a kvx vault.</summary>
    internal CertificateAuthorityCollection Trust { get; } = new($"{name}-trust");

    internal KvxDescription RequireDescription() =>
        Description ?? throw new InvalidOperationException("kvx has not been described yet; the AppHost has not started.");
}
