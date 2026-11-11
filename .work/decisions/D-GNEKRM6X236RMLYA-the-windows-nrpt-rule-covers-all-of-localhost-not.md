---
status: accepted
date: 2026-11-11
read_if:
  - changing the Windows name-resolution setup
  - a non-kvx *.localhost name stops resolving on Windows
refs:
  - src/Kvx.Server/Cli/DoctorCommand.cs#NameResolution
  - .github/workflows/ci.yml
---

# D-GNEKRM6X236RMLYA The Windows NRPT rule covers all of .localhost, not only the vault domain

## Context
The NRPT rule can name `.dev.localhost` only or the whole `.localhost` namespace. Tests use `kvxtest.localhost`, and `Kvx:Domain` allows other vault domains. BR chose this on 2026-10-24.

## Decision
The rule kvx tells users to add, and CI adds, is for `.localhost`.

## Why
- RFC 6761 already asks resolvers to send all of `.localhost` to loopback, so the rule only makes Windows do what the standard expects.
- One rule covers the tests' domain and any custom vault domain without a second elevated step.
- The spike showed plain `localhost` keeps resolving under a `.localhost` rule.

## Consequences
While the rule exists, every `*.localhost` name on the machine resolves only while kvx runs.
