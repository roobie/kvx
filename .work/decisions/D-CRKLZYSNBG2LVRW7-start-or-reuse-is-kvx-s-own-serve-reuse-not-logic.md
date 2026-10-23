---
status: accepted
date: 2026-10-23
read_if:
  - changing how an AppHost or other tool starts kvx
  - changing kvx serve's handling of a store held by another kvx
refs:
  - src/Kvx.Server/Cli/KvxCli.cs#ReuseFlag
  - src/Kvx.Aspire.Hosting/KvxHostingExtensions.cs#AddOrGetKvx
---

# D-CRKLZYSNBG2LVRW7 Start-or-reuse is kvx's own serve --reuse, not logic in the hosting package

## Context
BR chose that an AppHost uses the developer's own kvx if it serves, and otherwise starts one. Only one kvx may serve a store (D-ZSYSODY6DTOL5U54).

## Decision
The AppHost always starts `kvx serve --reuse`. That kvx serves if the store is free; otherwise it waits while the holder serves, and takes over when it stops.

## Why
- Deciding in the hosting package means probing before start, which races, and choosing between two resource shapes (a process or an external reference), which the dashboard and wait wiring would both have to handle.
- With one always-started process, DCP keeps owning its lifetime and logs, and resources always wait on the same resource.
- The decision is made by the one party that can make it reliably: the process that tries the lock.
- The developer's kvx stopping mid-run no longer breaks the AppHost.

## Consequences
- `--reuse` refuses a holder that answers with other settings, so an AppHost with mismatched `Kvx:Settings` fails loudly instead of using the wrong kvx.
- Any other launcher (an IDE, a script) gets the same behaviour for free.
