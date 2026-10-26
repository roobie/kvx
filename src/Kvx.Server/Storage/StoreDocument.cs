using System.Text.Json.Serialization;

namespace Kvx.Server.Storage;

// The plaintext shape inside the sealed file. Key Vault names are case-insensitive,
// so dictionaries are populated in place to keep their comparers after deserialization.

public sealed class StoreDocument
{
    public int Format { get; set; } = 1;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, VaultDocument> Vaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Passthrough's service principals; outside <see cref="Vaults"/> so the vault API never serves them.</summary>
    public List<ServicePrincipal> Principals { get; set; } = [];

    /// <summary>The one principal a store held before there could be several; moved into <see cref="Principals"/> on load.</summary>
    public ServicePrincipal? Principal { get; set; }
}

/// <summary>
/// An app asking for <see cref="ClientId"/> or one of <see cref="Aliases"/> gets this principal; <see cref="Default"/>
/// marks the one for apps that name no identity, when Kvx:Passthrough is ServicePrincipal.
/// </summary>
public sealed record ServicePrincipal(string TenantId, string ClientId, string Secret, long Updated)
{
    public string[] Aliases { get; init; } = [];
    public bool Default { get; init; }

    public bool Answers(string id) =>
        ClientId.Equals(id, StringComparison.OrdinalIgnoreCase) || Aliases.Contains(id, StringComparer.OrdinalIgnoreCase);
}

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
