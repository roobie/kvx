using System.Text.Json.Serialization;

namespace Kvx.Server.Storage;

// The plaintext shape inside the sealed file. Key Vault names are case-insensitive,
// so dictionaries are populated in place to keep their comparers after deserialization.

public sealed class StoreDocument
{
    public int Format { get; set; } = 1;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, VaultDocument> Vaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Passthrough's service principal; outside <see cref="Vaults"/> so the vault API never serves it.</summary>
    public ServicePrincipal? Principal { get; set; }
}

public sealed record ServicePrincipal(string TenantId, string ClientId, string Secret, long Updated);

public sealed class VaultDocument
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, SecretDocument> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Kept apart from live secrets: a name is in exactly one of the two, and live lookups stay oblivious.
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, DeletedSecretDocument> Deleted { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecretDocument
{
    public required string Name { get; set; }
    public List<SecretVersion> Versions { get; set; } = [];
}

public sealed record DeletedSecretDocument(SecretDocument Secret, long DeletedDate, long ScheduledPurgeDate);

/// <summary>Immutable, so readers can hold one outside the repository lock; updates replace it.</summary>
public sealed record SecretVersion
{
    public required string Version { get; init; }
    public required string Value { get; init; }
    public string? ContentType { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public bool Enabled { get; init; } = true;
    public long? NotBefore { get; init; }
    public long? Expires { get; init; }
    public long Created { get; init; }
    public long Updated { get; init; }
}
