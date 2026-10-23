---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - "`dotnet pack` produces a dotnet tool; installed to a fresh tool path, `kvx` serves from any working directory, and `kvx env` prints exports that a POSIX shell and fish accept"
  - "`kvx run -- <app>` runs an unmodified DefaultAzureCredential app that round-trips secrets through Caddy, even with stray AZURE_* credential variables in the caller's environment, and exits with the app's exit code"
  - "`kvx run` with no kvx serving exits non-zero without starting the app, and says how to start kvx"
  - "`kvx import` moves a project's user-secrets into a vault under Key Vault configuration-provider names (`:` → `--`), reports keys it cannot map, and the app reads them back through AddAzureKeyVault"
  - the README documents install, serve, run, env and import
---

# milestone/packaging

## Purpose
Get the developer from `dotnet tool install` to a running app reading its secrets from kvx with one command, and give them a way to move existing plaintext user-secrets in. This is project criterion 5.

## Scope
In: tool packaging (`kvx` command), a content root independent of the working directory, `kvx` (serve), `kvx run`, `kvx env`, `kvx import`, README.
Out: publishing to nuget.org; starting or bundling Caddy; a systemd user unit; Windows/macOS; Aspire.

## Context & constraints
- `kvx run` and `kvx import` talk to a kvx that is already serving, never open the store themselves. The store is a single file held in memory by the server, so a second writer would lose updates.
- `kvx import` uses the plain-http loopback listener with the vault in the Host header. That needs neither Caddy nor TLS trust, and still goes through auth with the session token.
- `kvx run` removes the variables that make DefaultAzureCredential pick something before managed identity. The list is configuration (content), not code.
- Running with no subcommand must keep serving, because WebApplicationFactory drives the entry point with no arguments.

### Origin
Promoted from backlog: B-UW2PA4KWMLEWUDVQ kvx env / kvx run to inject identity variables. Also covers B-4YCFBMDYVWGYKPV6 (pin the content root).

### Why
Developers currently have to set IDENTITY_ENDPOINT, and copy IDENTITY_HEADER from the token file, by hand. That is friction, and an easy place to leak the token into shell history.

## Now
Tool packaging: `dotnet pack`, install to a fresh tool path, serve from another directory.

## Next
- README: install, serve, run, env, import.
- Finalize.

## Evidence
- Criteria 2–4, as processes against kvx on loopback (35/35 green): `CliTests` covers run (identity set, AZURE_CLIENT_SECRET removed and reported, exit code 7 passed through, 127 when the command is missing), refusal without a server or token file, env evaluated by sh and fish, import mapping/unmappable/unchanged/--remove-plaintext. `CaddyEndToEndTests` runs the sample app through `kvx run` with a service principal in the environment, and reads imported user-secrets back via AddAzureKeyVault.

## Harvest
- [ ] decision: one binary; no verb serves; run/env/import are clients of the serving kvx and never open the store
- [ ] decision: the variables `kvx run` removes are configuration (Kvx:RunUnset), reported on stderr each time
- [ ] backlog: `kvx run` forwards SIGTERM/SIGHUP to the app (today only Ctrl-C reaches it, via the process group)

## Log
- 2026-10-23 initialized from B-UW2PA4KWMLEWUDVQ; added import to cover project criterion 5; phase initialize → execute
