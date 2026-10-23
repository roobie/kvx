---
date: 2026-10-23
read_if:
  - changing delete, recover or deleted-secret endpoints
refs:
  - src/Kvx.Server/Api/SecretsApi.cs#Delete
---

# L-JK27OFYRSA6SLPSZ SDK delete and recover operations poll for 404 as pending

## Observation
The SDK's delete operation polls the deleted-secret endpoint, and its recover operation polls the get-latest endpoint (no trailing slash). For both, 404 means still pending, 200 or 403 means done, and anything else fails. Delete skips polling entirely when the response has no `recoveryId`. Source: Azure.Security.KeyVault.Secrets 4.11.2, `DeleteSecretOperation` and `RecoverDeletedSecretOperation`, read 2026-10-23.

## Why it matters
- A 404 on those endpoints never fails the operation; the client keeps polling every 2s instead. A bug that loses a just-deleted or just-recovered secret therefore looks like a hang, not an error.
- Answering immediately is why the tests finish without waiting.
- A purge-protected or no-soft-delete mode would have to drop `recoveryId`, or the SDK will poll for something that never appears.
- This stops holding if the SDK changes its operation classes; the conformance tests would show it as slow or hanging tests.
