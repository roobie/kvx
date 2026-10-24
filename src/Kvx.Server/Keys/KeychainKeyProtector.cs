using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Kvx.Server.Keys;

/// <summary>
/// macOS: a random key-encryption key kept as a generic password in the user's login keychain wraps the data key
/// with AES-GCM, the credential name bound in as associated data.
/// The item is made and read only through Apple's `security` tool. A keychain item admits only the code partition
/// that created it; kvx's own binaries are ad-hoc signed, so their partition changes with every build and differs
/// between `dotnet` and the apphost. Through `security` every caller is in Apple's tool partition. The item also
/// trusts any app (`-A`), as DPAPI and `systemd-creds --user` do. The key goes to `security` on stdin, never on a
/// command line. A read that would need a prompt (a locked keychain) hangs, so every call has a timeout.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainKeyProtector : IKeyProtector
{
    private const byte Format = 1;
    private const int NonceSize = 12, TagSize = 16, KekSize = 32;
    private const string Service = "kvx", Tool = "/usr/bin/security";
    private const int ItemNotFound = 44;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _account;
    private readonly byte[] _aad;

    public KeychainKeyProtector(string credentialName)
    {
        // It goes into a `security -i` command line, which has its own quoting; keep it to plain names.
        if (credentialName.Length == 0 || !credentialName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new KeyProtectorException($"Kvx:KeyCredentialName '{credentialName}' must be letters, digits, '-', '_' or '.'.");
        _account = credentialName;
        _aad = Encoding.UTF8.GetBytes(credentialName);
    }

    private string Item => $"'{_account}' (service '{Service}') in the login keychain";

    public byte[] Wrap(byte[] key)
    {
        var kek = Read() ?? Create();
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var ciphertext = new byte[key.Length];
            using (var aes = new AesGcm(kek, TagSize))
                aes.Encrypt(nonce, key, ciphertext, tag, _aad);
            return [Format, .. nonce, .. tag, .. ciphertext];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public byte[] Unwrap(byte[] wrapped)
    {
        if (wrapped.Length < 1 + NonceSize + TagSize || wrapped[0] != Format)
            throw new KeyProtectorException("The wrapped data key was not made by the keychain protector.");
        var kek = Read() ?? throw new KeyProtectorException($"There is no {Item}, so the data key cannot be unwrapped.");
        try
        {
            var body = wrapped.AsSpan(1);
            var key = new byte[body.Length - NonceSize - TagSize];
            using var aes = new AesGcm(kek, TagSize);
            aes.Decrypt(body[..NonceSize], body[(NonceSize + TagSize)..], body.Slice(NonceSize, TagSize), key, _aad);
            return key;
        }
        catch (AuthenticationTagMismatchException e)
        {
            throw new KeyProtectorException($"The data key does not unwrap with the key in {Item}.", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private byte[]? Read()
    {
        // Without a login keychain `security` waits on a prompt to create one, which no one can see.
        var login = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Keychains", "login.keychain-db");
        if (!File.Exists(login))
            throw new KeyProtectorException($"There is no login keychain at {login}, so kvx has nowhere to keep its key.");
        var (exit, stdout, stderr) = Run(["find-generic-password", "-s", Service, "-a", _account, "-w"], stdin: null);
        if (exit == ItemNotFound) return null;
        if (exit != 0) throw Failure("read", stderr);
        try
        {
            var kek = Convert.FromHexString(stdout.Trim());
            if (kek.Length == KekSize) return kek;
        }
        catch (FormatException) { }
        throw new KeyProtectorException($"{Item} does not hold a kvx key.");
    }

    /// <summary>
    /// Adds a new key without replacing one that exists, then reads back what is stored: if another kvx created
    /// the item first, its key is the one in use, and replacing it would orphan what it already wrapped.
    /// </summary>
    private byte[] Create()
    {
        var kek = RandomNumberGenerator.GetBytes(KekSize);
        var hex = Convert.ToHexString(kek);
        CryptographicOperations.ZeroMemory(kek);
        var (_, _, stderr) = Run(["-i"],
            $"add-generic-password -s {Service} -a {_account} -l \"kvx key-encryption key ({_account})\" -A -w {hex}\n");
        return Read() ?? throw Failure("create", stderr);
    }

    private (int Exit, string Stdout, string Stderr) Run(string[] args, string? stdin)
    {
        var psi = new ProcessStartInfo(Tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new KeyProtectorException($"{Tool} did not start.");
        }
        catch (Win32Exception e)
        {
            throw new KeyProtectorException($"{Tool} could not be started: {e.Message}", e);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new KeyProtectorException(
                    $"The keychain did not answer within {Timeout.TotalSeconds:0} s about {Item}; it is most likely " +
                    "locked and waiting for a password prompt no one can see. Unlock it with `security unlock-keychain`.");
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }

    private KeyProtectorException Failure(string verb, string stderr)
    {
        var why = stderr.Trim();
        return new($"Could not {verb} {Item}: {(why.Length > 0 ? why : "no reason given")}." +
            (why.Contains("interaction is not allowed", StringComparison.OrdinalIgnoreCase)
                ? " The login keychain is locked (as over SSH); unlock it with `security unlock-keychain`."
                : ""));
    }
}
