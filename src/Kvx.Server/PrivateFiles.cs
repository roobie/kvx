namespace Kvx.Server;

/// <summary>Files and directories readable only by the current user.</summary>
public static class PrivateFiles
{
    private const UnixFileMode DirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // On Windows the per-user profile ACLs already restrict these locations.
    public static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, DirMode);
    }

    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        var options = new FileStreamOptions { Mode = System.IO.FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = FileMode;
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
