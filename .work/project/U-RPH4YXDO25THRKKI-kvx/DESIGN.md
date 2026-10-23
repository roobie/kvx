# Design

## Problem
Developers need secrets locally, and the convenient stores keep them in plaintext. A store that is safe at rest but needs different app code in dev than in prod will be bypassed, so safety has to come without a code change.

## Approach
- A per-user HTTPS service on loopback that implements the Key Vault secrets REST API, including challenge-based auth, so the stock Azure SDK works against it.
- Envelope encryption: each secret version is sealed with a data key (AES-GCM); the data key is wrapped by a platform key protector.
- The key protector is one narrow seam (wrap/unwrap the data key), with these implementations: DPAPI (Windows), Keychain (macOS), Secret Service (Linux), plus a non-plaintext fallback for headless Linux. Reuse `Microsoft.Identity.Client.Extensions.Msal` storage where it fits; Git Credential Manager's stores are a reference.
- Packaging: dotnet tool, a user-secrets importer, and an Aspire hosting package.

## Alternatives considered
- Fork the Go emulator (calvinchengx): closest feature match, but inherits a keys/certs surface and test-double assumptions.
- Go implementation: a better single-binary story, but weaker fit for the .NET audience and Aspire.
- Not wire-compatible (sops+age, a custom config provider): solves encryption, but dev and prod code diverge.

## Settled (2026-10-23, with BR)
- Auth, for now: one random dev token per login session, kept in `$XDG_RUNTIME_DIR` so it survives kvx restarts but not logout or reboot (BR, 2026-10-23). Restarts must not break running apps, which cache tokens in memory. Real Entra validation can come later.
- Apps authenticate with an unmodified `DefaultAzureCredential`: kvx speaks the App Service managed-identity protocol, and the dev token doubles as IDENTITY_HEADER.
- Host name: look real without `DisableChallengeResourceVerification`. The SDK only checks that the request host ends with the challenge resource's host, so kvx answers with resource `https://dev.localhost` and vaults live at `https://<vault>.dev.localhost`. No azure.net names are hijacked and no SDK option is needed.
- TLS: Kestrel serves the ASP.NET Core dev cert, which .NET developers already have and trust and which covers `*.dev.localhost`; that is why the domain is `dev.localhost`. A configured certificate replaces it for another domain. The identity endpoint and kvx's own commands use a plain-http loopback listener, as Azure's identity endpoint does.
- Headless Linux: `systemd-creds --user` wraps the data key. It needs no UX, works unprivileged (verified on systemd 257) and uses TPM2 when present. A passphrase/agent fallback waits until someone needs it.
- Storage: the whole store (names, tags, attributes, values) is one AES-GCM sealed file, loaded into memory and atomically replaced on write. This is simpler than per-value encryption and hides names for free. The ergonomic cost is only that the file can't be inspected directly; the CLI covers that.
- One `kvx` tool. Its helper commands (run, env, import) are clients of the serving kvx and never open the store (D-L4OU7YMFJ6W6OENA). `kvx run` removes the credential variables that would outrank kvx (D-IJMXLBG5U3LFTH62).

## Risks & open questions
- An app using kvx through `DefaultAzureCredential` gets every Azure token from kvx. Other resources pass through to the developer's login (D-BGGXSS3A5S3YOP24), so locally the app acts as the developer, not as its managed identity. Access can then differ from production, and kvx must find the developer's CLIs and login when running as a service.
- `*.localhost` resolves to loopback with systemd-resolved and browsers, but not with every glibc setup. `kvx doctor` detects this and says how to fix it (D-TWUUALFAE6RLZWGC).
- The dev cert is per user and expires (about a year). An expired or regenerated cert breaks every app until it is trusted again; `kvx doctor` catches it and warns two weeks before expiry.
- Whole-file store: a corrupt or truncated write loses everything, so keep the previous generation. Fine for dev-sized stores (thousands of secrets), not beyond.
- Machine-bound keys (DPAPI, systemd-creds) make a copied store useless elsewhere. That is intended, but losing the machine loses the secrets; an explicit export/backup path is needed.
- Keyring prompts (Keychain, some Secret Service setups) can block at startup; the service must tolerate this.
- Must detect a missing libsecret and fail with a clear message.
