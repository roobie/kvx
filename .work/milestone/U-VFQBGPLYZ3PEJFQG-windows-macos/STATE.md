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
- Windows rule namespace (proposed, for BR): `.localhost` rather than `.dev.localhost`. It is what RFC 6761 asks of a resolver anyway, it covers the tests' `kvxtest.localhost` and any custom vault domain, and plain `localhost` keeps resolving. The cost: while the rule exists, every `*.localhost` name on the machine needs kvx running.
- macOS protector: own Security.framework calls (file-based login keychain, generic password holding a key-encryption key), not MSAL Extensions: same keychain, same prompts, without the MSAL dependency.
- macOS item access (proposed, for BR): the item trusts any application of the user, not only the creating binary. The spike showed a read from another binary (what every tool update is) blocks on an invisible prompt rather than failing; per-app trust would buy nothing anyway, since DPAPI and `systemd-creds --user` already let any process of the user unwrap. User interaction is turned off so a locked keychain fails fast with errSecInteractionNotAllowed, which `doctor` explains.
- Windows protector: DPAPI, CurrentUser. Caveat for the README: an admin password reset of a local account loses the store.
- Defaults (BR accepted 2026-10-24): data dir `%LOCALAPPDATA%\kvx` and `~/Library/Application Support/kvx`; token in the per-user temp dir, replaced when the machine has rebooted since it was written (weaker than Linux, which also drops it at logout).
- CI trust: Windows imports the test certificate into LocalMachine\Root, macOS adds it with `sudo security add-trusted-cert` to the System keychain; directory trust (SSL_CERT_DIR) exists only on Linux. Linux CI pins `ubuntu-26.04` (systemd 259) until `ubuntu-latest` has moved to it.

## Now
Second spike read (branch `spike/windows-macos`, GitHub only): every open question answered; two proposals wait on BR (rule namespace, macOS item access).

## Next
- BR: the two proposals in Plan.
- Delete the spike branch on GitHub (its answers are recorded above).
- Protectors, then per-OS paths and token, then the DNS responder, `doctor` and README.
- A push mirror from gitea to GitHub (BR), until then pushed by hand with `GH_REPO_TOKEN_KVX`.
- Unverifiable in CI, for a real machine: NRPT on Home editions (research found no edition limit), Group Policy NRPT rules overriding local ones, ICS (SharedAccess) holding port 53 when Hyper-V, WSL or Docker run.

## Evidence
- Spike run 40007803218 (2026-10-23), 3/53 passing on every OS, all other tests stopped by the key protector:
  - Windows Server (windows-latest): `secrets.dev.localhost` does **not** resolve through .NET's Dns (probed from pwsh, so the runtime may predate .NET 10: "No such host is known"); curl connects, since it resolves `*.localhost` itself.
  - macOS (macos-latest): resolves to `::1, 127.0.0.1` through .NET and curl.
  - Ubuntu (ubuntu-latest = 24.04): resolves, but systemd is 255, whose `systemd-creds` has no `--user`; kvx's Linux protector needs 256.
- Spike run 40037860344 (2026-10-24, branch `spike/windows-macos`):
  - Windows Server 2025: a standard (non-admin) local user binds UDP and TCP 127.0.0.1:53; SharedAccess runs but holds no port 53. `Add-DnsClientNrptRule .dev.localhost → 127.0.0.1` takes effect at once, no restart: .NET 10 resolves `secrets.` and `new-vault.dev.localhost` to 127.0.0.1 through a loopback responder; `kvxtest.localhost` needs a rule of its own (or `.localhost`); `localhost` itself keeps resolving under a `.localhost` rule; with the responder stopped the names fail again. Windows asked only for A records.
  - macOS 26.6: a generic-password add and read round trip works in the unlocked login keychain. A read from a second binary and a read after `security lock-keychain` both **hang** (killed at 2 min) instead of returning an error.
  - Ubuntu 26.04: systemd 259, `systemd-creds.socket` active, `systemd-creds --user` round trips as the runner user; 51/53 tests passed, the 2 failures fixed in b0f880d.

## Harvest
- [ ] learning: stock Windows does not resolve `*.localhost` subdomains for .NET, while macOS and systemd-resolved Linux do; curl resolves them itself everywhere
- [ ] decision: Windows vault names through a one-time NRPT rule and kvx's loopback DNS responder, not hosts entries per vault, a public loopback domain, or waiting for .NET 11
- [ ] decision: own Security.framework calls for the macOS protector rather than MSAL Extensions
- [ ] learning: GitHub's ubuntu-24.04 runner has systemd 255, too old for `systemd-creds --user`; ubuntu-26.04 has 259, and `--user` needs only the system's `systemd-creds.socket`, no user manager
- [ ] learning: a NRPT rule added with the cmdlet applies immediately; Group Policy NRPT rules, if any exist, silently replace all local ones
- [ ] learning: a keychain read that needs a prompt (another binary, a locked keychain) blocks without UI instead of failing, unless user interaction is turned off
- [ ] learning: an app run from another project's output loses its package assemblies to that project's shared framework when the runtime is newer than the packages

## Log
- 2026-10-23 initialized; BR chose a GitHub Actions mirror for Windows and macOS proof
- 2026-10-23 spike run: Windows does not resolve `*.localhost` for .NET; BR chose a one-time NRPT rule
- 2026-10-24 the TLS end-to-end tests ran the sample app from the test output, which breaks once the runtime is newer than its packages (CI's 10.0.13); it now runs from its own output
- 2026-10-24 BR accepted the data dir and token defaults; second spike answered NRPT, port 53, the login keychain and systemd on runners
