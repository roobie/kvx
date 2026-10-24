---
date: 2026-10-24
read_if:
  - writing a test that needs a user without a dev certificate
  - debugging certificate lookup on Windows or macOS
refs:
  - tests/Kvx.Tests/CliTests.cs#Serve_without_a_certificate_refuses_and_says_how_to_get_one
  - src/Kvx.Server/Cli/ServedCertificate.cs#Find
---

# L-CM3ZNAPDTPQ7FJGN The ASP.NET dev certificate is under HOME only on Linux

## Observation
On Linux .NET keeps the CurrentUser\My store as files under the home directory, so an empty HOME is a user without a dev certificate. On Windows and macOS that store is the OS's (the certificate store, the keychain), and HOME does not reach it; GitHub's Windows runner already holds a dev certificate. CI run 40042895190 hung for 30 minutes because a "no certificate" kvx found one and served.

## Why it matters
A test of the missing-certificate path can only run on Linux unless kvx gains a way to point its lookup at another store. A test that expects a command to fail must not be able to wait forever if it succeeds instead.
