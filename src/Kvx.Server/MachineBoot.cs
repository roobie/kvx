using System.Runtime.InteropServices;

namespace Kvx.Server;

/// <summary>
/// When the machine last booted, from the OS's own record. Not now minus Environment.TickCount64: on Linux and
/// macOS that clock stops while the machine sleeps, so every laptop lid-close would look like a reboot.
/// </summary>
public static class MachineBoot
{
    /// <summary>UTC; null when the OS doesn't say.</summary>
    public static DateTime? Time()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);   // counts through sleep
            if (OperatingSystem.IsMacOS()) return MacBootTime();
            if (OperatingSystem.IsLinux())
                foreach (var line in File.ReadLines("/proc/stat"))
                    if (line.StartsWith("btime ") && long.TryParse(line.AsSpan(6), out var seconds))
                        return DateTime.UnixEpoch.AddSeconds(seconds);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeVal { public long Seconds; public int Microseconds; }

    [DllImport("libc", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out TimeVal value, ref nint size, nint newValue, nint newSize);

    private static DateTime? MacBootTime()
    {
        nint size = Marshal.SizeOf<TimeVal>();
        return SysctlByName("kern.boottime", out var tv, ref size, 0, 0) == 0
            ? DateTime.UnixEpoch.AddSeconds(tv.Seconds).AddTicks(tv.Microseconds * 10L)
            : null;
    }
}
