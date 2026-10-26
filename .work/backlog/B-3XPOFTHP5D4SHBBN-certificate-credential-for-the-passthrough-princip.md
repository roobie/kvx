---
created: 2026-10-26
---

# B-3XPOFTHP5D4SHBBN Certificate credential for the passthrough principal

## Why
Some tenants forbid client secrets for app registrations, which rules out principal passthrough (D-WHMYBXHZAGDFWJNC) there. A certificate also avoids a shared-secret string on the dev machine.

## Notes
The OS certificate store on Windows and macOS would keep the private key out of kvx entirely; Linux has no common store, so the sealed store would hold it there. `kvx principal set` would take a certificate reference instead of a secret.
