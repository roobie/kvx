---
created: 2026-10-26
---

# B-AYIFGKIAQ7CX64W3 Per-app passthrough principal chosen by the managed-identity client_id

## Why
Apps that have different managed identities in production would each want their own principal locally; today one kvx has one principal (D-WHMYBXHZAGDFWJNC).

## Notes
Managed-identity requests can carry a client_id, which could select among several stored principals. Wait for a concrete user before building it.
