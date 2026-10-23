using System.Security.Cryptography;
using Kvx.Server.Keys;

namespace Kvx.Server.Storage;

/// <summary>Another kvx serves from this data dir; a second writer would lose updates.</summary>
public sealed class StoreInUseException(string message) : Exception(message);

/// <summary>
/// One AES-GCM sealed file holding the whole store, so names and metadata are as protected as values.
/// The previous generation is kept so a torn write never loses everything.
/// </summary>
public sealed class SealedStore : IDisposable
{
    // The magic is also the AAD, so a blob sealed for one purpose cannot be passed off as another.
    private static readonly byte[] StoreMagic = "KVX1"u8.ToArray();
    private static readonly byte[] BackupMagic = "KVB1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    private readonly string _store, _previous, _temp;
    private readonly byte[] _key;
    private readonly ILogger _log;
    private readonly FileStream _lock;

    private SealedStore(string dir, byte[] key, ILogger log, FileStream heldLock)
    {
        _store = Path.Combine(dir, "store.kvx");
        _previous = Path.Combine(dir, "store.kvx.prev");
        _temp = Path.Combine(dir, "store.kvx.tmp");
        _key = key;
        _log = log;
        _lock = heldLock;
    }

    public static SealedStore Open(string dir, IKeyProtector protector, ILogger log)
    {
        PrivateFiles.CreateDirectory(dir);
        var heldLock = Lock(dir);
        try
        {
            var keyFile = Path.Combine(dir, "data-key.cred");
            byte[] key;
            if (File.Exists(keyFile))
            {
                key = protector.Unwrap(File.ReadAllBytes(keyFile));
            }
            else
            {
                if (File.Exists(Path.Combine(dir, "store.kvx")))
                    throw new InvalidOperationException($"{dir} has a store but no data key; refusing to start.");
                key = RandomNumberGenerator.GetBytes(32);
                PrivateFiles.Write(keyFile + ".tmp", protector.Wrap(key));
                File.Move(keyFile + ".tmp", keyFile);
            }
            return new SealedStore(dir, key, log, heldLock);
        }
        catch
        {
            heldLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Held while serving. The OS drops it when the process dies, so a crash never leaves the store locked.
    /// The holder's process id sits in a separate file because the lock file itself cannot be read while held.
    /// </summary>
    private static FileStream Lock(string dir)
    {
        var owner = Path.Combine(dir, "store.owner");
        try
        {
            var held = new FileStream(Path.Combine(dir, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            PrivateFiles.Write(owner, System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString()));
            return held;
        }
        catch (IOException)
        {
            var pid = File.Exists(owner) ? File.ReadAllText(owner).Trim() : "unknown";
            throw new StoreInUseException($"the store in {dir} is in use by another kvx (process {pid}). " +
                                          "Use that kvx, or stop it before starting this one.");
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>The plaintext, or null when no store exists yet.</summary>
    public byte[]? Load()
    {
        if (!File.Exists(_store)) return null;
        try
        {
            return Unseal(File.ReadAllBytes(_store), StoreMagic);
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException && File.Exists(_previous))
        {
            _log.LogError(e, "Store {Path} is unreadable; falling back to the previous generation", _store);
            return Unseal(File.ReadAllBytes(_previous), StoreMagic);
        }
    }

    public void Save(ReadOnlySpan<byte> plaintext)
    {
        PrivateFiles.Write(_temp, Seal(plaintext, StoreMagic));
        if (File.Exists(_store)) File.Replace(_temp, _store, _previous);
        else File.Move(_temp, _store);
    }

    /// <summary>Backups leave kvx, so they are sealed with the same key and restore only where that key unwraps.</summary>
    public byte[] SealBackup(ReadOnlySpan<byte> plaintext) => Seal(plaintext, BackupMagic);

    /// <exception cref="InvalidDataException">The blob is not a kvx backup.</exception>
    /// <exception cref="CryptographicException">The blob was sealed by another key or tampered with.</exception>
    public byte[] UnsealBackup(byte[] sealedBytes) => Unseal(sealedBytes, BackupMagic);

    private byte[] Seal(ReadOnlySpan<byte> plaintext, byte[] magic)
    {
        var sealedBytes = new byte[magic.Length + NonceSize + TagSize + plaintext.Length];
        var span = sealedBytes.AsSpan();
        magic.CopyTo(span);
        var nonce = span.Slice(magic.Length, NonceSize);
        var tag = span.Slice(magic.Length + NonceSize, TagSize);
        var cipher = span[(magic.Length + NonceSize + TagSize)..];
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag, magic);
        return sealedBytes;
    }

    private byte[] Unseal(byte[] sealedBytes, byte[] magic)
    {
        var span = sealedBytes.AsSpan();
        if (span.Length < magic.Length + NonceSize + TagSize || !span[..magic.Length].SequenceEqual(magic))
            throw new InvalidDataException("Not a sealed kvx blob of the expected kind.");
        var nonce = span.Slice(magic.Length, NonceSize);
        var tag = span.Slice(magic.Length + NonceSize, TagSize);
        var cipher = span[(magic.Length + NonceSize + TagSize)..];
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext, magic);
        return plaintext;
    }
}
