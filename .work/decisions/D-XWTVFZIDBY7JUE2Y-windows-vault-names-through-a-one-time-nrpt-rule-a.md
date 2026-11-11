---
status: accepted
date: 2026-11-11
read_if:
  - changing how vault names resolve on Windows
  - a Windows user's vault names do not resolve
refs:
  - src/Kvx.Server/Names/LoopbackDns.cs#LoopbackDns
  - src/Kvx.Server/Cli/DoctorCommand.cs#NameResolution
---

# D-XWTVFZIDBY7JUE2Y Windows vault names through a one-time NRPT rule and kvx's loopback DNS responder

## Context
Vault hosts are `<vault>.dev.localhost` (D-LLPISGPW4SP4BB4K), and stock Windows does not resolve `*.localhost` subdomains for .NET 8 or 10. Most of kvx's audience is on Windows. BR chose this on 2026-10-23.

## Decision
A one-time elevated NRPT rule sends the names to a DNS responder inside kvx, which answers every name under the domain with loopback while kvx runs. `kvx doctor` detects a missing rule and prints the setup command.

## Why
- **Hosts line per vault:** every new vault would need an elevated edit, so vaults would no longer appear on first write as they do elsewhere. It stays as a fallback (D-PBTUVD4LI4H26RGV).
- **Waiting for .NET 11:** its `*.localhost` fallback is not in .NET 10, and most of the audience's apps are on .NET 8 or 10.
- **A public loopback domain:** also rejected; the reason was not written down at the time.
- **NRPT plus responder:** one admin step, like `dotnet dev-certs https --trust`; the spike showed a standard user binds 127.0.0.1:53 and the rule applies at once.

## Consequences
- Names resolve only while some kvx runs; a stopped kvx makes them fail again (L-GXSEOONO6VE5P4EJ for the hand-over between instances).
- Port 53 taken by another program (ICS) or Group Policy NRPT rules leave the hosts file as the only way.
