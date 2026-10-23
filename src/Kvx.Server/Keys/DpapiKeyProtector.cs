using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Kvx.Server.Keys;

/// <summary>
/// Windows DPAPI for the current user: no prompt, keyed to the user's logon secret. The credential name is the
/// entropy, so a key wrapped under one name does not unwrap under another, as with systemd-creds.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector(string credentialName) : IKeyProtector
{
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes(credentialName);

    public byte[] Wrap(byte[] key) => Run("protect", () => ProtectedData.Protect(key, _entropy, DataProtectionScope.CurrentUser));
    public byte[] Unwrap(byte[] wrapped) => Run("unprotect", () => ProtectedData.Unprotect(wrapped, _entropy, DataProtectionScope.CurrentUser));

    private static byte[] Run(string verb, Func<byte[]> dpapi)
    {
        try
        {
            return dpapi();
        }
        catch (CryptographicException e)
        {
            throw new KeyProtectorException($"DPAPI could not {verb} the data key: {e.Message}", e);
        }
    }
}
