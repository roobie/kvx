---
date: 2026-11-11
read_if:
  - reading or writing macOS keychain items from kvx or a test
  - a keychain call returns -25293
refs:
  - src/Kvx.Server/Keys/KeychainKeyProtector.cs#KeychainKeyProtector
---

# L-OFQGNNIOWG5UAGIH A macOS keychain item is bound to its creator's code partition as well as its ACL

## Observation
A file-keychain item admits only callers in the code partition that created it, regardless of an any-app ACL. Unsigned and ad-hoc-signed binaries, which a .NET apphost is, get a partition per build; the `dotnet` muxer has Microsoft's team id. Changing an item's partition list needs the keychain password. Sources: developer.apple.com/forums/thread/845980, Apple's Security `securityd/src/clientid.cpp`, zalando/go-keyring.

## Why it matters
Anything kvx stores in the keychain with its own calls becomes unreadable after a rebuild or update, and the failure looks like an access-control error (-25293). Going through `/usr/bin/security` puts every caller in Apple's partition.
