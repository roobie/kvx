---
date: 2026-10-23
read_if:
  - changing the vault host scheme or challenge header
  - debugging SDK auth failures against kvx
refs:
  - src/Kvx.Server/Auth/ChallengeAuth.cs#UseChallengeAuth
---

# L-7BL5CLN57QWZWCQU Azure SDK challenge verification is a host suffix match

## Observation
The SDK accepts any vault whose host sits under the challenge's resource host, and takes the tenant from the authorization URI. That is what lets kvx look real under its own domain without hijacking azure.net names or requiring `DisableChallengeResourceVerification`. Source: azure-sdk-for-net `ChallengeBasedAuthenticationPolicy.VerifyChallengeResource`.

## Why it matters
Any change to the domain or challenge header must keep vault hosts strictly beneath the resource host. It stops being true if the SDK tightens verification, for example to an allow-list of Azure domains; the challenge-verification test would catch that on an SDK upgrade.
