using System.Runtime.InteropServices;
using System.Text;

namespace Kvx.Server;

/// <summary>macOS per-user directories that only confstr names.</summary>
public static class MacUserDirs
{
    private const int CsDarwinUserCacheDir = 65538;

    [DllImport("libc", EntryPoint = "confstr")]
    private static extern nuint ConfStr(int name, byte[]? buffer, nuint length);

    /// <summary>
    /// The user's /var/folders/…/C/: private to the user like the temp dir, but not purged after days without access.
    /// Null when confstr fails.
    /// </summary>
    public static string? Cache()
    {
        var length = ConfStr(CsDarwinUserCacheDir, null, 0);
        if (length == 0) return null;
        var buffer = new byte[length];
        if (ConfStr(CsDarwinUserCacheDir, buffer, length) == 0) return null;
        return Encoding.UTF8.GetString(buffer, 0, (int)length - 1);
    }
}
