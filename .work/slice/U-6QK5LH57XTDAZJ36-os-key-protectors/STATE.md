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
Both protectors work in CI. The Windows hang was not DPAPI: a "no dev cert" test served forever because on Windows (and macOS) the dev cert sits in the OS store, which an empty HOME does not hide. Those tests now run only on Linux, and the test helper kills a command that does not exit within 60 s. Pushing for a Windows run that finishes.

## Next
- A finished Windows run whose remaining failures are all sibling-slice ones, then finalize.
- The rest of the Windows/macOS failures (data dir, token, names, trust) belong to sibling slices; note them, don't fix them here.

## Evidence
- CI run 40039756068 (05ec70a): macOS 44/54; doctor shows "keychain wraps and unwraps a key" in the test host, but the kvx the Aspire AppHost starts gets -25293 reading the item the test host made. Windows cancelled by the next push. Linux 54/54.
- CI run 40042895190 (5730053): macOS, the Aspire-started kvx now opens the store the test host made. Remaining macOS failures: Describe (no default token file), Run (SSL_CERT_DIR asserted as /etc/ssl/certs), TLS and Aspire (UntrustedRoot), all sibling slices; and `Serve_without_a_certificate…` timing out on the keychain with HOME empty (fixed since).
- Run 40042895190, Windows: doctor shows "dpapi wraps and unwraps a key"; conformance, restart and no-plaintext store tests pass; cancelled at 30 min, hung in `Serve_without_a_certificate…` (a dev cert in CurrentUser\My, so kvx served).
- Run 40044774152 (07a96a9), macOS 45/54: "keychain wraps and unwraps a key" in every process, `Serve_without_a_certificate…` passes; the 9 failures are trust (UntrustedRoot, doctor certificate), the token location (Describe) and SSL_CERT_DIR (Run), all sibling slices.

## Harvest
- [ ] process: Windows and macOS code is written blind and proven only by a CI round trip; batch fixes per push
- [ ] process: a hanging test used up a whole 30-minute Windows job with no name; hang timeouts in the helper and on `dotnet test` turn that into a named failure
- [ ] learning: the ASP.NET dev cert is under HOME only on Linux; on Windows and macOS it is in the OS store, so "a user without a dev cert" can't be simulated there

## Log
- 2026-10-23 initialized
- 2026-10-24 execute
- 2026-10-24 keychain protector moved from Security.framework calls to `/usr/bin/security` after the cross-binary refusal in CI
- 2026-10-24 serve checks the certificate before opening the store; keychain fails fast without a login keychain
- 2026-10-24 no-dev-cert tests Linux-only; CLI test helper and CI step get hang timeouts
