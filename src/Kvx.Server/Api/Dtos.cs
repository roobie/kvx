namespace Kvx.Server.Api;

// Wire shapes of the Key Vault secrets API. Nulls are omitted on write: the SDK throws on a JSON null
// for most fields, but tolerates their absence.

public sealed record SetSecretRequest(
    string? Value, string? ContentType, Dictionary<string, string>? Tags, AttributesRequest? Attributes);

public sealed record UpdateSecretRequest(
    string? ContentType, Dictionary<string, string>? Tags, AttributesRequest? Attributes);

public sealed record AttributesRequest(bool? Enabled, long? Nbf, long? Exp);

public sealed record BackupBody(string Value);

public sealed record SecretAttributes(
    bool Enabled, long? Nbf, long? Exp, long Created, long Updated, string RecoveryLevel, int RecoverableDays);

/// <summary>A bundle (with value) or, with <c>Value</c> null, the properties-only answers to update and recover.</summary>
public sealed record SecretBundle(
    string? Value, string Id, string? ContentType, Dictionary<string, string>? Tags, SecretAttributes Attributes);

public sealed record SecretItem(
    string Id, string? ContentType, Dictionary<string, string>? Tags, SecretAttributes Attributes);

public sealed record DeletedSecretBody(
    string Id, string? ContentType, Dictionary<string, string>? Tags, SecretAttributes Attributes,
    string RecoveryId, long DeletedDate, long ScheduledPurgeDate);

public sealed record PageBody<T>(IReadOnlyList<T> Value, string? NextLink);
