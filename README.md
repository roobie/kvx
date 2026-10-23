# kvx

A local, encrypted-at-rest stand-in for Azure Key Vault secrets, so development secrets stop living in plaintext.

> [!WARNING]
> **An app pointed at kvx through `DefaultAzureCredential` gets *all* its Azure tokens through kvx — and reaches every other Azure service as *you*, only if you are signed in.**
>
> `DefaultAzureCredential` keeps using the first credential that works. Once the app has a token from kvx, every Azure client in that process (Storage, Service Bus, Cosmos DB, ARM, …) asks kvx for its token too. kvx answers Key Vault itself and passes every other request through to your own login:
>
> - **You must be signed in** with `az login`, `azd auth login`, Visual Studio or `Connect-AzAccount`, in the session kvx runs in. Otherwise those calls fail with `AuthenticationFailedException`, and the message says to sign in.
> - **The app acts as you, not as its managed identity.** It sees your role assignments, your default tenant and your subscriptions, so access that works locally may still fail in production, and the reverse.
> - **Anything holding the session token can get your Azure tokens from kvx.** That is the same reach as running `az account get-access-token` yourself, since the token file is readable only by you.
>
> Set `Kvx:Passthrough` to an empty string to turn passthrough off. Apps then reach only Key Vault through kvx.

## Using it from an unmodified app

kvx serves the managed-identity protocol, so `new DefaultAzureCredential()` works with no code change once the app's environment has:

```
IDENTITY_ENDPOINT=http://127.0.0.1:5380/kvx/identity/token
IDENTITY_HEADER=<contents of $XDG_RUNTIME_DIR/kvx/dev-token>
```

`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`/`AZURE_TENANT_ID` and a workload-identity setup come earlier in the chain and take priority over kvx, so unset them for apps that should use kvx.
