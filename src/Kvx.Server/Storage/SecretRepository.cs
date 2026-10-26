using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Storage;

public enum StoreProblem { NotFound, DeletedButRecoverable, AlreadyExists, InvalidBackup }

public sealed class StoreException(StoreProblem problem, string message) : Exception(message)
{
    public StoreProblem Problem { get; } = problem;
}

public sealed record SecretSummary(string Name, SecretVersion Latest);
public sealed record DeletedSecret(string Name, SecretVersion Latest, long DeletedDate, long ScheduledPurgeDate);

/// <summary>The store lives in memory; every mutation reseals the whole file before returning.</summary>
public sealed class SecretRepository
{
    private readonly SealedStore _sealed;
    private readonly TimeProvider _time;
    private readonly int _recoverableDays;
    private readonly StoreDocument _doc;
    private readonly Lock _lock = new();

    public SecretRepository(SealedStore sealedStore, TimeProvider time, IOptions<KvxOptions> options)
    {
        _sealed = sealedStore;
        _time = time;
        _recoverableDays = options.Value.RecoverableDays;
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

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    public (string Name, SecretVersion Version) Set(
        string vault, string name, string value, string? contentType,
        Dictionary<string, string>? tags, bool enabled, long? notBefore, long? expires)
    {
        lock (_lock)
        {
            PurgeExpired();
            var v = Vault(vault, create: true)!;
            if (v.Deleted.ContainsKey(name)) throw DeletedButRecoverable(name);
            if (!v.Secrets.TryGetValue(name, out var secret)) v.Secrets[name] = secret = new SecretDocument { Name = name };

            var now = Now;
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
            return (secret.Name, version);
        }
    }

    /// <summary>The newest version when <paramref name="version"/> is empty.</summary>
    public (string Name, SecretVersion Version) Get(string vault, string name, string? version)
    {
        lock (_lock)
        {
            var secret = Live(vault, name);
            var found = string.IsNullOrEmpty(version) ? secret.Versions.LastOrDefault() : Find(secret, version);
            return found is null ? throw NotFound(name, version) : (secret.Name, found);
        }
    }

    public IReadOnlyList<SecretSummary> List(string vault)
    {
        lock (_lock)
        {
            return Vault(vault)?.Secrets.Values.Where(s => s.Versions.Count > 0)
                .Select(s => new SecretSummary(s.Name, s.Versions[^1])).ToArray() ?? [];
        }
    }

    public (string Name, IReadOnlyList<SecretVersion> Versions) Versions(string vault, string name)
    {
        lock (_lock)
        {
            var secret = Live(vault, name);
            return (secret.Name, secret.Versions.ToArray());
        }
    }

    /// <summary>Null arguments leave the property as it is.</summary>
    public (string Name, SecretVersion Version) Update(
        string vault, string name, string version, string? contentType,
        Dictionary<string, string>? tags, bool? enabled, long? notBefore, long? expires)
    {
        lock (_lock)
        {
            var secret = Live(vault, name);
            var index = secret.Versions.FindIndex(x => SameVersion(x, version));
            if (index < 0) throw NotFound(name, version);

            var old = secret.Versions[index];
            var updated = old with
            {
                ContentType = contentType ?? old.ContentType,
                Tags = tags ?? old.Tags,
                Enabled = enabled ?? old.Enabled,
                NotBefore = notBefore ?? old.NotBefore,
                Expires = expires ?? old.Expires,
                Updated = Now,
            };
            secret.Versions[index] = updated;
            Persist();
            return (secret.Name, updated);
        }
    }

    public DeletedSecret Delete(string vault, string name)
    {
        lock (_lock)
        {
            var v = Vault(vault)!;
            var secret = Live(vault, name);
            var now = Now;
            var deleted = new DeletedSecretDocument(secret, now, now + _recoverableDays * 86400L);
            v.Secrets.Remove(name);
            v.Deleted[name] = deleted;
            Persist();
            return Describe(deleted);
        }
    }

    public DeletedSecret GetDeleted(string vault, string name)
    {
        lock (_lock) return Describe(Deleted(vault, name));
    }

    public IReadOnlyList<DeletedSecret> ListDeleted(string vault)
    {
        lock (_lock)
        {
            PurgeExpired();
            return Vault(vault)?.Deleted.Values.Select(Describe).ToArray() ?? [];
        }
    }

    public (string Name, SecretVersion Version) Recover(string vault, string name)
    {
        lock (_lock)
        {
            var deleted = Deleted(vault, name);
            var v = Vault(vault)!;
            v.Deleted.Remove(name);
            v.Secrets[name] = deleted.Secret;
            Persist();
            return (deleted.Secret.Name, deleted.Secret.Versions[^1]);
        }
    }

    public void Purge(string vault, string name)
    {
        lock (_lock)
        {
            Deleted(vault, name);
            Vault(vault)!.Deleted.Remove(name);
            Persist();
        }
    }

    /// <summary>All versions of a live secret, sealed so the blob can leave kvx.</summary>
    public byte[] Backup(string vault, string name)
    {
        lock (_lock)
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(Live(vault, name));
            try { return _sealed.SealBackup(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }

    public (string Name, SecretVersion Version) Restore(string vault, byte[] backup)
    {
        SecretDocument secret;
        byte[] plaintext;
        try { plaintext = _sealed.UnsealBackup(backup); }
        catch (Exception e) when (e is CryptographicException or InvalidDataException)
        {
            throw new StoreException(StoreProblem.InvalidBackup, "The backup blob is not a kvx secret backup made with this machine's key.");
        }
        try { secret = JsonSerializer.Deserialize<SecretDocument>(plaintext)!; }
        finally { CryptographicOperations.ZeroMemory(plaintext); }

        lock (_lock)
        {
            PurgeExpired();
            var v = Vault(vault, create: true)!;
            if (v.Deleted.ContainsKey(secret.Name)) throw DeletedButRecoverable(secret.Name);
            if (v.Secrets.ContainsKey(secret.Name))
                throw new StoreException(StoreProblem.AlreadyExists, $"Secret {secret.Name} already exists.");
            v.Secrets[secret.Name] = secret;
            Persist();
            return (secret.Name, secret.Versions[^1]);
        }
    }

    public ServicePrincipal? Principal
    {
        get { lock (_lock) return _doc.Principal; }
    }

    public ServicePrincipal SetPrincipal(string tenantId, string clientId, string secret)
    {
        lock (_lock)
        {
            _doc.Principal = new ServicePrincipal(tenantId, clientId, secret, Now);
            Persist();
            return _doc.Principal;
        }
    }

    public bool ClearPrincipal()
    {
        lock (_lock)
        {
            if (_doc.Principal is null) return false;
            _doc.Principal = null;
            Persist();
            return true;
        }
    }

    // --- helpers; callers hold the lock ---

    private VaultDocument? Vault(string vault, bool create = false)
    {
        if (!_doc.Vaults.TryGetValue(vault, out var v) && create) _doc.Vaults[vault] = v = new VaultDocument();
        return v;
    }

    private SecretDocument Live(string vault, string name) =>
        Vault(vault)?.Secrets.GetValueOrDefault(name) ?? throw NotFound(name, null);

    private DeletedSecretDocument Deleted(string vault, string name)
    {
        PurgeExpired();
        return Vault(vault)?.Deleted.GetValueOrDefault(name)
            ?? throw new StoreException(StoreProblem.NotFound, $"Deleted secret {name} was not found in this key vault.");
    }

    /// <summary>Retention is enforced lazily: an expired deleted secret disappears the next time deleted state is read.</summary>
    private void PurgeExpired()
    {
        var now = Now;
        var purged = false;
        foreach (var v in _doc.Vaults.Values)
            foreach (var (name, deleted) in v.Deleted.ToArray())
                if (deleted.ScheduledPurgeDate <= now) purged |= v.Deleted.Remove(name);
        if (purged) Persist();
    }

    private static DeletedSecret Describe(DeletedSecretDocument d) =>
        new(d.Secret.Name, d.Secret.Versions[^1], d.DeletedDate, d.ScheduledPurgeDate);

    private static SecretVersion? Find(SecretDocument secret, string version) =>
        secret.Versions.FirstOrDefault(x => SameVersion(x, version));

    private static bool SameVersion(SecretVersion v, string version) =>
        string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase);

    private static StoreException NotFound(string name, string? version) =>
        new(StoreProblem.NotFound,
            $"A secret with (name/id) {name}{(string.IsNullOrEmpty(version) ? "" : "/" + version)} was not found in this key vault.");

    private static StoreException DeletedButRecoverable(string name) =>
        new(StoreProblem.DeletedButRecoverable,
            $"Secret {name} is currently in a deleted but recoverable state, and its name cannot be reused; in this state, the secret can only be recovered or purged.");

    private void Persist()
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(_doc);
        try { _sealed.Save(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
