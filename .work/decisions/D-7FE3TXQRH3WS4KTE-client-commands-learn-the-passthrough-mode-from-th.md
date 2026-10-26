---
status: accepted
date: 2026-10-26
read_if:
  - a client command needs a server setting
refs:
  - src/Kvx.Server/Api/PrincipalApi.cs#PrincipalState
  - src/Kvx.Server/Cli/DoctorCommand.cs#Principal
---

# D-7FE3TXQRH3WS4KTE Client commands learn the passthrough mode from the serving kvx

## Context
BR's live check started `kvx serve --Kvx:Passthrough=ServicePrincipal`, then ran `kvx principal set` and `kvx doctor` without the flag. They read the mode from their own settings, so they said the principal was unused and skipped its check, although the serving kvx used it.

## Decision
The serving kvx reports whether its passthrough uses the principal, and client commands go by that answer.

## Why
- Requiring every client command to repeat the server's overrides is the documented rule for `urls` and `Kvx:TokenFile`, but those settings are needed to find the server at all. The mode is not, so asking the server removes a way to be wrong at no cost.
- Same spirit as tools learning endpoints from `kvx describe` (D-CHUCZKSUCNIWSIB2): the running kvx is the source of truth about itself.

## Consequences
- Settings that change server behaviour, rather than how to reach it, should be reported by the server when a client needs them.
