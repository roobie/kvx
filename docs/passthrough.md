# Choose how apps reach other Azure services

An app pointed at kvx through `DefaultAzureCredential` gets *all* its Azure tokens through kvx. `DefaultAzureCredential` keeps using the first credential that works, so once the app has a token from kvx, every Azure client in that process (Storage, Service Bus, Cosmos DB, ARM, …) asks kvx for its token too. kvx answers Key Vault itself. `Kvx:Passthrough` decides what every other request gets:

| `Kvx:Passthrough` | Apps reach other Azure services as |
|---|---|
| `AzureCli`, `AzureDeveloperCli`, `VisualStudio`, `AzurePowerShell` (default: all four) | you, through your own login |
| empty | nothing: apps reach only Key Vault |
| `ServicePrincipal` | a service principal kvx holds |

Whatever the mode, an app that names its identity with `AZURE_CLIENT_ID` gets the principal kvx holds for that id; see [each app as its own principal](#each-app-as-its-own-principal).

Switching `Kvx:Passthrough` takes a restart of kvx.

## Your own login (the default)

- **You must be signed in** with `az login`, `azd auth login`, Visual Studio or `Connect-AzAccount`, in the session kvx runs in. Otherwise those calls fail with `AuthenticationFailedException`, and the message says to sign in.
- **The app acts as you, not as its managed identity.** It sees your role assignments, your default tenant and your subscriptions, so access that works locally may still fail in production, and the reverse.
- **Anything holding the session token can get your Azure tokens from kvx.** That is the same reach as running `az account get-access-token` yourself, since the token file is readable only by you.

## Off

Set `Kvx:Passthrough` to an empty string. Apps then reach only Key Vault through kvx, and any other Azure client gets an error saying passthrough is off.

## Pass through as a service principal

With `Kvx:Passthrough` set to `ServicePrincipal`, kvx gets tokens for other Azure services by signing in as a service principal, not as you. The app then sees that principal's role assignments, which you can make match what its managed identity has in production. Key Vault still gets kvx's own token.

```sh
kvx --Kvx:Passthrough=ServicePrincipal          # or set it in appsettings.json
kvx principal set --tenant <tenant id> --client-id <application id> --default   # asks for the client secret
kvx doctor                                       # checks that the principal signs in
```

`kvx principal set` reads the secret from stdin (piped, or typed without echo), so it stays out of shell history. kvx keeps it in its encrypted store, under the same key as your vaults, and never returns it: `kvx principal show` prints only the tenant and client ids. A new principal applies to the next token request, with no restart. `kvx principal` and `kvx doctor` ask the serving kvx which mode it runs in, so they need no `--Kvx:Passthrough` of their own. `ServicePrincipal` must be the only entry in `Kvx:Passthrough`, so an app never falls back to acting as you when the principal fails. A principal that is missing or cannot sign in gives the app an error naming it and the Entra error code.

## Each app as its own principal

In production, an app that runs as its own service principal or a user-assigned managed identity names it with `AZURE_CLIENT_ID`. Under `kvx run`, DefaultAzureCredential sends that id to kvx, and kvx signs in as the stored principal with that client id. This works whatever `Kvx:Passthrough` is set to, so the app keeps production's code and configuration, and its secret moves from your environment into kvx's store.

```sh
kvx principal set --tenant <tenant id> --client-id <app's client id>        # AZURE_CLIENT_ID is the principal itself
kvx principal set --tenant <tenant id> --client-id <dev principal's id> \
    --for <production managed identity's client id>                         # a dev principal stands in for it
kvx principal show                                                          # which AZURE_CLIENT_ID gets which principal
kvx principal clear --client-id <client id>
```

`--for` is for an identity kvx cannot sign in as, such as a managed identity, which exists only on Azure. Give the stand-in the same roles. Running `set` again for the same client id without `--for` or `--default` keeps both, so rotating a secret needs only the ids and the new secret.

An app that sends an `AZURE_CLIENT_ID` kvx has no principal for gets an error naming the id, and is never served by your login instead. If your shell sets `AZURE_CLIENT_ID` for other reasons, store a principal for it, or unset it for apps that should use kvx's passthrough. Apps that name no identity are unaffected: they get your login, or with `ServicePrincipal` the principal marked `--default`. kvx selects only by client id: an app that names its managed identity by resource id or object id gets an error.

## What a principal needs in Entra

- **An app registration**, preferably one per developer, so no secret is shared.
- **A client secret** with a short expiry. Certificates and federated credentials are not supported.
- **Role assignments** on the resources the app uses, the same ones production's managed identity has, and no more. They replace your own access for the app. A new principal has none: it signs in, and `kvx doctor` passes, but it sees no resources until it is given roles.
- **A check of tenant policy.** Some tenants restrict service-principal sign-in by location or forbid client secrets. The error then shows up in `kvx doctor`.

> [!WARNING]
> This puts a long-lived credential on your machine. It is encrypted at rest, but anything running as you that can read kvx's session token can get tokens as the principal while kvx runs. Give the principal only the roles the app needs, keep the secret's expiry short, and replace it with `kvx principal set` when it is rotated. `kvx principal clear --client-id <client id>` removes it. Each stored principal is one more such credential.
