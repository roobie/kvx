using System.Text.Json.Serialization;

namespace Kvx.Server.Storage;

// The plaintext shape inside the sealed file. Key Vault names are case-insensitive,
// so dictionaries are populated in place to keep their comparers after deserialization.

public sealed class StoreDocument
{
    public int Format { get; set; } = 1;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, VaultDocument> Vaults { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VaultDocument
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, SecretDocument> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecretDocument
{
    public required string Name { get; set; }
    public List<SecretVersion> Versions { get; set; } = [];
}

public sealed class SecretVersion
{
    public required string Version { get; set; }
    public required string Value { get; set; }
    public string? ContentType { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public bool Enabled { get; set; } = true;
    public long? NotBefore { get; set; }
    public long? Expires { get; set; }
    public long Created { get; set; }
    public long Updated { get; set; }
}
