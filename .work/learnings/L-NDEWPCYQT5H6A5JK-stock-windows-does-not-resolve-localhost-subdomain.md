---
date: 2026-11-11
read_if:
  - kvx vault names do not resolve on Windows
  - choosing a vault host scheme
refs:
  - src/Kvx.Server/Names/LoopbackDns.cs#LoopbackDns
---

# L-NDEWPCYQT5H6A5JK Stock Windows does not resolve *.localhost subdomains for .NET

## Observation
On windows-latest (Server 2025), .NET 10's `Dns` fails for `secrets.dev.localhost` with "No such host is known" (spike run 40007803218). macOS and systemd-resolved Linux resolve the same names to loopback. curl resolves `*.localhost` itself on every OS, so a curl probe passing says nothing about .NET apps. .NET 11 adds a `*.localhost` fallback (dotnet/runtime#123076); .NET 8 and 10 do not have it.

## Why it matters
Without extra setup, no vault is reachable from a .NET app on Windows, which is most of kvx's audience. Name resolution has to be tested through .NET, not curl. Related: L-NRQPHNQCOYNBPSS7 (Linux depends on NSS setup).
