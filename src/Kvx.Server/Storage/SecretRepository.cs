using System.Security.Cryptography;
using System.Text.Json;

namespace Kvx.Server.Storage;

/// <summary>The store lives in memory; every mutation reseals the whole file before returning.</summary>
public sealed class SecretRepository
{
    private readonly SealedStore _sealed;
    private readonly TimeProvider _time;
    private readonly StoreDocument _doc;
    private readonly Lock _lock = new();

    public SecretRepository(SealedStore sealedStore, TimeProvider time)
    {
        _sealed = sealedStore;
        _time = time;
        var plaintext = sealedStore.Load();
        try
        {
            _doc = plaintext is null ? new StoreDocument() : JsonSerializer.Deserialize<StoreDocument>(plaintext)!;
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public (SecretDocument Secret, SecretVersion Version) Set(
        string vault, string name, string value, string? contentType,
        Dictionary<string, string>? tags, bool enabled, long? notBefore, long? expires)
    {
        lock (_lock)
        {
            if (!_doc.Vaults.TryGetValue(vault, out var v)) _doc.Vaults[vault] = v = new VaultDocument();
            if (!v.Secrets.TryGetValue(name, out var secret)) v.Secrets[name] = secret = new SecretDocument { Name = name };

            var now = _time.GetUtcNow().ToUnixTimeSeconds();
            var version = new SecretVersion
            {
                Version = Guid.NewGuid().ToString("N"),
                Value = value,
                ContentType = contentType,
                Tags = tags,
                Enabled = enabled,
                NotBefore = notBefore,
                Expires = expires,
                Created = now,
                Updated = now,
            };
            secret.Versions.Add(version);
            Persist();
            return (secret, version);
        }
    }

    /// <summary>The newest version when <paramref name="version"/> is empty.</summary>
    public (SecretDocument Secret, SecretVersion Version)? Get(string vault, string name, string? version)
    {
        lock (_lock)
        {
            if (!_doc.Vaults.TryGetValue(vault, out var v) || !v.Secrets.TryGetValue(name, out var secret))
                return null;
            var found = string.IsNullOrEmpty(version)
                ? secret.Versions.LastOrDefault()
                : secret.Versions.FirstOrDefault(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));
            return found is null ? null : (secret, found);
        }
    }

    private void Persist()
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(_doc);
        try { _sealed.Save(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
