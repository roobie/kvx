using System.ComponentModel.DataAnnotations;

namespace Kvx.Server;

public sealed class KvxOptions
{
    /// <summary>Vaults are served at https://&lt;vault&gt;.&lt;Domain&gt;; also the challenge resource host.</summary>
    [Required] public string Domain { get; set; } = "";

    /// <summary>Sent in the auth challenge; the SDK takes the tenant id from its first path segment.</summary>
    [Required, Url] public string AuthorizationUri { get; set; } = "";

    [Required] public string KeyProtector { get; set; } = "";
    [Required] public string KeyCredentialName { get; set; } = "";
    [Required] public string RecoveryLevel { get; set; } = "";
    [Range(7, 90)] public int RecoverableDays { get; set; }

    /// <summary>Empty means $XDG_DATA_HOME/kvx.</summary>
    public string DataDir { get; set; } = "";

    /// <summary>Empty means $XDG_RUNTIME_DIR/kvx/dev-token.</summary>
    public string TokenFile { get; set; } = "";

    /// <summary>
    /// Comma-separated developer credentials (tried in order) that serve token requests for resources other than kvx,
    /// so apps can reach real Azure services too. Empty turns passthrough off. See <see cref="Auth.Upstream"/>.
    /// </summary>
    public string Passthrough { get; set; } = "";

    /// <summary>
    /// Variables `kvx run` and `kvx env` remove from the app's environment, because they would make
    /// DefaultAzureCredential pick another credential before kvx's managed identity.
    /// </summary>
    public string[] RunUnset { get; set; } = [];

    /// <summary>
    /// Linux only: where `dotnet dev-certs https --trust` puts the dev cert for OpenSSL. Processes trust it only
    /// through SSL_CERT_DIR, so `kvx run` and `kvx env` add it. Empty means $HOME/.aspnet/dev-certs/trust.
    /// </summary>
    public string DevCertTrustDir { get; set; } = "";

    public string ResolveDataDir()
    {
        if (!string.IsNullOrEmpty(DataDir)) return DataDir;
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = string.IsNullOrEmpty(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdg;
        return Path.Combine(root, "kvx");
    }

    public string ResolveDevCertTrustDir() =>
        !string.IsNullOrEmpty(DevCertTrustDir)
            ? DevCertTrustDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "dev-certs", "trust");

    public string ResolveTokenFile()
    {
        if (!string.IsNullOrEmpty(TokenFile)) return TokenFile;
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime))
            throw new InvalidOperationException(
                "XDG_RUNTIME_DIR is not set; configure Kvx:TokenFile to a per-user, non-persistent location.");
        return Path.Combine(runtime, "kvx", "dev-token");
    }
}
