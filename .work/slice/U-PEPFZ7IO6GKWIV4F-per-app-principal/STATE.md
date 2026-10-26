---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-26
success_criteria:
  - a token request carrying a client_id that matches a stored principal, by its own client id or by an alias, is served by that principal for any non-kvx resource; kvx's own resource gets kvx's token whatever the client_id (test)
  - an unknown client_id is answered 400 naming it and the `kvx principal set` that would serve it, in every Kvx:Passthrough mode, never falling back (test)
  - requests without a client_id behave as before in every Kvx:Passthrough mode (tests)
  - an unmodified DefaultAzureCredential app under `kvx run`, with AZURE_CLIENT_ID set to a stored principal's id and AZURE_CLIENT_SECRET in the caller's environment, reaches other resources as that principal and kvx's vaults with kvx's token (test)
  - `kvx principal set … [--for <id>]` adds or replaces one principal, `show` lists them all without secrets, `clear` removes one; `kvx doctor` checks each stored principal signs in (tests)
  - a store written by the single-principal release still loads with its principal intact (test)
  - the README covers per-app principals and how they line up with production's AZURE_CLIENT_ID
  - an app with its own service principal runs against kvx on BR's machine with production's code and AZURE_CLIENT_ID, or BR waives the live check
---

# slice/per-app-principal

## Purpose
BR wants dev workloads to run on production's code and configuration. An app that runs as its own service principal (or a user-assigned managed identity) in production says which identity it wants through AZURE_CLIENT_ID. Today kvx ignores that and serves every app the same passthrough. This slice lets kvx sign in as the principal the app asks for, so the app's secret moves from the developer's environment into kvx's sealed store.

## Scope
In: several stored principals, each found by its client id and optional aliases; client_id on the identity endpoint selecting one; `kvx principal` and `kvx doctor` for several; loading single-principal stores; README.
Out: object_id and mi_res_id selection (refused with a message); certificates (B-3XPOFTHP5D4SHBBN); per-resource mix (B-A6YQYZBNQLXE4NRU); `kvx run --keep` (B-JFDCULFFIMQ45ODQ, not wanted: plaintext secret in the environment).

## Context & constraints
- BR, 2026-10-26: a principal is found by its own client id, and `--for <id>` adds an alias (e.g. production's user-assigned managed identity's client id). Requests without a client_id go by Kvx:Passthrough exactly as now; with a client_id they always get the stored principal, in any mode, and an unknown one is refused.
- Never fall back to the developer for a request that named an identity (D-WHMYBXHZAGDFWJNC).
- `kvx run` keeps AZURE_CLIENT_ID and strips AZURE_CLIENT_SECRET (D-IJMXLBG5U3LFTH62), so DefaultAzureCredential skips EnvironmentCredential and its ManagedIdentityCredential sends client_id to kvx. A plain `new ManagedIdentityCredential()` sends none, so explicit-credential apps are unaffected.
- Client commands learn server state from the serving kvx (D-7FE3TXQRH3WS4KTE, D-L4OU7YMFJ6W6OENA).
- Identity endpoint errors stay 400/401 (L-GPX6ZOUCE2IAT4SR).

### Origin
Promoted from backlog: B-AYIFGKIAQ7CX64W3 Per-app passthrough principal chosen by the managed-identity client_id

## Now
Initialized; next is the store model and the endpoint.

## Next
- Store: several principals with aliases; read the single-principal field.
- Identity endpoint: client_id → principal; unknown → 400.
- API, CLI, doctor; README; sample-app test under `kvx run`.

## Evidence

## Harvest

## Log
- 2026-10-26 initialized from B-AYIFGKIAQ7CX64W3 after BR's live check of the single principal
- 2026-10-26 BR chose: own client id plus optional alias; no client_id behaves as today
- 2026-10-26 phase initialize → execute
