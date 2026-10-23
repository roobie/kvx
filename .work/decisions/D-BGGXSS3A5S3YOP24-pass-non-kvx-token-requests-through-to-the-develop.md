---
status: accepted
date: 2026-10-23
read_if:
  - changing what the identity endpoint does for other resources
  - reviewing kvx's security boundary
refs:
  - src/Kvx.Server/Auth/Upstream.cs#Upstream
  - README.md
---

# D-BGGXSS3A5S3YOP24 Pass non-kvx token requests through to the developer login, on by default

## Context
An app on kvx via DefaultAzureCredential asks kvx for every Azure token (L-GPX6ZOUCE2IAT4SR). Refusing anything that wasn't Key Vault cut such apps off from every other Azure service. That excluded most real apps from the no-code-change pitch, which BR flagged as a blocker on 2026-10-23.

## Decision
For any resource other than kvx's, kvx serves the token from the developer's own login. The login is an explicit, configurable list of developer credentials, and this is on by default.

## Why
- Refusing and documenting it: honest, but it leaves the main audience out.
- Per-client credentials in the app: works, but it is the code change kvx exists to avoid.
- Passthrough: apps behave as they would run locally without kvx.
  - It adds no reach. The caller must hold the session token, which only the same user can read, and that user could run `az account get-access-token` directly.
  - A browser cannot send the custom header without a CORS preflight, which kvx does not answer.
- The upstream is an explicit list rather than DefaultAzureCredential, so a kvx process started with IDENTITY_ENDPOINT set cannot end up asking itself for a token.

## Consequences
- Apps act as the developer, not as their managed identity, so local access can differ from production. The README states this up front.
- kvx needs the developer's CLIs on its own PATH and their login in its session, which matters for running kvx as a service.
- Upstream failures must stay 400 so clients do not retry them.
- `Kvx:Passthrough` empty is the opt-out.
