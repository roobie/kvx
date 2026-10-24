---
phase: execute
parent: U-VFQBGPLYZ3PEJFQG
owner:
  - claude
created: 2026-10-24
success_criteria:
  - on macos-latest the whole suite passes (every remaining failure there was UntrustedRoot)
  - on windows-latest no test fails for trust; only name resolution is left
  - a certificate the OS does not trust is still reported by `kvx doctor` on every OS (An_untrusted_certificate_fails)
---

# slice/ci-test-trust

## Purpose
The tests' certificates are trusted on Linux through a trust directory; Windows and macOS trust only their OS store, so every TLS test there fails for UntrustedRoot, hiding whether kvx itself works.

## Scope
In: a CA that CI trusts machine-wide on Windows and macOS, test certificates issued from it, the untrusted-certificate test kept honest.
Out: name resolution on Windows (next slice), anything in kvx itself.

## Context & constraints
- Milestone Plan "CI trust" (LocalMachine\Root, System keychain via sudo).
- A test can't add trust itself: CurrentUser\Root prompts on Windows, user trust settings prompt on macOS, and machine stores need admin. CI runners have admin and passwordless sudo; a developer box running the tests must not be changed by them.
- Tests issue certificates already expired or expiring soon, so the CA is back-dated.

## Now
CA step in ci.yml, LiveKvx issues from KVX_TEST_CA when set. Local Linux 56/56 with and without a CA. Pushing for the Windows and macOS read.

## Next
- Read CI; then finalize.

## Evidence
- Local Linux 56/56 without KVX_TEST_CA and with a CA made the way CI makes it.

## Harvest

## Log
- 2026-10-24 initialized
- 2026-10-24 phase initialize → execute
