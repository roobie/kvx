using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Auth;

/// <summary>
/// One random bearer token per process. It is written to a per-user runtime file so local
/// tooling can pick it up, and it dies with the process.
/// </summary>
public sealed class DevTokens
{
    private readonly string _file;

    public string Current { get; }

    public DevTokens(IOptions<KvxOptions> options, IHostApplicationLifetime lifetime)
    {
        Current = Base64Url(RandomNumberGenerator.GetBytes(32));
        _file = options.Value.ResolveTokenFile();

        PrivateFiles.CreateDirectory(Path.GetDirectoryName(_file)!);
        PrivateFiles.Write(_file, Encoding.ASCII.GetBytes(Current));

        lifetime.ApplicationStopping.Register(RemoveFileIfOurs);
    }

    public bool IsValid(ReadOnlySpan<char> presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(presented.ToString()), Encoding.ASCII.GetBytes(Current));

    private void RemoveFileIfOurs()
    {
        try
        {
            if (File.Exists(_file) && File.ReadAllText(_file) == Current) File.Delete(_file);
        }
        catch (IOException) { /* best effort; the token is useless once we stop anyway */ }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
