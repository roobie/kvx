---
date: 2026-10-23
read_if:
  - an app needs kvx and real Azure services at once
  - choosing status codes for the identity endpoint
refs:
  - src/Kvx.Server/Auth/IdentityEndpoint.cs#Token
---

# L-GPX6ZOUCE2IAT4SR DefaultAzureCredential latches the first working credential

## Observation
Once a credential in the chain succeeds, DefaultAzureCredential uses only that credential from then on. MSAL also caches managed-identity tokens per process. So an app pointed at kvx asks kvx for every resource it touches, such as Storage or Service Bus, and kvx's resource check (refs) is the only place that mistake surfaces.

The source was read on 2026-10-23 (Azure.Core 1.63, MSAL 4.90). Other observations from that reading:
- A reported lifetime over 2h makes MSAL refresh at half-life.
- Shorter lifetimes are served until 5 minutes before expiry.

## Why it matters
An app that needs kvx and real Azure services together cannot use a single DefaultAzureCredential. It needs per-client credentials or a future kvx feature. This stops being true if Azure.Identity adds per-scope chain selection.
