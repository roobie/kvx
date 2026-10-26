---
status: accepted
date: 2026-10-26
read_if:
  - adding a command or changing its arguments
  - changing how --help is parsed
refs:
  - src/Kvx.Server/Cli/Help.cs#Commands
  - src/Kvx.Server/Cli/Help.cs#Asked
---

# D-SN5Z7VNGRYGAA4GL Command synopses live in Help.Commands beside dispatch; --help only directly after the verb

## Context
Each command had its own usage text, and the overview had another copy of it. Adding per-command `--help` would have made a third. `kvx run` passes its arguments to an app, and that app may take `--help` itself.

## Decision
Each command's synopsis is written once, in the list `kvx help` prints. The overview, `<command> --help` and that command's usage error all print it. `--help` counts only as the first argument after the command.

## Why
- One copy cannot drift. Commands are code, so their synopses belong in code next to the dispatch, not in docs/, which holds goals.
- Accepting `--help` anywhere would take it away from the app under `kvx run app --help`, and from any setting value spelled that way.

## Consequences
- A new command needs a line in the list, or `kvx help` won't show it.
- `kvx run --setting=x --help` is not help. It is a run of a program named `--help`.
