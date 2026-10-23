---
status: accepted
date: 2026-10-23
read_if:
  - changing how apps authenticate to kvx
  - considering az CLI or authority-host based auth
refs:
  - src/Kvx.Server/Auth/IdentityEndpoint.cs#IdentityEndpoint
---

# D-KKZWEBYCP6FAKSHB DefaultAzureCredential via the App Service managed-identity protocol

## Context
Nearly every Azure .NET app builds its credential with `new DefaultAzureCredential()`. Wire compatibility means nothing if the developer has to change that line for local development.

## Decision
kvx answers the App Service managed-identity protocol, selected purely by the IDENTITY_ENDPOINT and IDENTITY_HEADER environment variables.

## Why
These options were checked against the Azure.Identity 1.21 / Azure.Core 1.63 / MSAL 4.90 source on 2026-10-23:
- A custom AZURE_AUTHORITY_HOST: MSAL instance discovery still calls login.microsoftonline.com, and the host must be https.
- Shimming the az CLI: it needs PATH changes, comes later in the chain, and is fragile across az versions.
- The App Service protocol: it allows plain http on loopback, skips the IMDS probe, ignores the tenant, and needs no code change.

## Consequences
- The identity endpoint must answer errors with 400 or 401 only. 404, 408, 429 and 5xx are retried by MSAL and Azure.Core, so the app would stall instead of failing clearly.
- It must never issue a claims challenge, because that bypasses the token cache.
- A stray AZURE_CLIENT_ID/secret or a workload-identity setup comes earlier in the chain and wins over kvx.
