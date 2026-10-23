---
date: 2026-10-23
read_if:
  - an app needs kvx and real Azure services at once
  - choosing status codes for the identity endpoint
refs:
  - src/Kvx.Server/Auth/IdentityEndpoint.cs#Token
  - README.md
---

# L-GPX6ZOUCE2IAT4SR DefaultAzureCredential latches the first working credential

## Observation
Once a credential in the chain succeeds, DefaultAzureCredential uses only that credential from then on. MSAL also caches managed-identity tokens per process. So an app pointed at kvx asks kvx for every resource it touches, such as Storage or Service Bus.

The source was read on 2026-10-23 (Azure.Core 1.63, MSAL 4.90). Other observations from that reading:
- MSAL keys that cache by the requested scope, not the response, so tokens from different issuers coexist in one app.
- A reported lifetime over 2h makes MSAL refresh at half-life.
- Shorter lifetimes are served until 5 minutes before expiry.

## Why it matters
kvx is effectively the token broker for the whole app, not just for Key Vault. That is why passthrough exists (D-BGGXSS3A5S3YOP24), and why every identity-endpoint failure surfaces in non-Key Vault clients. This stops being true if Azure.Identity adds per-scope chain selection.
