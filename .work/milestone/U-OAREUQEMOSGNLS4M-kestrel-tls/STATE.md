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
- Visual Studio, Rider and `dotnet dev-certs https --trust` already trust the dev cert on Windows and macOS. On Linux, `--trust` also adds it to .NET's per-user Root store (so .NET apps trust it) and writes an OpenSSL trust directory that other tools see only through SSL_CERT_DIR, which `kvx run` and `kvx env` supply.
- The client commands (run, env, import) and the identity endpoint must keep working without the client trusting anything. Azure's own IDENTITY_ENDPOINT is plain http on loopback, so a loopback http listener stays alongside https.
- Tests must not need, or change, the machine's dev cert: they configure a generated certificate. A manual smoke run covers the real dev cert after the owner has trusted it.
- *.localhost resolution depends on NSS (L-NRQPHNQCOYNBPSS7).

## Now
All criteria shown; finalizing.

## Next
- Harvest, close.

## Evidence
- Criterion 1 (smoke, BR's machine after `dotnet dev-certs https --trust`): the freshly packed and installed tool, run from an unrelated directory on its default urls, served SHA-1 38DD…C9BA (SANs include `*.dev.localhost`). With SSL_CERT_* and IDENTITY_* unset and a stray AZURE_CLIENT_SECRET, `kvx run` started the unmodified sample app, which set and read back a secret at `https://smoke.dev.localhost:5381` and read it again through AddAzureKeyVault. The sample's extra Storage-token step failed with kvx's sign-in message, since this machine has no Azure login (expected passthrough behaviour).
- The same app also worked without SSL_CERT_DIR: `--trust` puts the cert in `~/.dotnet/corefx/cryptography/x509stores/root`, so the SSL_CERT_DIR passthrough is only for non-.NET tools. README, DESIGN and the no-cert message corrected.
- Spike: started in the Production environment with `--urls=https://…;http://…` and no certificate settings, kvx served this machine's dev cert (SHA-1 38DD…C9BA) and answered with its 401 challenge.
- Criterion 2: `CliTests.Serve_without_a_certificate_refuses_and_says_how_to_get_one` (a HOME with no dev cert → exit 1, names `dotnet dev-certs https --trust`).
- Criterion 3: every live test serves `*.kvxtest.localhost` from a generated cert set under `Kestrel:Certificates:Default`; `TlsEndToEndTests` round-trips an unmodified DAC app through `kvx run`, which supplies the cert's trust dir via SSL_CERT_DIR (36/36).
- Criterion 4: `git grep -i caddy` finds nothing outside `.work` history; README Serve section covers trust per OS.

## Harvest
- [ ] decision: serve the ASP.NET Core dev cert from Kestrel, hence the `dev.localhost` domain, over kvx's own CA or a proxy
- [ ] decision: plain-http loopback listener for the identity endpoint and kvx's commands, https only for vaults
- [ ] learning: Kestrel loads the dev cert for https urls even in Production; with none it throws from ListenOptionsHttpsExtensions.UseHttps
- [ ] learning: on Linux `dev-certs --trust` makes .NET apps trust the cert via the user Root store; its SSL_CERT_DIR warning concerns only OpenSSL-based tools

## Log
- 2026-10-23 initialized
- 2026-10-23 phase initialize → execute
