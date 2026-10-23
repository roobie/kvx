using System.Runtime.InteropServices;

namespace Kvx.Server.Keys;

/// <summary>Wraps the store's data key with a key the OS holds for the current user.</summary>
public interface IKeyProtector
{
    byte[] Wrap(byte[] key);
    byte[] Unwrap(byte[] wrapped);
}

public sealed class KeyProtectorException(string message, Exception? inner = null) : Exception(message, inner);

public static class KeyProtectors
{
    public const string SystemdCreds = "systemd-creds", Dpapi = "dpapi", Keychain = "keychain";

    /// <summary>The protector Kvx:KeyProtector names, or the OS's own when it names none.</summary>
    public static string Name(KvxOptions options) =>
        !string.IsNullOrEmpty(options.KeyProtector) ? options.KeyProtector
        : OperatingSystem.IsWindows() ? Dpapi
        : OperatingSystem.IsMacOS() ? Keychain
        : SystemdCreds;

    // There is deliberately no plaintext protector: refusing to start beats storing secrets in the clear.
    public static IKeyProtector Create(KvxOptions options)
    {
        var name = Name(options);
        if (name == SystemdCreds && OperatingSystem.IsLinux()) return new SystemdCredsKeyProtector(options.KeyCredentialName);
        if (name == Dpapi && OperatingSystem.IsWindows()) return new DpapiKeyProtector(options.KeyCredentialName);
        if (name == Keychain && OperatingSystem.IsMacOS()) return new KeychainKeyProtector(options.KeyCredentialName);
        if (name is SystemdCreds or Dpapi or Keychain)
            throw new KeyProtectorException($"The key protector '{name}' does not exist on {RuntimeInformation.OSDescription}.");
        throw new KeyProtectorException($"Unknown key protector '{name}'.");
    }
}
