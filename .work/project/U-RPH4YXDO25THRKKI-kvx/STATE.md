---
phase: execute
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - an unmodified Azure.Security.KeyVault.Secrets SecretClient passes a conformance suite against kvx covering set, get by version, list and list-versions with paging, update attributes, delete, recover, purge, backup and restore
  - a test that scans every file kvx writes finds no secret value in plaintext
  - the data key is protected by the OS keyring on Windows (DPAPI), macOS (Keychain) and Linux (Secret Service), each shown by a passing test on that OS in CI
  - with no keyring available kvx refuses to start unless a non-plaintext fallback is configured; there is no plaintext mode
  - a developer can install kvx as a dotnet tool, import an existing user-secrets store, and run an app whose only change is the vault URI
  - an Aspire hosting integration starts kvx and wires the vault URI and certificate trust into a resource
---

# project/kvx

## Purpose
Local development secrets (dotnet user-secrets and the like) sit unencrypted at rest, where backups, accidental commits and file-grepping tools (AI agents included) can read them. kvx keeps them encrypted, with the key held by the OS keyring. It speaks the Azure Key Vault wire protocol, so app code stays the same in dev and prod and only the vault URI changes.

## Scope
In: the Key Vault secrets API; keyring-backed envelope encryption; a dotnet tool; importing user-secrets; Aspire hosting integration.
Out (for now): the keys and certificates APIs; multi-user or networked deployment; managed HSM.

## Context & constraints
- Off-the-shelf survey (2026-10): wire-compatible emulators exist (calvinchengx Go emulator, AzureKeyVaultEmulator for .NET, Lowkey Vault), but they are test doubles with plaintext storage and fake auth. Keyring-backed tools (sops+age, 1Password `op run`) are not wire-compatible. The gap is the combination.
- Pure .NET, SDK 10 via `mise.local.toml`. Chosen over Go because the audience is enterprise .NET developers, mostly on Windows, where DPAPI is first-party, and because Aspire is the main integration point. Go's edge is distribution, which packaging can recover.
- Threat model: protects data at rest from anything that is not the logged-in user's live session. It does not protect against malware running as the same user, which can query kvx or the keyring directly.
- Same-user scope means the service runs per user, on loopback.

## Now
kvx is an installable dotnet tool on Linux that serves TLS itself with the ASP.NET Core dev cert, so nothing else needs installing. `kvx run` starts unmodified DefaultAzureCredential apps against it, and `kvx import` moves user-secrets in. `kvx doctor` says what is wrong with the machine's setup and how to fix it. An Aspire AppHost gets kvx with `RunAsKvx()`. Choosing the next milestone.

## Next
- Candidates:
  - the Windows/macOS key protectors (criterion 3)

## Evidence
- Walking skeleton (U-MTGVCUWN5POPRG5D), 2026-10-23: an unmodified app process sets and gets secrets via Caddy at https://test.kvx.localhost; the data dir holds no plaintext name or value (partial evidence for criteria 1 and 2).
- Secrets conformance (U-LVSM4WJS6GP7YORS), 2026-10-23: a stock SecretClient passes tests for every operation in criterion 1 (17/17 total). The no-plaintext scan now covers the full lifecycle and backup blobs (criterion 2). Error-case fidelity against a real vault is unconfirmed (B-MDJXFAWWOZWP6FIO).
- DefaultAzureCredential (U-VLRFD3QF7OBYKRKF), 2026-10-23: an app whose only credential code is `new DefaultAzureCredential()` round-trips through Caddy, configured by two environment variables. Tokens survive kvx restarts within the login session (22/22 tests).
- Az passthrough (U-CXXMFJZTD4J77REJ), 2026-10-23: the same app process also gets a token for another Azure resource from the developer's login. A missing login fails fast with 400 and sign-in instructions, confirmed live with the real credential chain (26/26 tests).
- Packaging (U-NFJTG766TNW2O6U2), 2026-10-23: `dotnet pack` produces the `kvx` tool. Installed to a fresh tool path, it serves from an unrelated directory, and its `env` output is accepted by sh and fish. `kvx run` round-trips the unmodified app through Caddy despite a service principal in the caller's environment, and passes through the app's exit code. `kvx import` moves user-secrets in, and AddAzureKeyVault reads them back (criterion 5, on Linux; Caddy is still a separate install). 35/35 tests.
- Kestrel TLS (U-OAREUQEMOSGNLS4M), 2026-10-23: Caddy is gone. The installed tool serves `https://<vault>.dev.localhost:5381` with the real dev cert, and `kvx run` round-trips the unmodified app with no trust variables from the caller; with no certificate kvx refuses to start and says how to get one. Criterion 5 now needs only the dotnet tool on Linux. 36/36 tests.
- kvx doctor (U-NZQKGC3WEV7UKMVJ), 2026-10-23: `kvx doctor` checks name resolution, the certificate kvx would serve, the key protector, and the serving kvx with a live handshake, and gives a fix for each failure. Each failure is induced in a test, and kvx's startup and not-serving errors point to it. The installed tool reports all five checks ok on BR's machine. 46/46 tests.
- Aspire hosting (U-PUQYAEAE4IXK6QX5), 2026-10-23: `AddAzureKeyVault(…).RunAsKvx()` lets an unmodified app using Aspire's Key Vault client set and get secrets on kvx in run mode, with no subscription. The AppHost reuses a serving kvx (imported secrets visible) or starts its own and stops it after. A .NET worker and curl both get trusted TLS, and publish mode is unchanged. A second server on one store refuses. Shown by tests through Aspire.Hosting.Testing, and by the sample AppHost on BR's machine with the installed tool (criterion 6). 53/53 tests.

## Harvest
- [ ] decision: pure .NET over Go (audience, DPAPI, Aspire; distribution recoverable by packaging)
- [ ] decision: build secrets-only from scratch rather than fork an existing emulator (avoid inheriting keys/certs code and test-double assumptions)
- [ ] decision: envelope encryption, keyring wraps a data key; never a plaintext fallback
- [ ] decision: whole-store sealed file over per-value encryption (simpler, hides names)
- [x] decision: `*.kvx.localhost` host scheme satisfies SDK challenge verification without client options → D-LLPISGPW4SP4BB4K (now `*.dev.localhost`)
- [ ] decision: systemd-creds --user as the headless Linux key protector
- [x] backlog: replace Caddy with Kestrel-managed TLS before packaging as a dotnet tool → done by U-OAREUQEMOSGNLS4M
- [ ] backlog: real Entra token validation

## Log
- 2026-10-23 initialized; .NET chosen after build-vs-buy and Go-vs-.NET discussion
- 2026-10-23 settled auth (ephemeral dev tokens), host scheme, Caddy TLS, systemd-creds, whole-store encryption
- 2026-10-23 phase initialize → execute
- 2026-10-23 walking-skeleton milestone delivered and closed
- 2026-10-23 secrets-conformance milestone delivered and closed
- 2026-10-23 packaging milestone delivered and closed
- 2026-10-23 kestrel-tls milestone delivered and closed; Caddy replaced by the dev cert
- 2026-10-23 kvx-doctor milestone delivered and closed
- 2026-10-23 aspire-hosting milestone delivered and closed
