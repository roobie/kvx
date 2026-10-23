---
status: accepted
date: 2026-10-23
read_if:
  - changing kvx's listeners or urls
  - making the identity endpoint https
refs:
  - src/Kvx.Server/Cli/KvxClient.cs#ListenEndpoint
---

# D-WHOJPIDH674TO3S7 Identity endpoint and kvx commands use a plain-http loopback listener; only vaults are https

## Context
Once kvx serves its own TLS, every client of kvx would need to trust the certificate: the managed-identity endpoint that DefaultAzureCredential calls, and kvx's own run/env/import commands.

## Decision
kvx listens on two urls: https for vaults, and plain http on loopback for the identity endpoint and kvx's commands, which find it as the first `http://` url.

## Why
Azure's own IDENTITY_ENDPOINT is plain http on a local address, and MSAL expects that, so http there is what the protocol already assumes. It keeps run/env/import and token issuance working when certificate trust is broken, so they can report the problem instead of failing on it. The alternative, https everywhere, would make the commands depend on the trust they may be trying to diagnose.

## Consequences
The session token travels in clear over loopback, the same exposure as Azure's endpoint; the listener must stay bound to loopback. Settings that override `urls` must keep an http url, or the commands refuse with a message saying so.
