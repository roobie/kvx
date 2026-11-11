---
status: accepted
date: 2026-11-11
read_if:
  - changing the macOS key protector
  - a keychain read fails or hangs on macOS
refs:
  - src/Kvx.Server/Keys/KeychainKeyProtector.cs#KeychainKeyProtector
---

# D-SW57A6CVT7S2LPRQ The macOS key protector goes through /usr/bin/security with the key on stdin

## Context
The macOS protector keeps a key-encryption key as a generic password in the file-based login keychain. It could be reached through MSAL Extensions, kvx's own Security.framework calls, or Apple's `security` tool. An earlier plan chose own Security.framework calls.

## Decision
kvx creates and reads the item only by running `security`, passing the key on stdin.

## Why
- **Own Security.framework calls:** CI showed an item made by one binary refused to another (-25293) despite an any-app ACL, because items are bound to the creator's code partition and kvx's ad-hoc-signed apphost gets a new partition with every build. Every tool update would lock the user out.
- **MSAL Extensions:** same keychain and same prompts, plus a dependency.
- **`security`:** every caller is in Apple's tool partition, so any kvx build reads the item.
- **stdin:** the key never appears on a command line, where other processes could read it.

## Consequences
kvx depends on `/usr/bin/security` being present and its behaviour stable. `security` cannot turn user interaction off, so each call carries a timeout (D-6D6T5BRU2K4YDTPV).
