---
created: 2026-10-23
---

# B-ONCYO4FXWOO2VFDB Let kvx-backed apps reach other Azure services

## Why
Most real apps use more than Key Vault. Today, pointing one at kvx through DefaultAzureCredential cuts it off from every other Azure service (L-GPX6ZOUCE2IAT4SR). That excludes exactly the apps the "no code change" pitch targets, and the README warns about it up front.

## Notes
- Candidate: for any resource other than kvx's, the identity endpoint passes the request through to the developer's own Azure login (az / azd / VS credential) instead of refusing it. Apps would then reach real Azure services as the developer, as they would without kvx.
- Before choosing, check the security cost of kvx brokering real Entra tokens.
- Check that MSAL's per-resource caching works when tokens for different resources come from different issuers.
