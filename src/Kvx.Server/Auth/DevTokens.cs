using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

/// <summary>
/// One random bearer token per login session. It lives in a per-user runtime file (cleared at logout
/// and reboot) and is reused when kvx restarts, because apps cache it in memory for as long as they run.
/// The same value is the IDENTITY_HEADER apps present to the identity endpoint.
/// </summary>
public sealed class DevTokens
{
    public string Current { get; }

    public DevTokens(IOptions<KvxOptions> options)
    {
        var file = options.Value.ResolveTokenFile();
        Current = Read(file) ?? Create(file);
    }

    public bool IsValid(ReadOnlySpan<char> presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(presented.ToString()), Encoding.ASCII.GetBytes(Current));

    /// <summary>The session token in <paramref name="file"/>, or null when there is none we would accept.</summary>
    public static string? Read(string file)
    {
        if (!File.Exists(file)) return null;
        var token = File.ReadAllText(file).Trim();
        // Anything else is not ours to trust (truncated write, hand edit); replace it.
        return token.Length == 43 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? token : null;
    }

    private static string Create(string file)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        PrivateFiles.CreateDirectory(Path.GetDirectoryName(file)!);
        PrivateFiles.Write(file + ".tmp", Encoding.ASCII.GetBytes(token));
        File.Move(file + ".tmp", file, overwrite: true);
        return token;
    }
}
