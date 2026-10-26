# Use kvx from an Aspire AppHost

```sh
dotnet pack src/Kvx.Aspire.Hosting -o ./artifacts
dotnet add path/to/AppHost package Kvx.Aspire.Hosting --source ./artifacts
```

```csharp
var secrets = builder.AddAzureKeyVault("secrets").RunAsKvx();
builder.AddProject<Projects.MyApp>("myapp").WithReference(secrets);
```

`RunAsKvx` changes only run mode. The vault stays Aspire's own Azure Key Vault resource, so the app keeps Aspire's Key Vault client integration unchanged, and publishing still provisions a real Key Vault. The local run needs no Azure subscription.

In run mode the AppHost shows kvx as a resource with its logs. If your kvx is already serving, the AppHost uses it, so secrets you imported are there; otherwise it starts kvx and stops it again with the AppHost. Every resource that references the vault waits for kvx, gets it as its managed identity the way `kvx run` sets that up, and trusts its certificate. What those resources get for other Azure services is the same as for any app run through kvx: [passthrough.md](passthrough.md), or `kvx help passthrough`.

The AppHost's `Kvx` configuration section sets `Command` (default `kvx`) and `Settings`, a list of settings passed to every kvx command, for example `["--Kvx:Domain=…"]`. They must match the kvx you run yourself if the AppHost is to reuse it. [samples/Kvx.Aspire.Sample](../samples/Kvx.Aspire.Sample) is a working AppHost.
