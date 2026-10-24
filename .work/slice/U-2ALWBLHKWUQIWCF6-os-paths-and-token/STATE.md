---
phase: execute
parent: U-VFQBGPLYZ3PEJFQG
owner:
  - claude
created: 2026-10-24
success_criteria:
  - with Kvx:DataDir and Kvx:TokenFile empty, kvx serves on Windows and macOS, keeping its store in %LOCALAPPDATA%\kvx or ~/Library/Application Support/kvx and its token in the user's temp dir (Windows) or user cache dir (macOS); Linux keeps $XDG_DATA_HOME/kvx and $XDG_RUNTIME_DIR/kvx
  - a token file written before the machine's last boot is not accepted, and a serving kvx replaces it
  - `kvx describe` reports both defaults on windows-latest and macos-latest in CI (the Describe test passes there)
---

# slice/os-paths-and-token

## Purpose
Without a default token file kvx can't serve on Windows or macOS (no XDG_RUNTIME_DIR), and its data dir default is a Linux convention there.

## Scope
In: the data dir and token file defaults, the reboot rule for the token, the Describe test on every OS.
Out: name resolution, certificate trust, console encoding, Linux-only test assertions (other slices or the milestone's Next).

## Context & constraints
- Milestone Plan "Defaults" (BR accepted 2026-10-24); D-ELGOBWMLQBGY3M42 keeps the token for the login session. Windows and macOS have no per-user directory cleared at logout, so the token there lives until reboot, which is weaker.
- The temp dirs are private to the user: the profile ACL on Windows, a 0700 `/var/folders/…` on macOS.
- Only provable in CI on the GitHub mirror.

## Now
Defaults and the reboot rule pass on all three OSes. The macOS token moves from the temp dir to the user cache dir, because macOS purges temp files unread for about 3 days (confstr(3)) and an idle kvx's clients would lose the token; the reboot rule already does the expiring. Pushing for the macOS read.

## Next
- Read CI: the macOS Describe test with the cache dir; then finalize.

## Evidence
- Local Linux 56/56 with the new Describe-defaults and boot tests.
- The Describe and Run failures on Windows and macOS in run 40045700073 were SSL_CERT_DIR assertions (kvx sets it only on Linux, by design), not the token; the tests now expect that.
- Run 40046335711 (de4a0d7): Describe-defaults, boot, Describe and Run tests pass on Windows (48/56) and macOS (49/56); Linux 56/56. The rest is name resolution, cert trust, Aspire (both) and the import arrow (Windows), all outside this slice. Doctor there reports the temp-dir token path, so kvx finds its default.
- One local run hung over 10 min in the testhost with no child process; the rerun with a hang timeout passed in 15 s. Unexplained; watch for it.

## Harvest
- [ ] learning: Environment.TickCount64 stops during sleep on Linux and macOS (not on Windows), so it can't date a reboot
- [ ] learning: macOS purges files in the per-user temp dir not accessed for about 3 days (confstr(3); Apple calls the details not API), so state that must outlive idleness goes in the user cache dir, which nothing auto-cleans

## Log
- 2026-10-24 initialized
- 2026-10-24 phase initialize → execute
- 2026-10-24 per-OS defaults, token dropped when older than the last boot
- 2026-10-24 CI green for the slice on Windows and macOS; macOS token moved to the user cache dir
