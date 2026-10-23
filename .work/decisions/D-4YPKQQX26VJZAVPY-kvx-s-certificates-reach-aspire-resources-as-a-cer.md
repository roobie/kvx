---
status: accepted
date: 2026-10-23
read_if:
  - an Aspire resource does not trust kvx's certificate
  - changing how the hosting integration sets up TLS trust
refs:
  - src/Kvx.Aspire.Hosting/KvxHostingExtensions.cs#Trust
---

# D-4YPKQQX26VJZAVPY kvx's certificates reach Aspire resources as a CertificateAuthorityCollection, not SSL_CERT_DIR

## Context
On Linux, .NET and OpenSSL clients trust the dev cert only through SSL_CERT_DIR, which `kvx run` sets. Aspire 13.6 sets SSL_CERT_DIR on every resource itself, after environment callbacks, so a value set by the integration is silently overwritten.

## Decision
The integration loads kvx's certificates into a `CertificateAuthorityCollection` and attaches it to every resource that references the vault.

## Why
- Fighting Aspire for SSL_CERT_DIR loses, or breaks the trust Aspire sets up for its own endpoints.
- The collection is Aspire's supported way to add trust: it merges with Aspire's own, and Aspire writes the certificates out in a form both .NET and non-.NET clients (curl) accept.

## Consequences
- kvx's trust follows Aspire's model, including for resource types Aspire adds later.
- Containers referencing the vault remain out of scope; their trust and loopback reachability differ.
