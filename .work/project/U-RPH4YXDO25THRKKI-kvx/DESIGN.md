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
- Auth, for now: ephemeral dev tokens. The signing key is regenerated on every kvx start, so tokens never outlive the process. This keeps development unblocked; real Entra validation can come later.
- Host name: look real without `DisableChallengeResourceVerification`. The SDK only checks that the request host ends with the challenge resource's host, so kvx answers with resource `https://kvx.localhost` and vaults live at `https://<vault>.kvx.localhost`. No azure.net names are hijacked and no SDK option is needed.
- TLS: Caddy reverse proxy with `tls internal` (its own CA, trusted via `caddy trust`) for development.
- Headless Linux: `systemd-creds --user` wraps the data key. It needs no UX, works unprivileged (verified on systemd 257) and uses TPM2 when present. A passphrase/agent fallback waits until someone needs it.
- Storage: the whole store (names, tags, attributes, values) is one AES-GCM sealed file, loaded into memory and atomically replaced on write. This is simpler than per-value encryption and hides names for free. The ergonomic cost is only that the file can't be inspected directly; the CLI covers that.

## Risks & open questions
- How a client obtains a dev token: tests can use a static credential; apps using `DefaultAzureCredential` need a path (a managed-identity-style endpoint via `IDENTITY_ENDPOINT` is the candidate). It would also catch token requests for other Azure resources.
- `*.localhost` resolves to loopback with systemd-resolved and browsers, but not with every glibc setup; may need a hosts entry or a check.
- Caddy is an extra runtime dependency for a dotnet tool; revisit (Kestrel with its own CA) before packaging.
- Whole-file store: a corrupt or truncated write loses everything, so keep the previous generation. Fine for dev-sized stores (thousands of secrets), not beyond.
- Machine-bound keys (DPAPI, systemd-creds) make a copied store useless elsewhere. That is intended, but losing the machine loses the secrets; an explicit export/backup path is needed.
- Keyring prompts (Keychain, some Secret Service setups) can block at startup; the service must tolerate this.
- Must detect a missing libsecret and fail with a clear message.
