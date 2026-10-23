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
Walking skeleton delivered: the stock SDK set/get works end to end on Linux through Caddy, with the sealed store and systemd-creds. Choosing the next milestone.

## Next
- Candidate next milestone: the rest of the secrets API (list with paging, versions, update, delete/recover/purge, backup/restore) as a conformance suite.
- Candidates after that: DefaultAzureCredential support, the Windows/macOS key protectors, `kvx doctor`, dotnet tool packaging.

## Evidence
- Walking skeleton (U-MTGVCUWN5POPRG5D), 2026-10-23: an unmodified app process sets and gets secrets via Caddy at https://test.kvx.localhost; the data dir holds no plaintext name or value (partial evidence for criteria 1 and 2).

## Harvest
- [ ] decision: pure .NET over Go (audience, DPAPI, Aspire; distribution recoverable by packaging)
- [ ] decision: build secrets-only from scratch rather than fork an existing emulator (avoid inheriting keys/certs code and test-double assumptions)
- [ ] decision: envelope encryption, keyring wraps a data key; never a plaintext fallback
- [ ] decision: whole-store sealed file over per-value encryption (simpler, hides names)
- [ ] decision: `*.kvx.localhost` host scheme satisfies SDK challenge verification without client options
- [ ] decision: systemd-creds --user as the headless Linux key protector
- [ ] backlog: replace Caddy with Kestrel-managed TLS before packaging as a dotnet tool
- [ ] backlog: real Entra token validation

## Log
- 2026-10-23 initialized; .NET chosen after build-vs-buy and Go-vs-.NET discussion
- 2026-10-23 settled auth (ephemeral dev tokens), host scheme, Caddy TLS, systemd-creds, whole-store encryption
- 2026-10-23 phase initialize → execute
- 2026-10-23 walking-skeleton milestone delivered and closed
