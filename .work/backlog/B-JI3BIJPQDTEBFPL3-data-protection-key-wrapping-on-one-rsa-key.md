---
created: 2026-10-24
---

# B-JI3BIJPQDTEBFPL3 data protection key wrapping on one RSA key

## Why
`ProtectKeysWithAzureKeyVault` is a common enterprise pattern for ASP.NET Core Data Protection. Such apps can't run on kvx without a code change today, which breaks "only the vault URI changes". It needs only a small slice of the keys API (get key, wrapKey, unwrapKey on one RSA key), but it opens a second API surface, so it waits for a real app that needs it.

## Notes
- Out: every other key operation and algorithm (project Scope).
