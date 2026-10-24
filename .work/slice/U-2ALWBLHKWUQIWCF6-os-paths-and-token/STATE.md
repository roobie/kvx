---
phase: execute
parent: U-VFQBGPLYZ3PEJFQG
owner:
  - claude
created: 2026-10-24
success_criteria:
  - with Kvx:DataDir and Kvx:TokenFile empty, kvx serves on Windows and macOS, keeping its store in %LOCALAPPDATA%\kvx or ~/Library/Application Support/kvx and its token in the user's temp dir; Linux keeps $XDG_DATA_HOME/kvx and $XDG_RUNTIME_DIR/kvx
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
Defaults and the reboot rule written; pushing for the Windows and macOS read. The data dir uses .NET's LocalApplicationData, which already is each OS's convention (XDG_DATA_HOME on Linux, Application Support on macOS since .NET 8). The boot time comes from the OS (`/proc/stat`, `kern.boottime`, the Windows tick count), not from now minus uptime, which on Linux and macOS stops during sleep.

## Next
- Read CI: the new Describe test and the boot test on Windows and macOS.

## Evidence
- Local Linux 56/56 with the new Describe-defaults and boot tests.
- The Describe and Run failures on Windows and macOS in run 40045700073 were SSL_CERT_DIR assertions (kvx sets it only on Linux, by design), not the token; the tests now expect that.

## Harvest
- [ ] learning: Environment.TickCount64 stops during sleep on Linux and macOS (not on Windows), so it can't date a reboot
- [ ] learning (to verify before recording): macOS may purge files in the per-user temp dir not accessed for about 3 days; an idle kvx may lose its token file while serving, and clients then can't connect until kvx restarts

## Log
- 2026-10-24 initialized
- 2026-10-24 phase initialize → execute
- 2026-10-24 per-OS defaults, token dropped when older than the last boot
