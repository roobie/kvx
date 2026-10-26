# Get started: serve vaults and run an app against them

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

An app run this way gets *all* its Azure tokens through kvx, not only Key Vault's. By default it reaches every other Azure service as you: `kvx help passthrough` says what that means and how to change it.
