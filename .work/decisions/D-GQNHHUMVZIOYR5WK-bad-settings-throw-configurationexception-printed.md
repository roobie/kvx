---
status: accepted
date: 2026-10-26
read_if:
  - validating a setting
refs:
  - src/Kvx.Server/ConfigurationException.cs#ConfigurationException
  - src/Kvx.Server/Cli/KvxCli.cs
---

# D-GQNHHUMVZIOYR5WK Bad settings throw ConfigurationException, printed as a message

## Context
`ServicePrincipal` listed with a developer login must stop kvx from starting. A plain exception at startup reached the user as a stack trace, as a bad `Kvx:Dns` already did.

## Decision
Invalid settings throw ConfigurationException; the CLI prints its message and exits 1.

## Why
- The user caused it and can fix it from the message; a stack trace hides the message and suggests a kvx bug.
- A distinct type keeps real bugs (other exceptions) loud, unlike catching InvalidOperationException broadly.

## Consequences
- New setting checks throw ConfigurationException and phrase the message as what to change.
