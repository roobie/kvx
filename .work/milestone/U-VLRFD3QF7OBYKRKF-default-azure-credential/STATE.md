---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - an app whose only credential code is `new DefaultAzureCredential()` sets and gets secrets through Caddy, configured solely by the environment variables IDENTITY_ENDPOINT and IDENTITY_HEADER (with any AZURE_* and other managed-identity variables removed)
  - a token issued before a kvx restart is still accepted after it, within the same login session; with the runtime token file gone, kvx issues a new token and the old one is rejected
  - the identity endpoint answers a missing or wrong header with 401 and a resource other than kvx's with 400, each with a Key Vault-style error body (never a status that the client retries)
---

# milestone/default-azure-credential

## Purpose
Make kvx usable by real apps without code changes: `DefaultAzureCredential` is what nearly every Azure .NET app uses, and today an app must read kvx's token file itself, so it is not truly unmodified.

## Scope
In: the App Service managed-identity protocol on kvx; login-session token lifetime; resource check; E2E with a stock DefaultAzureCredential.
Out: a `kvx env` / `kvx run` command and the Aspire wiring (packaging milestones); real Entra tokens; other credential types in the chain.

## Context & constraints
- Wire facts, read from source 2026-10-23 (Azure.Identity 1.21.0 forwards to Azure.Core 1.63.0 with MSAL 4.90.0):
  - IDENTITY_ENDPOINT plus IDENTITY_HEADER selects the App Service source, with no IMDS probe. The endpoint may be plain http on loopback.
  - The `kvx` tenant is ignored.
  - MSAL caches the token for the whole process, and the chain latches the first credential that works.
  - 4xx/5xx answers abort the chain, and 404/408/429/5xx are retried, so errors must be 400 or 401.
- BR decided 2026-10-23: a token lives for the login session (kept in $XDG_RUNTIME_DIR, reused across kvx restarts), and the same value is the IDENTITY_HEADER. This replaces the earlier rule that tokens die with the process.
- Until a CLI exists, the developer sets IDENTITY_HEADER from the token file.

## Now
All criteria hold; ready to finalize.

## Next
- Finalize: harvest, close.

## Evidence
- Criterion 1: `CaddyEndToEndTests.Unmodified_app_round_trips_through_caddy` passes. The sample app's only credential code is `new DefaultAzureCredential()`, and the test strips AZURE_/IDENTITY_/MSI_ variables before setting the two IDENTITY_* ones.
- Criterion 2: `IdentityTests.Token_survives_restart_and_is_replaced_when_the_session_file_is_gone` passes.
- Criterion 3: `IdentityTests.Missing_or_wrong_header_is_401` (null, empty, wrong) and `Other_resources_are_400_with_a_reason` pass.
- `mise exec -- dotnet test`: 22/22 on 2026-10-23.

## Harvest
- [ ] decision: kvx tokens live for the login session and double as IDENTITY_HEADER (BR, 2026-10-23); supersedes "tokens die with the process" in DESIGN.md
- [ ] decision: DefaultAzureCredential is served via the App Service managed-identity protocol; the alternatives (custom authority host, az CLI shim) were ruled out by source
- [ ] learning: DefaultAzureCredential latches the first working credential and MSAL caches managed-identity tokens per process, so a kvx token is offered for every resource the app asks for
- [ ] backlog: `kvx env` / `kvx run` to put IDENTITY_ENDPOINT and IDENTITY_HEADER into an app's environment

## Log
- 2026-10-23 initialized; wire facts verified from source; BR chose login-session token lifetime; phase initialize → execute
