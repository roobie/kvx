using System.Text.Json;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

// An app as a developer writes it against real Key Vault: a vault URI and a credential, nothing else.
// Usage: Kvx.SampleApp <vault-uri> <token-file> <secret-name> <secret-value>
var client = new SecretClient(new Uri(args[0]), new TokenFileCredential(args[1]));

var set = (await client.SetSecretAsync(args[2], args[3])).Value;
var latest = (await client.GetSecretAsync(args[2])).Value;
var byVersion = (await client.GetSecretAsync(args[2], set.Properties.Version)).Value;

Console.WriteLine(JsonSerializer.Serialize(new { id = set.Id, latest = latest.Value, byVersion = byVersion.Value }));

sealed class TokenFileCredential(string path) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
        new(File.ReadAllText(path).Trim(), DateTimeOffset.UtcNow.AddMinutes(5));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        new(GetToken(context, ct));
}
