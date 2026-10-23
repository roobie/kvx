using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

// An app exactly as a developer writes it against real Key Vault; only its environment points it at kvx.
// Usage: Kvx.SampleApp <vault-uri> <secret-name> <secret-value>
var client = new SecretClient(new Uri(args[0]), new DefaultAzureCredential());

var set = (await client.SetSecretAsync(args[1], args[2])).Value;
var latest = (await client.GetSecretAsync(args[1])).Value;
var byVersion = (await client.GetSecretAsync(args[1], set.Properties.Version)).Value;

Console.WriteLine(JsonSerializer.Serialize(new { id = set.Id, latest = latest.Value, byVersion = byVersion.Value }));
