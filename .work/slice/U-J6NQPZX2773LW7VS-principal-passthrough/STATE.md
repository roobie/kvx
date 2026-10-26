---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-26
success_criteria:
  - with Kvx:Passthrough=ServicePrincipal and a principal stored by `kvx principal set`, a non-kvx token request is served by a client-secret credential built from the stored tenant, client id and secret, while kvx's own resource still gets kvx's token (test)
  - changing the stored principal takes effect on the next token request without restarting kvx (test)
  - ServicePrincipal listed together with another passthrough source makes kvx refuse to start, saying why (test)
  - no principal stored, or a sign-in failure, answers 400 naming the principal and the command that fixes it; `kvx doctor` reports a missing or failing principal with a fix (tests)
  - the principal's secret appears in no file kvx writes and in no response (vault API, `kvx principal show`) (test)
  - the README covers principal passthrough, what it needs in Entra, and the risk of a long-lived credential on the dev machine
  - a real service principal gets a token through kvx on BR's machine, or BR waives the live check
---

# slice/principal-passthrough

## Purpose
Apps run through kvx reach every non-Key Vault Azure service as the developer. Some must be tested under a scoped, non-human identity closer to production's managed identity, with no code change. This slice lets kvx's passthrough sign in as a service principal instead of the developer's login.

## Scope
In: a `ServicePrincipal` passthrough source; its tenant, client id and client secret held in kvx's sealed store, set through the serving kvx by `kvx principal set|show|clear`; failure messages; a doctor check; README.
Out (BR, 2026-10-26): certificates; several principals or a choice per app; mixing the principal with developer logins, per resource or in a chain. Out (issue): kvx accepting Entra tokens for its vaults; emulating managed identities one-to-one.

## Context & constraints
- Passthrough exists because DefaultAzureCredential latches kvx for every resource (L-GPX6ZOUCE2IAT4SR, D-BGGXSS3A5S3YOP24). The upstream is an explicit list, never DefaultAzureCredential.
- `kvx run` strips AZURE_CLIENT_SECRET and friends from apps (D-IJMXLBG5U3LFTH62); the principal's credential must therefore live with the server, not in the app's environment.
- Client commands never open the store; they go through the serving kvx (KvxClient). So `kvx principal set` needs kvx serving, and kvx must serve with no principal stored.
- Upstream failures stay 400 so clients do not retry.
- Threat model unchanged: same-user processes are trusted. The new exposure is persistence: a long-lived secret on the machine, encrypted at rest.

## Now
Design written (DESIGN.md); BR answered the three open questions. Ready to move to execute.

## Next
- Store: principal in StoreDocument, repository get/set/clear; no-plaintext scan covers it.
- Upstream refactor (From/Fix) and `ServicePrincipal` source with exclusivity; PrincipalCredential.
- `/kvx/principal` endpoint and `kvx principal` command.
- Doctor check, README, live check with BR.

## Evidence

## Harvest
- [ ] decision: ServicePrincipal is the only passthrough source when listed, so a broken principal never falls back to acting as the developer
- [ ] decision: the principal's secret lives in kvx's sealed store, outside the vault API, set through the serving kvx; not config, not env, not a cert file
- [ ] decision: one principal per kvx; per-app selection deferred
- [ ] backlog: certificate credential for the principal (OS store on Windows/macOS; sealed store elsewhere)
- [ ] backlog: per-app principal chosen by the managed-identity client_id
- [ ] backlog: per-resource mix of principal and developer login

## Log
- 2026-10-26 initialized from BR's issue "Let passthrough use a service principal instead of the developer's login"
- 2026-10-26 BR chose: exclusive source, client secret in kvx's store, one principal per kvx
- 2026-10-26 phase initialize → execute
