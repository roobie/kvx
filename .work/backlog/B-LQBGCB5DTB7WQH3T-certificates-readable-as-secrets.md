---
created: 2026-10-24
---

# B-LQBGCB5DTB7WQH3T certificates readable as secrets

## Why
Apps commonly load client certificates from Key Vault by reading the certificate's backing secret (PFX or PEM, with a content type), including through the configuration provider. Such apps work on kvx only if a certificate can be put in and read back that way. It is mostly the secrets API kvx already has, so it stays within "the SDK can't tell the difference" at little cost.

## Notes
- Likely shape: `kvx import` of a PFX/PEM into a secret with the matching content type; no certificates API.
- Pick up when an app needs it.
