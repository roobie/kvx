---
created: 2026-10-23
---

# B-6UOHZXCHMFX3OSIY kvx doctor: check name resolution, CA trust and key protector

## Why
kvx works only when three machine-level things are true: vault names resolve to loopback, the ASP.NET Core dev cert exists and is trusted, and the key protector is available. Each fails silently or with an unhelpful SDK error (e.g. "Name or service not known"). A developer trying kvx for the first time needs one command that says what's wrong and how to fix it.

## Notes
- Name resolution: stock Debian lacks nss-myhostname; suggest the package and the nsswitch line.
- Cert trust: suggest `dotnet dev-certs https --trust`; on Linux also check that `~/.aspnet/dev-certs/trust` exists, since apps started outside `kvx run`/`kvx env` need it in `SSL_CERT_DIR`.
- Key protector: systemd-creds version (≥ 256) and a wrap/unwrap probe.
