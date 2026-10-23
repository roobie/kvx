using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

// An app exactly as a developer writes it against real Azure; only its environment points it at kvx.
// Usage: Kvx.SampleApp <vault-uri> <secret-name> <secret-value>
//        Kvx.SampleApp config <vault-uri> <configuration-key>…
var credential = new DefaultAzureCredential();

if (args[0] == "config")
{
    // The usual ASP.NET wiring for Key Vault-backed configuration.
    var config = new ConfigurationBuilder().AddAzureKeyVault(new Uri(args[1]), credential).Build();
    Console.WriteLine(JsonSerializer.Serialize(args[2..].ToDictionary(k => k, k => config[k])));
    return;
}

var client = new SecretClient(new Uri(args[0]), credential);

var set = (await client.SetSecretAsync(args[1], args[2])).Value;
var latest = (await client.GetSecretAsync(args[1])).Value;
var byVersion = (await client.GetSecretAsync(args[1], set.Properties.Version)).Value;

// What a Storage client would do with the same credential after Key Vault has been used.
var storage = await credential.GetTokenAsync(new TokenRequestContext(["https://storage.azure.com/.default"]));

Console.WriteLine(JsonSerializer.Serialize(new
{
    id = set.Id, latest = latest.Value, byVersion = byVersion.Value, storageToken = storage.Token,
}));
