# kvx

A local, encrypted-at-rest stand-in for Azure Key Vault secrets, so development secrets stop living in plaintext.

> [!WARNING]
> **An app pointed at kvx through `DefaultAzureCredential` gets *all* its Azure tokens through kvx — and by default reaches every other Azure service as *you*, only if you are signed in.** It sees your role assignments, tenant and subscriptions, not its managed identity's. Passthrough can be turned off, or made to sign in as a service principal, one per app if you like: [choose how apps reach other Azure services](docs/passthrough.md).

## Install

```sh
dotnet pack src/Kvx.Server -o ./artifacts
dotnet tool install --global kvx --add-source ./artifacts
```

**When something does not work, run `kvx doctor` first.**

## What do you want to do?

Each guide is also built into kvx, matching the installed version: `kvx help <topic>` prints it.

| Goal | Guide | Topic |
|---|---|---|
| Serve vaults and run an app against them | [get-started](docs/get-started.md) | `get-started` |
| Choose how apps reach other Azure services: your login, off, a service principal, or one per app | [passthrough](docs/passthrough.md) | `passthrough` |
| Use kvx from an Aspire AppHost | [aspire](docs/aspire.md) | `aspire` |
| Move .NET user-secrets into a vault | [import](docs/import.md) | `import` |
| Find out why something does not work | [troubleshoot](docs/troubleshoot.md) | `troubleshoot` |

`kvx help` lists every command, and `kvx <command> --help` shows one.
