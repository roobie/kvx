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

## Risks & open questions
- Auth posture: accept any bearer token, validate real Entra JWTs offline, or a local trust model (loopback plus a keyring-held token)? Ties into what `DefaultAzureCredential` does on a dev box.
- SDK challenge-resource verification: emulate a host name the SDK accepts without `DisableChallengeResourceVerification`, or require that option? Zero-config compatibility depends on it.
- Certificate trust: generate a per-user CA or reuse the dotnet dev-cert?
- Headless Linux fallback: systemd-creds, a session passphrase (agent model), or pass/GPG?
- Do secret names, tags and content types also need encryption? Names leak information, but listing needs them.
- Keyring prompts (Keychain, some Secret Service setups) can block at startup; the service must tolerate this.
- Must detect a missing libsecret and fail with a clear message.
