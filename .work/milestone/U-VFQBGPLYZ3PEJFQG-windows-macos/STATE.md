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

## Plan
- Windows names: BR chose (2026-10-23) a one-time elevated setup, like `dev-certs --trust`: a Windows NRPT rule sends `.dev.localhost` to a loopback DNS responder in kvx that answers every name under the domain with loopback. Vaults then appear on first write as elsewhere. `kvx doctor` detects the missing rule and prints the setup command. Waiting for .NET 11 was rejected: its `*.localhost` fallback (dotnet/runtime#123076) is not in .NET 10, and most of the audience's apps are on .NET 8/10.
- macOS protector: own Security.framework calls (file-based login keychain, generic password holding a key-encryption key), not MSAL Extensions: same keychain, same prompts, without the MSAL dependency. An unsigned tool is trusted by code hash, so expect an "Always Allow" prompt after a tool update; a locked keychain (SSH) fails with errSecInteractionNotAllowed, which `doctor` explains.
- Windows protector: DPAPI, CurrentUser. Caveat for the README: an admin password reset of a local account loses the store.
- Proposed defaults (to confirm with BR before code): data dir `%LOCALAPPDATA%\kvx` and `~/Library/Application Support/kvx`; token in the per-user temp dir, replaced when the machine has rebooted since it was written (weaker than Linux, which also drops it at logout).
- CI trust: Windows imports the test certificate into LocalMachine\Root, macOS adds it with `sudo security add-trusted-cert` to the System keychain; directory trust (SSL_CERT_DIR) exists only on Linux. Linux CI needs systemd ≥ 256 (a newer runner image, else a container).

## Now
Spike run read; Windows naming decided; plan written.

## Next
- Spike on the runners, before building on them:
  - Windows: can an unelevated kvx answer DNS on 127.0.0.1:53, and does an NRPT rule for `.dev.localhost` make .NET 10 resolve vault names through it? Which Windows editions have NRPT?
  - macOS: a Security.framework generic password round trip from the test process.
  - Linux: a runner image or container with systemd ≥ 256 and a user manager for `systemd-creds --user`.
- A push mirror from gitea to GitHub (BR), until then pushed by hand with `GH_REPO_TOKEN_KVX`.
- Protectors, then per-OS paths and token, then `doctor` and README.

## Evidence
- Spike run 40007803218 (2026-10-23), 3/53 passing on every OS, all other tests stopped by the key protector:
  - Windows Server (windows-latest): `secrets.dev.localhost` does **not** resolve through .NET's Dns (probed from pwsh, so the runtime may predate .NET 10: "No such host is known"); curl connects, since it resolves `*.localhost` itself.
  - macOS (macos-latest): resolves to `::1, 127.0.0.1` through .NET and curl.
  - Ubuntu (ubuntu-latest = 24.04): resolves, but systemd is 255, whose `systemd-creds` has no `--user`; kvx's Linux protector needs 256.

## Harvest
- [ ] learning: stock Windows does not resolve `*.localhost` subdomains for .NET, while macOS and systemd-resolved Linux do; curl resolves them itself everywhere
- [ ] decision: Windows vault names through a one-time NRPT rule and kvx's loopback DNS responder, not hosts entries per vault, a public loopback domain, or waiting for .NET 11
- [ ] decision: own Security.framework calls for the macOS protector rather than MSAL Extensions
- [ ] learning: GitHub's ubuntu-24.04 runner has systemd 255, too old for `systemd-creds --user`

## Log
- 2026-10-23 initialized; BR chose a GitHub Actions mirror for Windows and macOS proof
- 2026-10-23 spike run: Windows does not resolve `*.localhost` for .NET; BR chose a one-time NRPT rule
