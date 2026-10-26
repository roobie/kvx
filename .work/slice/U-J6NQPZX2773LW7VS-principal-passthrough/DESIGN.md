# Design

## Problem
Passthrough can only act as the developer. An app that must be tested as a scoped, non-human identity has no path that keeps `new DefaultAzureCredential()` as its only credential code.

## Approach
- **Source.** `Kvx:Passthrough` accepts `ServicePrincipal`. Listed with anything else, kvx refuses to start: a chain falls through on "unavailable", so a missing principal would quietly act as the developer, which is the thing this mode exists to avoid.
- **Storage.** The sealed store document gains an optional principal (tenant, client id, secret, updated). It shares the vaults' data key, so it gets the same at-rest protection with no new mechanism, and the vault API cannot reach it. Tenant and client id sit with the secret rather than in appsettings so that one command replaces the principal atomically and live; config files next to an installed tool are awkward to edit anyway.
- **Setting it.** `kvx principal set --tenant <id> --client-id <id>` reads the secret from stdin (no echo on a terminal), never from argv, where it would land in shell history and process listings. `show` prints tenant, client id and when it was set; `clear` removes it. They talk to `/kvx/principal` on the serving kvx with the session token, like `kvx import`. kvx serves with no principal stored, otherwise the command could never run.
- **Credential.** A `PrincipalCredential` reads the stored principal per request and rebuilds its `ClientSecretCredential` when the principal changes; MSAL's in-memory cache does the rest. Nothing persists tokens to disk. Its failures name the client id and tenant.
- **Messages.** `Upstream` carries where tokens come from and how to fix a failure, so the identity endpoint says "sign in with …" for logins and points at `kvx doctor` / `kvx principal set` for the principal.
- **Doctor.** With the principal selected: not stored → fail with the set command; stored → ask the identity endpoint for an ARM token, which proves sign-in without needing any role assignment.

## Alternatives considered
- Mixing in one chain, or per resource: rejected for now (BR); fallback defeats the purpose, and per-resource needs mapping config nobody has asked for yet.
- Certificate in the OS store: no secret at rest in kvx, but Linux has no user cert store, so it would need a plaintext PEM there. Deferred to backlog.
- The secret as a secret in a kvx vault: reuses the API, but then every app under kvx could read the principal's long-lived credential, not just get short-lived tokens from it.
- appsettings or environment variables: plaintext at rest, and env is exactly what `kvx run` strips.
- Per app by managed-identity client_id: unmodified apps send none; deferred.

## Risks & open questions
- An older kvx that rewrites the store drops the principal (unknown JSON property). Acceptable for a dev tool; re-run `set`.
- Tenants with Conditional Access for workload identities or a ban on client secrets: surfaced as the AADSTS text in the 400 and in doctor, not worked around.
- Live verification needs a real app registration in BR's tenant.
