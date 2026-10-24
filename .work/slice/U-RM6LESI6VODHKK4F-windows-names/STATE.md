---
phase: execute
parent: U-VFQBGPLYZ3PEJFQG
owner:
  - claude
created: 2026-10-24
success_criteria:
  - on windows-latest, with the NRPT rule doctor asks for, the whole suite passes
  - kvx answers `*.localhost` A queries with loopback; a second kvx takes over when the first stops
  - `kvx doctor` on Windows names the NRPT command when kvx answers but Windows doesn't ask it, and does not FAIL a user whose vaults resolve through hosts lines
  - the README says how vault names resolve per OS, and the DPAPI password-reset caveat
---

# slice/windows-names

## Purpose
Stock Windows doesn't resolve `*.localhost` subdomains for .NET, so no vault is reachable there; the milestone chose a one-time NRPT rule plus a responder in kvx.

## Scope
In: the loopback DNS responder and its `Kvx:Dns` setting, the CI rule, `doctor`'s name check, the README.
Out: real-machine checks CI can't make (Home editions, Group Policy NRPT, ICS on port 53); they stay in the milestone's Next.

## Context & constraints
- Milestone Plan: "Windows names", "Windows rule namespace", "Hosts-file fallback".
- `doctor` never opens the store, so it can't list vaults; the hosts file's lines under the domain stand for a hosts-file user's vaults.
- Several kvx may run (tests, an AppHost and a shell); only one can hold port 53, and all answer alike.

## Now
Responder, setting, doctor and README in place; local Linux 66/66. Pushing for the Windows read.

## Next
- Read CI; then finalize.

## Evidence
- Local Linux: the whole suite passes, with the responder tested on a free port (answers, refusals, malformed input, takeover).
- Run 40048722510 (94f8032): windows-latest 65/66; every name resolves through the NRPT rule. The one failure was Windows' curl refusing the test CA's certificate for its revocation check (CRYPT_E_NO_REVOCATION_CHECK); the test now passes `--ssl-no-revoke` there.

## Harvest
- [ ] learning: since .NET 10, BackgroundService.StartAsync returns before ExecuteAsync's synchronous part has run, so a test can't assume a bind happened at start
- [ ] learning: Schannel (Windows' curl) fails a CA-issued certificate with no revocation list, but doesn't check a self-signed one like the dev cert; only the test CA needs `--ssl-no-revoke`

## Log
- 2026-10-24 initialized
- 2026-10-24 phase initialize → execute
