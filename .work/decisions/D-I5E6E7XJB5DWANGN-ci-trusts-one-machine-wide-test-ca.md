---
status: accepted
date: 2026-10-24
read_if:
  - a TLS test fails for trust on Windows or macOS
  - changing how tests get trusted certificates
refs:
  - .github/scripts/trust-test-ca.ps1
  - tests/Kvx.Tests/LiveKvx.cs#TestCa
---

# D-I5E6E7XJB5DWANGN CI trusts one machine-wide test CA

## Context
On Windows and macOS, certificate trust comes only from the OS store. Linux can use a trust directory instead. The user-level stores prompt a person before adding a root, and the machine-level stores need admin rights. So without help, a test on those OSes can't make kvx's certificate trusted, and every TLS test failed for that reason.

## Decision
CI trusts one throwaway CA machine-wide before the build. When that CA is present, the tests issue their certificates from it; when it's absent, they fall back to self-signed certificates and the Linux trust directory.

## Why
- **Tests add trust themselves:** they would prompt, or need admin rights, on any developer machine that runs the suite.
- **Mocking chain validation:** it would stop testing the thing `kvx doctor` and real clients depend on.
- **One CA trusted by CI:** CI runners already have admin rights and passwordless sudo, so this costs one step and leaves developer machines untouched.

## Consequences
- On a Windows or Mac developer machine without the CA, TLS tests fail for trust. That failure is the honest result, and the fix is to run the same script there.
- The untrusted-certificate test must issue its own self-signed certificate, so the machine-wide CA can't make it pass.
