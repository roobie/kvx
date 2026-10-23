---
status: accepted
date: 2026-10-23
read_if:
  - changing the Aspire hosting integration's resource model
  - publish mode or the client integration behaves differently with kvx
refs:
  - src/Kvx.Aspire.Hosting/KvxHostingExtensions.cs#RunAsKvx
---

# D-P2LDBKFIWXL6NNE3 RunAsKvx redirects Aspire's own Azure Key Vault resource instead of adding a resource type

## Context
The point of the integration is no code change: an app using Aspire's Key Vault client integration gets kvx locally and a real Key Vault when published. The community Key Vault emulator's `RunAsEmulator` is prior art.

## Decision
`RunAsKvx` marks Aspire's `AzureKeyVaultResource` as emulated and points its `vaultUri` output at kvx, in run mode only.

## Why
- A separate kvx resource type would need its own connection-string shape and its own publish story, and apps would reference something different locally than in production.
- Redirecting keeps the vault the same resource in both modes: `WithReference`, the connection string and the client integration are Aspire's own, and publish provisions exactly what it would without kvx.

## Consequences
- The integration depends on Aspire internals that are public but not designed as extension points (the emulator annotation, outputs, the provisioning task, see L-MSSJHFGW32EV6BG3); Aspire upgrades need the Aspire tests to pass.
- Publish mode being untouched is a tested property, not an assumption.
