---
date: 2026-10-23
read_if:
  - kvx vault names do not resolve
  - setting up a dev machine or CI runner for kvx
refs:
  - tests/Kvx.Tests/CaddyEndToEndTests.cs#Unmodified_app_round_trips_through_caddy
---

# L-NRQPHNQCOYNBPSS7 *.localhost resolution depends on the host's NSS setup

## Observation
.NET on Linux resolves names through glibc, which does not treat `*.localhost` as loopback by itself. It works only with the `myhostname` NSS module or systemd-resolved. Stock Debian 13 without resolved has neither (found 2026-10-23; fixed on BR's machine with libnss-myhostname).

## Why it matters
Every kvx user and CI runner needs one of the two, or vault URIs fail with "Name or service not known" before kvx is ever reached. The E2E test asserts resolution up front so the failure names the cause. It would stop mattering if kvx moved away from `*.localhost` vault hosts.
