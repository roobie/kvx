---
date: 2026-10-24
read_if:
  - running curl or another Schannel client against kvx on Windows with a CA-issued certificate
refs:
  - tests/Kvx.Tests/AspireTests.cs#Run
---

# L-J2HIVGIV24FCZLYX Windows curl checks revocation only for CA-issued certificates

## Observation
Windows' curl uses Schannel. It refuses a certificate issued by a trusted CA that publishes no revocation list (CRYPT_E_NO_REVOCATION_CHECK), but it doesn't check a self-signed one, such as the .NET dev cert kvx serves by default.

## Why it matters
Developers on the default certificate see no problem. Only CI's test CA, or a developer's own private CA, runs into it. So the test, not kvx, opts out of the check, and only for the test CA. A private CA that publishes a revocation endpoint wouldn't need the opt-out.
