---
status: accepted
date: 2026-10-26
read_if:
  - changing what the identity endpoint does for other resources
  - adding a credential type or several principals
  - reviewing kvx's security boundary
refs:
  - src/Kvx.Server/Auth/Upstream.cs#FromConfig
  - src/Kvx.Server/Auth/PrincipalCredential.cs#PrincipalCredential
  - src/Kvx.Server/Api/PrincipalApi.cs#PrincipalApi
  - README.md
---

# D-WHMYBXHZAGDFWJNC Passthrough can sign in as one service principal, held in kvx's store, as its only source

## Context
Passthrough (D-BGGXSS3A5S3YOP24) makes apps act as the developer, which differs from production's managed identity. Some apps must be tested under a scoped, non-human identity with no code change. `kvx run` strips AZURE_CLIENT_SECRET and its kin from apps (D-IJMXLBG5U3LFTH62), so the principal's credential has to live with the server. BR chose the three options below on 2026-10-26.

## Decision
`ServicePrincipal` is a passthrough source that signs in with a client secret kept in kvx's sealed store and set through the serving kvx. It must be the only entry in `Kvx:Passthrough`, and there is one principal per kvx.

## Why
- **Exclusive:** a credential chain moves on when a credential is unavailable, so a missing or broken principal would quietly turn into the developer, the very thing this mode is chosen to avoid. A loud failure is better.
- **Sealed store:** it is encrypted under the vault key, never returned by any API, and outside config and env, which leak into files, history and child processes. A certificate in the OS store would avoid a secret, but it needs per-OS work and Linux has no common store.
- **One per kvx:** it covers the request. Choosing by the managed-identity client_id is possible later without changing this.
- Live-checked by BR on Windows: an unmodified DefaultAzureCredential got the principal's app token while Key Vault still worked through kvx.

## Consequences
- A long-lived credential sits on the dev machine; anything that can read the session token can get the principal's tokens while kvx runs. The README warns about this and asks for least privilege and short expiry.
- kvx must serve with no principal set, so `kvx principal set` can reach it. Until one is set, token requests are 400.
- Certificates, several principals and per-resource mixing are backlog (B-3XPOFTHP5D4SHBBN, B-AYIFGKIAQ7CX64W3, B-A6YQYZBNQLXE4NRU).
