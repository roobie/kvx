---
date: 2026-11-11
read_if:
  - calling the macOS keychain from a server or a test
  - kvx hangs at startup on macOS
refs:
  - src/Kvx.Server/Keys/KeychainKeyProtector.cs#Timeout
---

# L-R6WT5TQSYKXZJBSB A macOS keychain read that needs a prompt blocks without UI instead of failing

## Observation
On macOS 26.6 (spike run 40037860344), a read from a second binary and a read after `security lock-keychain` both hung until killed at 2 minutes. Neither returned an error. A prompt was presumably waiting where no one could see it.

## Why it matters
Unless user interaction is turned off, which `security` cannot do, every keychain call needs a timeout of its own, or kvx and its tests hang instead of failing.
