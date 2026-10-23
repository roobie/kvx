---
created: 2026-10-23
---

# B-UW2PA4KWMLEWUDVQ kvx env / kvx run to inject identity variables

## Why
Developers currently have to set IDENTITY_ENDPOINT, and copy IDENTITY_HEADER from the token file, by hand. That is friction, and an easy place to leak the token into shell history.

## Notes
- The command should strip stray AZURE_* credential variables and AZURE_TOKEN_CREDENTIALS (unless it is `prod` or `managedidentitycredential`) from the child environment, as the E2E test does, because those variables take precedence over kvx in the chain.
- Aspire wiring is the same idea for AppHost-launched projects.
