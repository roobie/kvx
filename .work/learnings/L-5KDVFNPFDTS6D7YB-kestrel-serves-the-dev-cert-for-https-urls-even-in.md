---
date: 2026-10-23
read_if:
  - kvx fails to start over a missing certificate
  - changing Kestrel certificate configuration
refs:
  - src/Kvx.Server/Cli/KvxCli.cs#Dispatch
---

# L-5KDVFNPFDTS6D7YB Kestrel serves the dev cert for https urls even in Production

## Observation
Kestrel falls back to the user's ASP.NET Core dev cert for any https url with no certificate configured, in every environment, not just Development (seen 2026-10-23, .NET 10). With no dev cert it throws an InvalidOperationException from `ListenOptionsHttpsExtensions.UseHttps` at startup, with advice that reads as a crash.

## Why it matters
kvx needs no certificate code of its own for the default case; configuring `Kestrel:Certificates:Default` is the only override. The no-certificate case is recognised by that exception's origin, so a Kestrel change to the exception or its throw site would bring the raw crash back (the CLI test for a missing cert catches that).
