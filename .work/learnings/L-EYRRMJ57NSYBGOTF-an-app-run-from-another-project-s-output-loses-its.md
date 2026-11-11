---
date: 2026-11-11
read_if:
  - a test runs another project's app
  - a test app fails to load a package assembly in CI
refs:
  - tests/Kvx.Tests/TlsEndToEndTests.cs#SampleApp
---

# L-EYRRMJ57NSYBGOTF An app run from another project's output loses its package assemblies to the newer shared framework

## Observation
The TLS end-to-end tests ran the sample app from the test project's output. Once CI's runtime (10.0.13) was newer than the sample's package versions, the app loaded the test project's shared-framework assemblies instead of its own packages and broke. Running the app from its own output fixed it.

## Why it matters
A test that starts another project's app must run it from that project's output directory, not copy it into the test output.
