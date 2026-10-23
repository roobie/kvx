---
status: accepted
date: 2026-10-23
read_if:
  - a tool needs kvx's urls, domain, token file or certificate dir
  - changing the output of kvx describe
refs:
  - src/Kvx.Server/Cli/DescribeCommand.cs#DescribeCommand
  - src/Kvx.Aspire.Hosting/KvxDescription.cs
---

# D-CHUCZKSUCNIWSIB2 Tools learn kvx's endpoints from kvx describe, not from its config files or assumed ports

## Context
The hosting package must know where kvx serves, its domain, token file and certificate dir before kvx runs. kvx's effective settings come from layered configuration: the installed appsettings, environment and per-command overrides.

## Decision
`kvx describe` prints the effective settings as JSON without connecting to anything; tools ask it, passing the same settings they will pass to `serve`.

## Why
- Reading appsettings.json from a tool duplicates kvx's configuration layering and drifts as soon as it changes.
- Assuming default ports breaks every user who overrides one.
- kvx resolving its own configuration is the only source that cannot disagree with the kvx that then serves.

## Consequences
- The JSON is a contract with external tools: add fields freely, rename or remove them only deliberately.
- A kvx too old to have `describe` cannot be used by the hosting package; it says so.
