---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - an unmodified SecretClient sets a secret and gets it back (latest and by version) through Caddy at https://<vault>.kvx.localhost, with no client options beyond the vault URI and a credential
  - the SDK's challenge-resource verification runs and passes (DisableChallengeResourceVerification is not set)
  - after a restart of kvx the secret is still readable, and a test scanning the data directory finds neither the secret value nor its name in plaintext
  - requests without the current dev token get 401; a token from a previous kvx run is rejected
---

# milestone/walking-skeleton

## Purpose
Prove the riskiest assumptions end to end before building the API out: that the stock SDK accepts a `*.kvx.localhost` vault, and that keyring-wrapped whole-store encryption holds up across restarts.

## Scope
In: PUT and GET secret (latest and by version); challenge auth with ephemeral dev tokens; sealed store with the data key wrapped by systemd-creds; Caddy in front; Linux only.
Out: list, delete, recover, purge, backup, update attributes; Windows/macOS key protectors; dotnet tool packaging; DefaultAzureCredential support.

## Context & constraints
- Design: see the parent project's DESIGN.md "Settled" section.
- Tooling via mise.local.toml: dotnet 10, caddy 2.11.7.
- glibc here does not resolve `*.kvx.localhost` (getent fails); whether .NET resolves it by itself is unknown.

## Now
All criteria are shown except one gap: `*.kvx.localhost` does not resolve on stock Debian (no nss-myhostname, no systemd-resolved), so a real app here cannot reach the vault without a machine change. Waiting on BR to choose the resolution approach before finalizing.

## Next
- Decide name resolution (nss-myhostname / systemd-resolved / hosts entries / another domain), then verify an unmodified app with only `caddy trust` and that change.
- Finalize: harvest, then close.

## Evidence
- `dotnet test`: 7/7 pass in-process (stock SecretClient over TestServer, real systemd-creds):
  set/get latest and by version; 404 SecretNotFound; restart keeps data and a scan of the data dir finds neither name nor value; stale token → 401; bare request → Key Vault challenge; SDK rejects a non-matching host with its DisableChallengeResourceVerification error, so verification is active and passes for *.kvx.localhost.
- `dotnet test`: 8/8 with the E2E test: kvx on a loopback port, Caddy (dev/Caddyfile, `tls internal`) in front, stock SecretClient over real TLS sets and gets latest and by version; the id carries the vault host and port, so Caddy preserves Host. The test trusts only that run's Caddy root (standing in for `caddy trust`) and pins connections to loopback (standing in for *.localhost resolution); no Caddy processes or token files are left behind.

## Harvest
- [ ] learning: Azure SDK challenge verification is `request host EndsWith("." + resource host)`; the tenant is the first path segment of the authorization URI
- [ ] learning: Azure.Core refuses bearer tokens over plain http, so SDK tests need https URIs (TestServer accepts them as-is)
- [ ] learning: .NET resolves names through glibc, so *.localhost works only with nss-myhostname or systemd-resolved; stock Debian has neither
- [ ] backlog: a `kvx doctor` that checks name resolution, Caddy CA trust and the key protector, and says how to fix each

## Log
- 2026-10-23 initialized
- 2026-10-23 phase initialize → execute
- 2026-10-23 in-process slice green (7 tests), committed 05c878a
- 2026-10-23 E2E through Caddy green; found *.localhost resolution gap on stock Debian
