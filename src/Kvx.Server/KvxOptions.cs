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

    public string ResolveDataDir()
    {
        if (!string.IsNullOrEmpty(DataDir)) return DataDir;
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = string.IsNullOrEmpty(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdg;
        return Path.Combine(root, "kvx");
    }

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
