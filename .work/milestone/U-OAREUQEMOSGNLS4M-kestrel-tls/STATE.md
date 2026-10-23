---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - with the ASP.NET Core dev cert trusted, `kvx run` starts an unmodified DefaultAzureCredential app that round-trips secrets at `https://<vault>.dev.localhost:<port>` served by kvx itself, with no Caddy and no trust variables set by the caller
  - with no usable certificate kvx refuses to start and says how to create and trust the dev cert
  - a certificate configured in settings replaces the dev cert (for another Domain), shown by a test that does not depend on the machine's dev cert
  - Caddy is gone from the repo, tests and README; the README says how to trust the dev cert on Linux, Windows and macOS
---

# milestone/kestrel-tls

## Purpose
Remove Caddy, the one extra install the dotnet tool still needs, by serving TLS from kvx with the certificate .NET developers already trust.

## Scope
In: https from Kestrel with the ASP.NET Core dev cert; the default Domain becomes `dev.localhost`; `kvx run`/`kvx env` pass the Linux dev-cert trust directory to the app; settings for a custom certificate; removing Caddy.
Out: kvx's own CA or `kvx trust`; changing the dev cert or trusting it on the user's behalf; Windows/macOS key protectors.

## Context & constraints
- The dev cert (verified on this machine) carries `localhost`, `*.dev.localhost`, `*.dev.internal`, 127.0.0.1 and ::1, but not `*.kvx.localhost`. A wildcard directly under `localhost` is not usable, so vaults need a second label, and `dev.localhost` is what the cert offers. The SDK's challenge check only needs the vault host to end with the challenge resource host.
- Visual Studio, Rider and `dotnet dev-certs https --trust` already trust the dev cert on Windows and macOS. On Linux, `--trust` writes a trust directory that processes only see through SSL_CERT_DIR, so `kvx run` and `kvx env` can supply it.
- The client commands (run, env, import) and the identity endpoint must keep working without the client trusting anything. Azure's own IDENTITY_ENDPOINT is plain http on loopback, so a loopback http listener stays alongside https.
- Tests must not need, or change, the machine's dev cert: they configure a generated certificate. A manual smoke run covers the real dev cert after the owner has trusted it.
- *.localhost resolution depends on NSS (L-NRQPHNQCOYNBPSS7).

## Now
Code, tests and README done (36/36). Waiting for BR to trust the dev cert on this machine for the criterion-1 smoke run.

## Next
- Smoke: installed tool, real dev cert, `kvx run` the sample app with SSL_CERT_* cleared in the caller.
- Finalize.

## Evidence
- Spike: started in the Production environment with `--urls=https://…;http://…` and no certificate settings, kvx served this machine's dev cert (SHA-1 38DD…C9BA) and answered with its 401 challenge.
- Criterion 2: `CliTests.Serve_without_a_certificate_refuses_and_says_how_to_get_one` (a HOME with no dev cert → exit 1, names `dotnet dev-certs https --trust`).
- Criterion 3: every live test serves `*.kvxtest.localhost` from a generated cert set under `Kestrel:Certificates:Default`; `TlsEndToEndTests` round-trips an unmodified DAC app through `kvx run`, which supplies the cert's trust dir via SSL_CERT_DIR (36/36).
- Criterion 4: `git grep -i caddy` finds nothing outside `.work` history; README Serve section covers trust per OS.

## Harvest
- [ ] decision: serve the ASP.NET Core dev cert from Kestrel, hence the `dev.localhost` domain, over kvx's own CA or a proxy
- [ ] decision: plain-http loopback listener for the identity endpoint and kvx's commands, https only for vaults
- [ ] decision: `kvx run`/`kvx env` pass the dev cert's trust dir via SSL_CERT_DIR on Linux, rather than kvx touching the system trust store
- [ ] learning: Kestrel loads the dev cert for https urls even in Production; with none it throws from ListenOptionsHttpsExtensions.UseHttps

## Log
- 2026-10-23 initialized
- 2026-10-23 phase initialize → execute
