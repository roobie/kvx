---
phase: execute
parent: U-VFQBGPLYZ3PEJFQG
owner:
  - claude
created: 2026-10-23
success_criteria:
  - with Kvx:KeyProtector empty, kvx uses DPAPI on Windows, the login keychain on macOS and systemd-creds on Linux
  - the suite's store tests pass on windows-latest and macos-latest in CI with those protectors, including a key wrapped in one process and unwrapped in another binary
  - a protector named for another OS, or one that cannot work, fails with a KeyProtectorException that `doctor` turns into an OS-specific fix
---

# slice/os-key-protectors

## Purpose
The data key has no OS-held protector on Windows or macOS, so kvx cannot open a store there; every other part of the milestone sits behind this.

## Scope
In: DPAPI and keychain protectors, the per-OS default, doctor's protector fixes, tests that are Linux-only by nature made to say so.
Out: data dir and token locations, name resolution, certificate trust (sibling slices).

## Context & constraints
- Milestone Plan: DPAPI CurrentUser; macOS through own Security.framework calls, item trusted by any app of the user, user interaction off (BR, 2026-10-24).
- No plaintext fallback (IKeyProtector.cs).
- Windows and macOS are only testable in CI (GitHub mirror); each push is one feedback loop.

## Now
macOS: the protector reads across binaries; the last protector-caused failure was a serve test with an empty HOME, which waited on a keychain that did not exist. Serve now checks the certificate before the store opens, and the protector fails at once without a login keychain. Windows: run 40042895190 sat in Test for most of its 30 minutes; waiting for its log.

## Next
- Read the Windows log; tell DPAPI failures apart from sibling-slice ones (token, names, trust), which likely hang rather than fail.
- The rest of the Windows/macOS failures (data dir, token, names, trust) belong to sibling slices; note them, don't fix them here.

## Evidence
- CI run 40039756068 (05ec70a): macOS 44/54; doctor shows "keychain wraps and unwraps a key" in the test host, but the kvx the Aspire AppHost starts gets -25293 reading the item the test host made. Windows cancelled by the next push. Linux 54/54.
- CI run 40042895190 (5730053): macOS, the Aspire-started kvx now opens the store the test host made. Remaining macOS failures: Describe (no default token file), Run (SSL_CERT_DIR asserted as /etc/ssl/certs), TLS and Aspire (UntrustedRoot), all sibling slices; and `Serve_without_a_certificate…` timing out on the keychain with HOME empty (fixed since).

## Harvest
- [ ] process: Windows and macOS code is written blind and proven only by a CI round trip; batch fixes per push

## Log
- 2026-10-23 initialized
- 2026-10-24 execute
- 2026-10-24 keychain protector moved from Security.framework calls to `/usr/bin/security` after the cross-binary refusal in CI
- 2026-10-24 serve checks the certificate before opening the store; keychain fails fast without a login keychain
