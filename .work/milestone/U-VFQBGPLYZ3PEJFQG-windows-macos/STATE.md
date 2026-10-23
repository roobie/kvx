---
phase: initialize
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - on Windows the data key is wrapped with DPAPI for the current user, and on macOS by a key held in the user's login Keychain; each shown by a passing test on that OS in CI
  - with no explicit Kvx:KeyProtector, kvx picks the OS's protector; a protector that cannot work (locked Keychain, missing systemd-creds) refuses to start and `kvx doctor` says how to fix it on that OS
  - the full test suite, including the unmodified-app TLS round trip and the Aspire tests, passes on Linux, Windows and macOS in GitHub Actions on every push to the mirror
  - on Windows and macOS the data dir and the session token live in the OS's per-user locations, and the token never outlives a reboot
  - vault names resolve on a stock Windows and macOS machine, or `kvx doctor` detects that they don't and gives a fix that works there
  - the README's install and serve sections cover Windows and macOS
---

# milestone/windows-macos

## Purpose
kvx's audience is enterprise .NET developers, mostly on Windows (the reason the project chose .NET). Until it runs there and on macOS, the project's core promise reaches a minority of them.

## Scope
In: DPAPI and Keychain key protectors; per-OS defaults (protector, data dir, token file); vault name resolution and certificate trust per OS; `kvx doctor` fixes per OS; CI on all three OSes through a GitHub mirror; README.
Out: signing or notarising the tool; installers beyond the dotnet tool; containers; Windows services or launchd agents to run kvx at login (backlog if wanted).

## Context & constraints
- BR chose (2026-10-23) to prove the other OSes in GitHub Actions on a mirror of the gitea repo, so the parent's criterion "a passing test on that OS in CI" holds as written. The repo has no CI yet. `gh` is not logged in on BR's machine.
- The key protector is one seam (`IKeyProtector`); there is no plaintext fallback, and that must stay true.
- Tokens live for the login session (D-ELGOBWMLQBGY3M42), resting on Linux's tmpfs runtime dir. Windows has no equivalent and macOS's `$TMPDIR` survives logout, so the guarantee there needs a fresh decision.
- Vault hosts are `<vault>.dev.localhost` (D-LLPISGPW4SP4BB4K); resolution already differs by NSS setup on Linux (L-NRQPHNQCOYNBPSS7). Whether stock Windows and macOS resolve `*.localhost` subdomains is unknown.
- The project design suggests MSAL Extensions' storage (DPAPI, Keychain) where it fits. Shelling out to `security` would put the key on a command line.
- Moving the data dir later strands existing stores, so it is settled before the first non-Linux user.

## Now
Spike: the first CI run on the GitHub mirror (github.com/roobie/kvx, public, created by BR) probes name resolution and systemd-creds per OS, and runs the unchanged suite on all three, to show what breaks where.

## Next
- Read the spike run; turn each failure into scope.
- Spike the Keychain: does MSAL Extensions' storage hold a key without a prompt per kvx upgrade, and fail clearly when the Keychain is locked (SSH)?
- A push mirror from gitea to GitHub (BR), until then pushed by hand with `GH_REPO_TOKEN_KVX`.
- Protectors, then per-OS paths and token, then `doctor` and README.

## Evidence

## Harvest

## Log
- 2026-10-23 initialized; BR chose a GitHub Actions mirror for Windows and macOS proof
