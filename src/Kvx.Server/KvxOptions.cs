using System.ComponentModel.DataAnnotations;

namespace Kvx.Server;

public sealed class KvxOptions
{
    /// <summary>Vaults are served at https://&lt;vault&gt;.&lt;Domain&gt;; also the challenge resource host.</summary>
    [Required] public string Domain { get; set; } = "";

    /// <summary>Sent in the auth challenge; the SDK takes the tenant id from its first path segment.</summary>
    [Required, Url] public string AuthorizationUri { get; set; } = "";

    /// <summary>systemd-creds, dpapi or keychain; empty means the OS's own (see <see cref="Keys.KeyProtectors.Name"/>).</summary>
    public string KeyProtector { get; set; } = "";
    [Required] public string KeyCredentialName { get; set; } = "";
    [Required] public string RecoveryLevel { get; set; } = "";
    [Range(7, 90)] public int RecoverableDays { get; set; }

    /// <summary>Empty means the user's local app data: $XDG_DATA_HOME/kvx, %LOCALAPPDATA%\kvx, ~/Library/Application Support/kvx.</summary>
    public string DataDir { get; set; } = "";

    /// <summary>Empty means kvx/dev-token in $XDG_RUNTIME_DIR on Linux, the user's temp dir on Windows, the user cache dir (/var/folders/…/C) on macOS.</summary>
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
    /// Linux only: where `dotnet dev-certs https --trust` puts the dev cert for OpenSSL. .NET apps already trust it
    /// through their own user store; OpenSSL-based tools see it only through SSL_CERT_DIR, so `kvx run` and
    /// `kvx env` add it. Empty means $HOME/.aspnet/dev-certs/trust.
    /// </summary>
    public string DevCertTrustDir { get; set; } = "";

    public string ResolveDataDir()
    {
        if (!string.IsNullOrEmpty(DataDir)) return DataDir;
        // .NET maps this to each OS's convention, XDG_DATA_HOME included on Linux.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify), "kvx");
    }

    public string ResolveDevCertTrustDir() =>
        !string.IsNullOrEmpty(DevCertTrustDir)
            ? DevCertTrustDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "dev-certs", "trust");

    public string ResolveTokenFile()
    {
        if (!string.IsNullOrEmpty(TokenFile)) return TokenFile;
        // Windows and macOS have no per-user directory cleared at logout; these are private to the user, and
        // DevTokens drops a token older than the last boot. Not macOS's temp dir: it purges files unread for days,
        // which would strand the clients of an idle kvx.
        if (OperatingSystem.IsMacOS())
            return Path.Combine(MacUserDirs.Cache() ?? throw new InvalidOperationException(
                "macOS did not name the user cache dir; configure Kvx:TokenFile."), "kvx", "dev-token");
        if (OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "kvx", "dev-token");
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime))
            throw new InvalidOperationException(
                "XDG_RUNTIME_DIR is not set; configure Kvx:TokenFile to a per-user, non-persistent location.");
        return Path.Combine(runtime, "kvx", "dev-token");
    }
}
