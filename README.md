# kvx

A local, encrypted-at-rest stand-in for Azure Key Vault secrets, so development secrets stop living in plaintext.

> [!WARNING]
> **An app pointed at kvx through `DefaultAzureCredential` cannot reach any other Azure service.**
>
> `DefaultAzureCredential` keeps using the first credential that works. Once the app has a token from kvx, every Azure client in that process (Storage, Service Bus, Cosmos DB, ARM, …) asks kvx for its token too. kvx refuses those requests, so the calls fail with `AuthenticationFailedException` naming kvx.
>
> Use kvx this way only for apps whose sole Azure dependency is Key Vault secrets. If your app also calls other Azure services, do not set the variables below. Until kvx supports that combination, those apps need a code change to give Key Vault and the other services separate credentials.

## Using it from an unmodified app

kvx serves the managed-identity protocol, so `new DefaultAzureCredential()` works with no code change once the app's environment has:

```
IDENTITY_ENDPOINT=http://127.0.0.1:5380/kvx/identity/token
IDENTITY_HEADER=<contents of $XDG_RUNTIME_DIR/kvx/dev-token>
```

`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`/`AZURE_TENANT_ID` and a workload-identity setup come earlier in the chain and take priority over kvx, so unset them for apps that should use kvx.
