# kvx

A local, encrypted-at-rest stand-in for Azure Key Vault secrets, so development secrets stop living in plaintext.

> [!WARNING]
> **An app pointed at kvx through `DefaultAzureCredential` gets *all* its Azure tokens through kvx — and by default reaches every other Azure service as *you*, only if you are signed in.**
>
> `DefaultAzureCredential` keeps using the first credential that works. Once the app has a token from kvx, every Azure client in that process (Storage, Service Bus, Cosmos DB, ARM, …) asks kvx for its token too. kvx answers Key Vault itself and passes every other request through to your own login:
>
> - **You must be signed in** with `az login`, `azd auth login`, Visual Studio or `Connect-AzAccount`, in the session kvx runs in. Otherwise those calls fail with `AuthenticationFailedException`, and the message says to sign in.
> - **The app acts as you, not as its managed identity.** It sees your role assignments, your default tenant and your subscriptions, so access that works locally may still fail in production, and the reverse.
> - **Anything holding the session token can get your Azure tokens from kvx.** That is the same reach as running `az account get-access-token` yourself, since the token file is readable only by you.
>
> Set `Kvx:Passthrough` to an empty string to turn passthrough off. Apps then reach only Key Vault through kvx. To have apps act as a scoped, non-human identity instead of you, [pass through as a service principal](#pass-through-as-a-service-principal).

## Install

```sh
dotnet pack src/Kvx.Server -o ./artifacts
dotnet tool install --global kvx --add-source ./artifacts
```

Settings live in `appsettings.json` next to the installed tool. Any of them can be overridden per command, for example `--urls=…` or `--Kvx:Passthrough=`. Client commands must use the same `urls` and `Kvx:TokenFile` as the server they talk to.

**When something does not work, run `kvx doctor` first.** It checks what kvx needs from the machine (vault names resolving to loopback, a valid and trusted certificate for them, the key protector, a serving kvx) and says how to fix each problem, because those problems otherwise show up as SDK errors that don't name the cause.

## Serve

```sh
kvx                     # or: kvx serve
```

Vaults are served at `https://<vault>.dev.localhost:5381`, with the ASP.NET Core development certificate, which covers `*.dev.localhost`. Each vault name is just a host label; vaults appear the first time you write to them.

The certificate has to exist and be trusted once per machine:

- **Windows, macOS:** Visual Studio and Rider usually do this already; otherwise run `dotnet dev-certs https --trust`.
- **Linux:** run `dotnet dev-certs https --trust`. .NET apps then trust it. It warns that OpenSSL needs `SSL_CERT_DIR`; that only matters for non-.NET tools such as curl, and `kvx run` and `kvx env` set it for them.

kvx refuses to start without a certificate. To serve another domain, set `Kvx:Domain` and give Kestrel a certificate for `*.<domain>` under `Kestrel:Certificates:Default`.

Vault names have to resolve to loopback:

- **macOS, most Linux:** they already do. On a Linux without systemd-resolved, `kvx doctor` names the `myhostname` module to install.
- **Windows:** run this once in an elevated PowerShell:

  ```powershell
  Add-DnsClientNrptRule -Namespace '.localhost' -NameServers '127.0.0.1'
  ```

  Windows then asks kvx for every `*.localhost` name, and kvx answers them with loopback on `127.0.0.1:53` while it runs, so new vaults resolve without further setup. Set `Kvx:Dns` to `off` to turn the responder off.
- **Anywhere the above can't work** (port 53 held by another program, Group Policy NRPT rules, no admin rights): add a `127.0.0.1 <vault>.dev.localhost` line per vault to the hosts file (`/etc/hosts`, `%SystemRoot%\System32\drivers\etc\hosts`).

The store's key is protected by DPAPI on Windows, the login keychain on macOS and `systemd-creds --user` on Linux, all of them per user. **On Windows, an administrator resetting your password (rather than you changing it) makes the store unreadable.**

kvx also listens on plain http on loopback (`127.0.0.1:5380`) for the managed-identity endpoint and its own commands, as Azure's identity endpoint does, so neither depends on certificate trust.

## Run an app against kvx

```sh
kvx run -- dotnet run --project src/MyApp
```

kvx serves the managed-identity protocol, so `new DefaultAzureCredential()` works with no code change. `kvx run` starts the app with `IDENTITY_ENDPOINT` and `IDENTITY_HEADER` set (and `SSL_CERT_DIR` on Linux), and returns the app's exit code. It refuses to start the app when kvx is not serving, so the app never falls back to something else unnoticed.

`AZURE_CLIENT_SECRET`, a workload-identity setup and similar variables come earlier in the `DefaultAzureCredential` chain and would win over kvx. `kvx run` removes them from the app's environment and names each one it removes. The list is `Kvx:RunUnset` in `appsettings.json`.

For an IDE or a long-lived shell, `kvx env` prints the same changes as shell commands:

```sh
eval "$(kvx env)"            # sh, bash, zsh
kvx env | source             # fish
kvx env --shell pwsh | Invoke-Expression
```

The token is per login session, so these values stay valid across kvx restarts until you log out.

## Pass through as a service principal

With `Kvx:Passthrough` set to `ServicePrincipal`, kvx gets tokens for other Azure services by signing in as a service principal, not as you. The app then sees that principal's role assignments, which you can make match what its managed identity has in production. Key Vault still gets kvx's own token.

```sh
kvx --Kvx:Passthrough=ServicePrincipal          # or set it in appsettings.json
kvx principal set --tenant <tenant id> --client-id <application id>   # asks for the client secret
kvx doctor                                       # checks that the principal signs in
```

`kvx principal set` reads the secret from stdin (piped, or typed without echo), so it stays out of shell history. kvx keeps it in its encrypted store, under the same key as your vaults, and never returns it: `kvx principal show` prints only the tenant and client id. A new principal applies to the next token request, with no restart, but switching `Kvx:Passthrough` itself takes a restart of kvx. `kvx principal` and `kvx doctor` ask the serving kvx which mode it runs in, so they need no `--Kvx:Passthrough` of their own. `ServicePrincipal` must be the only entry in `Kvx:Passthrough`, so an app never falls back to acting as you when the principal fails. A principal that is missing or cannot sign in gives the app an error naming it and the Entra error code.

In Entra this needs:

- **An app registration**, preferably one per developer, so no secret is shared.
- **A client secret** with a short expiry. Certificates and federated credentials are not supported.
- **Role assignments** on the resources the app uses, the same ones production's managed identity has, and no more. They replace your own access for the app. A new principal has none: it signs in, and `kvx doctor` passes, but it sees no resources until it is given roles.
- **A check of tenant policy.** Some tenants restrict service-principal sign-in by location or forbid client secrets. The error then shows up in `kvx doctor`.

> [!WARNING]
> This puts a long-lived credential on your machine. It is encrypted at rest, but anything running as you that can read kvx's session token can get tokens as the principal while kvx runs. Give the principal only the roles the app needs, keep the secret's expiry short, and replace it with `kvx principal set` when it is rotated. `kvx principal clear` removes it.

## Use kvx from an Aspire AppHost

```sh
dotnet pack src/Kvx.Aspire.Hosting -o ./artifacts
dotnet add path/to/AppHost package Kvx.Aspire.Hosting --source ./artifacts
```

```csharp
var secrets = builder.AddAzureKeyVault("secrets").RunAsKvx();
builder.AddProject<Projects.MyApp>("myapp").WithReference(secrets);
```

`RunAsKvx` changes only run mode. The vault stays Aspire's own Azure Key Vault resource, so the app keeps Aspire's Key Vault client integration unchanged, and publishing still provisions a real Key Vault. The local run needs no Azure subscription.

In run mode the AppHost shows kvx as a resource with its logs. If your kvx is already serving, the AppHost uses it, so secrets you imported are there; otherwise it starts kvx and stops it again with the AppHost. Every resource that references the vault waits for kvx, gets it as its managed identity the way `kvx run` sets that up, and trusts its certificate. The [passthrough warning](#kvx) above applies to those resources too.

The AppHost's `Kvx` configuration section sets `Command` (default `kvx`) and `Settings`, a list of settings passed to every kvx command, for example `["--Kvx:Domain=…"]`. They must match the kvx you run yourself if the AppHost is to reuse it. [samples/Kvx.Aspire.Sample](samples/Kvx.Aspire.Sample) is a working AppHost.

## Move user-secrets into a vault

```sh
kvx import --vault myapp --project src/MyApp            # or --id <UserSecretsId>, or --file secrets.json
kvx import --vault myapp --project src/MyApp --remove-plaintext
```

Keys are stored under the names the Key Vault configuration provider maps back, so `ConnectionStrings:Db` becomes `ConnectionStrings--Db`, and the app reads the same keys after:

```csharp
builder.Configuration.AddAzureKeyVault(new Uri("https://myapp.dev.localhost:5381"), new DefaultAzureCredential());
```

kvx reports any key that cannot be a secret name, and keeps the plaintext file unless every key was imported. Re-running skips secrets that are already in the vault unchanged.
