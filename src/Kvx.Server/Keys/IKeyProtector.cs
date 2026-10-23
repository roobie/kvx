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
    // There is deliberately no plaintext protector: refusing to start beats storing secrets in the clear.
    public static IKeyProtector Create(KvxOptions options) => options.KeyProtector switch
    {
        "systemd-creds" => new SystemdCredsKeyProtector(options.KeyCredentialName),
        var other => throw new KeyProtectorException($"Unknown key protector '{other}'."),
    };
}
