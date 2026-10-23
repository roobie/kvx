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
DPAPI and keychain protectors, the per-OS default and doctor's fixes are written; Linux suite passes (54/54). Pushing for the first Windows and macOS read.

## Next
- Read CI; fix what the other OSes surface. The keychain code has never run: expect P/Invoke or ACL surprises.
- The rest of the Windows/macOS failures (data dir, token, names, trust) belong to sibling slices; note them, don't fix them here.

## Evidence

## Harvest
- [ ] process: Windows and macOS code is written blind and proven only by a CI round trip; batch fixes per push

## Log
- 2026-10-23 initialized
- 2026-10-24 execute
