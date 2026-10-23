---
status: accepted
date: 2026-10-23
read_if:
  - changing the vault host name or domain
  - changing how kvx gets or serves its TLS certificate
refs:
  - src/Kvx.Server/appsettings.json
  - src/Kvx.Server/Cli/KvxCli.cs#Dispatch
---

# D-LLPISGPW4SP4BB4K Vaults are https://<vault>.dev.localhost, served by Kestrel with the ASP.NET Core dev cert

## Context
The SDK needs https and a vault host that ends with the challenge resource's host, and a stock app must work with no SDK option. Caddy provided TLS until now, but as a second install alongside the dotnet tool and a second CA to trust.

## Decision
kvx serves https itself, with the ASP.NET Core development certificate by default. Vaults are subdomains of `dev.localhost` because that is the wildcard the dev cert carries; a configured certificate replaces it for any other domain.

## Why
.NET developers already have the dev cert, and Visual Studio, Rider or one `dotnet dev-certs` command trusts it on every OS, so kvx adds nothing to trust. The alternatives were keeping Caddy (an extra install), or kvx minting and installing its own CA (a trust-store change made by kvx, per OS, and a CA key kvx would have to guard). The previous host `*.kvx.localhost` is not in the dev cert, and a wildcard directly under `localhost` is not usable.

## Consequences
No proxy and no kvx-owned CA. kvx depends on the dev cert's SAN list, so if a future SDK drops `*.dev.localhost` the default domain must follow. Other local tools that serve `*.dev.localhost` share the namespace. With no certificate kvx refuses to start rather than fall back to http.
