---
status: accepted
date: 2026-10-23
read_if:
  - changing backup or restore
  - moving secrets between machines
refs:
  - src/Kvx.Server/Storage/SealedStore.cs#SealBackup
---

# D-S5MKFASWUWOQWFH7 Backup blobs are sealed with the store's data key

## Context
Backup blobs leave kvx: apps and scripts write them wherever they like. The project's promise is no plaintext secrets at rest, and Azure documents backups as opaque and restorable only within their own boundary.

## Decision
A backup is the secret's versions, sealed with the store's data key under a backup-specific AAD, so a store file and a backup can't be swapped for one another.

## Why
- Plaintext or a merely encoded blob would break the project's core promise the moment someone saves a backup.
- A separate backup key or a passphrase would add key management for no gain in the same-user threat model.
- Using the data key mirrors Azure's "restore only within the boundary" rule, with the boundary being this machine's keyring.

## Consequences
- Backups don't move between machines, or survive losing the keyring entry. Moving secrets elsewhere needs a separate, explicit export path, which the project design already lists as a risk.
- Rotating the data key would orphan old backups.
