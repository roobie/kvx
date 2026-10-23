using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// An app exactly as it is written for Azure: Aspire's Key Vault client with its defaults. Nothing here knows about kvx.
var builder = Host.CreateApplicationBuilder(args);
builder.AddAzureKeyVaultClient("secrets");
using var host = builder.Build();
var client = host.Services.GetRequiredService<SecretClient>();

await client.SetSecretAsync("sample-greeting", "hello from the vault");
Console.WriteLine($"sample-greeting = {(await client.GetSecretAsync("sample-greeting")).Value.Value}");

// Secrets put in the vault beforehand, for example with `kvx import --vault secrets`. Their values stay out of the logs.
await foreach (var secret in client.GetPropertiesOfSecretsAsync())
    if (secret.Name != "sample-greeting")
        Console.WriteLine($"{secret.Name}: {(await client.GetSecretAsync(secret.Name)).Value.Value.Length} characters");
