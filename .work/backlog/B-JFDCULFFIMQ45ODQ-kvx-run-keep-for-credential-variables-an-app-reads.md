---
created: 2026-10-26
---

# B-JFDCULFFIMQ45ODQ kvx run --keep for credential variables an app reads itself

## Why
An app that builds its own credential per client (e.g. ClientSecretCredential from AZURE_CLIENT_SECRET for non-vault clients, ManagedIdentityCredential for kvx) loses the secret under `kvx run`, which strips it (D-IJMXLBG5U3LFTH62). `Kvx:RunUnset` is a list, which a command-line override cannot shorten.

## Notes
BR, 2026-10-26: not wanted for now. Keeping the variable means a plaintext client secret in the developer's environment, which kvx exists to remove. Better paths: the app reads the secret from kvx, or kvx signs in per app (U-PEPFZ7IO6GKWIV4F). Revisit only for an app that can do neither.
