---
date: 2026-10-23
read_if:
  - a client on Linux does not trust kvx's certificate
  - changing what kvx run or kvx env set
refs:
  - src/Kvx.Server/Cli/KvxClient.cs#AppVariables
---

# L-LDQ2ERU56U2NNKPV On Linux dev-certs --trust makes .NET apps trust the cert; SSL_CERT_DIR only matters for OpenSSL tools

## Observation
On Linux `dotnet dev-certs https --trust` does two things: it adds the cert to .NET's per-user Root store (`~/.dotnet/corefx/cryptography/x509stores/root`), which .NET apps trust directly, and writes an OpenSSL trust directory that other tools see only through SSL_CERT_DIR. Its warning ("There was an error trusting…", SSL_CERT_DIR must be set) is about the second part only. Confirmed 2026-10-23: an unmodified DAC app read a secret from kvx with no SSL_CERT_DIR set.

## Why it matters
.NET apps on Linux need nothing beyond `--trust`; the SSL_CERT_DIR that `kvx run`/`kvx env` add helps only curl and other OpenSSL-based clients. Don't tell users the warning means .NET apps will fail. Tests that use a generated cert can't rely on the user store and use SSL_CERT_DIR instead.
