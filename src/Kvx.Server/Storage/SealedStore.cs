using System.Security.Cryptography;
using Kvx.Server.Keys;

namespace Kvx.Server.Storage;

/// <summary>
/// One AES-GCM sealed file holding the whole store, so names and metadata are as protected as values.
/// The previous generation is kept so a torn write never loses everything.
/// </summary>
public sealed class SealedStore
{
    private static readonly byte[] Magic = "KVX1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    private readonly string _store, _previous, _temp;
    private readonly byte[] _key;
    private readonly ILogger _log;

    private SealedStore(string dir, byte[] key, ILogger log)
    {
        _store = Path.Combine(dir, "store.kvx");
        _previous = Path.Combine(dir, "store.kvx.prev");
        _temp = Path.Combine(dir, "store.kvx.tmp");
        _key = key;
        _log = log;
    }

    public static SealedStore Open(string dir, IKeyProtector protector, ILogger log)
    {
        PrivateFiles.CreateDirectory(dir);
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
        return new SealedStore(dir, key, log);
    }

    /// <summary>The plaintext, or null when no store exists yet.</summary>
    public byte[]? Load()
    {
        if (!File.Exists(_store)) return null;
        try
        {
            return Unseal(File.ReadAllBytes(_store));
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException && File.Exists(_previous))
        {
            _log.LogError(e, "Store {Path} is unreadable; falling back to the previous generation", _store);
            return Unseal(File.ReadAllBytes(_previous));
        }
    }

    public void Save(ReadOnlySpan<byte> plaintext)
    {
        PrivateFiles.Write(_temp, Seal(plaintext));
        if (File.Exists(_store)) File.Replace(_temp, _store, _previous);
        else File.Move(_temp, _store);
    }

    private byte[] Seal(ReadOnlySpan<byte> plaintext)
    {
        var sealedBytes = new byte[Magic.Length + NonceSize + TagSize + plaintext.Length];
        var span = sealedBytes.AsSpan();
        Magic.CopyTo(span);
        var nonce = span.Slice(Magic.Length, NonceSize);
        var tag = span.Slice(Magic.Length + NonceSize, TagSize);
        var cipher = span[(Magic.Length + NonceSize + TagSize)..];
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag, Magic);
        return sealedBytes;
    }

    private byte[] Unseal(byte[] sealedBytes)
    {
        var span = sealedBytes.AsSpan();
        if (span.Length < Magic.Length + NonceSize + TagSize || !span[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Not a kvx store file.");
        var nonce = span.Slice(Magic.Length, NonceSize);
        var tag = span.Slice(Magic.Length + NonceSize, TagSize);
        var cipher = span[(Magic.Length + NonceSize + TagSize)..];
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext, Magic);
        return plaintext;
    }
}
