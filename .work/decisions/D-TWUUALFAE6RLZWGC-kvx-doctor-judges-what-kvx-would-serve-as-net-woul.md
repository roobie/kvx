---
status: accepted
date: 2026-10-23
read_if:
  - changing kvx doctor's checks
  - adding a certificate source or trust check
refs:
  - src/Kvx.Server/Cli/ServedCertificate.cs#Find
  - src/Kvx.Server/Cli/DoctorCommand.cs#Handshake
---

# D-TWUUALFAE6RLZWGC kvx doctor judges what kvx would serve, as .NET would trust it

## Context
A first-time user's failures come from the machine, and an SDK error such as "the remote certificate is invalid" names neither the cert nor the fix. Several certificates may exist (old dev certs, a configured one), and trust differs between .NET, OpenSSL tools and browsers.

## Decision
The doctor judges the certificate Kestrel would pick, by Kestrel's own rule, and checks it the way a .NET app's chain build would. Its live handshake bypasses name resolution so that each failure is reported once, under its own cause.

## Why
- Checking "some dev cert exists" would pass while kvx serves an expired or configured cert.
- Checking curl or browser trust would fail for setups where every kvx client (a .NET app) works, and send users after problems they don't have.
- A handshake through DNS would blame TLS for a resolution failure that is already reported separately.

## Consequences
If kvx gains another certificate source, ServedCertificate must follow it, or the doctor will judge a certificate that kvx does not serve. Trust for non-.NET clients stays unchecked until there is a reason to support those clients.
