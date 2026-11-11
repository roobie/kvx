---
status: accepted
date: 2026-11-11
read_if:
  - reviewing kvx's security boundary on macOS
  - changing the keychain item's access control
refs:
  - src/Kvx.Server/Keys/KeychainKeyProtector.cs#Timeout
  - src/Kvx.Server/Cli/DoctorCommand.cs#KeyProtector
---

# D-6D6T5BRU2K4YDTPV The macOS keychain item trusts any app of the user, and a locked keychain fails instead of prompting

## Context
A keychain item can trust only its creating binary or any application of the user. The spike showed a read from another binary, which every tool update is, blocks on an invisible prompt instead of failing. BR chose this on 2026-10-24.

## Decision
The item trusts any application of the user. A call that would need a prompt, such as one against a locked keychain, fails after a timeout, and `kvx doctor` names `security unlock-keychain`.

## Why
- Per-app trust would buy nothing: DPAPI on Windows and `systemd-creds --user` on Linux already let any process of the user unwrap the key, so this matches the other OSes.
- A server that hangs on a prompt no one can see is worse than one that refuses to start with a fix.

## Consequences
Any process running as the user can read the key-encryption key, as on the other OSes. The store's protection is per user, not per program.
