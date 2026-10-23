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

## Install

```sh
dotnet pack src/Kvx.Server -o ./artifacts
dotnet tool install --global kvx --add-source ./artifacts
```

Settings live in `appsettings.json` next to the installed tool. Any of them can be overridden per command, for example `--urls=…` or `--Kvx:Passthrough=`. Client commands must use the same `urls` and `Kvx:TokenFile` as the server they talk to.

## Serve

```sh
kvx                     # or: kvx serve
```

kvx listens on plain http on loopback. SDKs require https, so put a TLS front before it. `dev/Caddyfile` serves `https://<vault>.kvx.localhost:8443` with Caddy's local CA, which you trust once with `caddy trust`. Each vault name is just a host label; vaults appear the first time you write to them.

## Run an app against kvx

```sh
kvx run -- dotnet run --project src/MyApp
```

kvx serves the managed-identity protocol, so `new DefaultAzureCredential()` works with no code change. `kvx run` starts the app with `IDENTITY_ENDPOINT` and `IDENTITY_HEADER` set, and returns the app's exit code. It refuses to start the app when kvx is not serving, so the app never falls back to something else unnoticed.

`AZURE_CLIENT_SECRET`, a workload-identity setup and similar variables come earlier in the `DefaultAzureCredential` chain and would win over kvx. `kvx run` removes them from the app's environment and names each one it removes. The list is `Kvx:RunUnset` in `appsettings.json`.

For an IDE or a long-lived shell, `kvx env` prints the same changes as shell commands:

```sh
eval "$(kvx env)"            # sh, bash, zsh
kvx env | source             # fish
kvx env --shell pwsh | Invoke-Expression
```

The token is per login session, so these values stay valid across kvx restarts until you log out.

## Move user-secrets into a vault

```sh
kvx import --vault myapp --project src/MyApp            # or --id <UserSecretsId>, or --file secrets.json
kvx import --vault myapp --project src/MyApp --remove-plaintext
```

Keys are stored under the names the Key Vault configuration provider maps back, so `ConnectionStrings:Db` becomes `ConnectionStrings--Db`, and the app reads the same keys after:

```csharp
builder.Configuration.AddAzureKeyVault(new Uri("https://myapp.kvx.localhost:8443"), new DefaultAzureCredential());
```

kvx reports any key that cannot be a secret name, and keeps the plaintext file unless every key was imported. Re-running skips secrets that are already in the vault unchanged.
