---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - `kvx doctor` reports name resolution, the certificate kvx serves, the key protector and whether kvx is serving, each as ok or with a concrete fix; it exits 1 when any check fails and 0 otherwise, never opens the store, and changes nothing on the machine
  - each failure is shown by a test that induces it without touching the machine's real setup - an unresolvable domain, no dev cert, an expired certificate, a certificate that does not cover the domain, an untrusted certificate, an unusable key protector, and kvx not serving (a warning, not a failure)
  - on BR's machine the installed tool reports every check ok against a serving kvx
  - kvx's own startup failures (no certificate, key protector) and the client commands' "not serving" error point to `kvx doctor`, and the README makes it the first step when something does not work
---

# milestone/kvx-doctor

## Purpose
A first-time user's failures come from the machine, not from kvx, and surface as SDK errors that don't name the cause. One command should say what is wrong and how to fix it.

## Scope
In: a `kvx doctor` client command with checks for vault-name resolution, the certificate (the dev cert or the configured one: present, valid, covers `*.<Domain>`, trusted by .NET), the key protector (a wrap/unwrap round trip), and the serving kvx (identity probe and a live TLS handshake on the https listener); pointers to it from existing failure messages and the README.
Out: fixing anything for the user (no `--fix`); checks for non-.NET clients' trust; Windows/macOS key protectors; reading or validating the existing store.

## Context & constraints
- Client commands never open the store (D-L4OU7YMFJ6W6OENA); doctor is a client, so the key protector probe round-trips random bytes and nothing else.
- Name resolution of `*.localhost` depends on NSS (L-NRQPHNQCOYNBPSS7).
- Kestrel serves the dev cert unless `Kestrel:Certificates:Default` is set (L-5KDVFNPFDTS6D7YB, D-LLPISGPW4SP4BB4K). Doctor must judge the same certificate kvx would serve.
- On Linux .NET trusts the dev cert through its user Root store, and a generated test cert through SSL_CERT_DIR (L-LDQ2ERU56U2NNKPV); a chain build in the doctor process sees both.
- Tests must not need or change the machine's dev cert, NSS or systemd setup; they induce each failure through settings and a throwaway HOME.

### Origin
Promoted from backlog: B-6UOHZXCHMFX3OSIY kvx doctor: check name resolution, CA trust and key protector

## Now
Doctor, tests, pointers and README are done. Only criterion 3 is left: the installed tool against a serving kvx.

## Next
- Reinstall the tool, serve, run `kvx doctor` (all ok, including serving and https), then finalize.

## Evidence
- Criteria 1, 2 and 4: DoctorTests has one test per induced failure plus an all-ok run against a serving kvx. CliTests asserts the `kvx doctor` pointer in the no-cert and not-serving errors. `dotnet test` passes 46/46.
- Partial criterion 3: on BR's machine, `dotnet run -- doctor` with kvx not serving reported name resolution, the dev cert (38DDEEFE…, valid until 2027-09-06) and systemd-creds as ok, serving as a warning, and exited 0.

## Harvest
- [ ] decision: doctor judges the certificate kvx would serve (the configured one, else the dev cert), checks .NET trust only, and goes straight to the listener for the handshake so that a TLS failure is told apart from a name-resolution failure
- [ ] learning: the systemd-creds round trip can create the per-user credential key on first use, so "changes nothing" holds only once kvx has run on that machine

## Log
- 2026-10-23 initialized
- 2026-10-23 phase initialize → execute
