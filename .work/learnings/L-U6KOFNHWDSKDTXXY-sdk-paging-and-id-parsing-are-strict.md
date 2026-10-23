---
date: 2026-10-23
read_if:
  - changing list endpoints, ids or response shapes
refs:
  - src/Kvx.Server/Api/Paging.cs#Page
  - src/Kvx.Server/Api/SecretsApi.cs#SecretUri
---

# L-U6KOFNHWDSKDTXXY SDK paging and id parsing are strict

## Observation
Source: Azure.Security.KeyVault.Secrets 4.11.2 (`KeyVaultPipeline`, `KeyVaultIdentifier`, the model readers), read 2026-10-23.
- **nextLink:** the SDK uses it verbatim, as an absolute https URL, and adds no api-version.
- **Ids:** it takes name and version from the item `id`, which must be `/secrets/{name}[/{version}]` with no path prefix. That holds even for deleted secrets, whose `/deletedsecrets/` URL goes only in `recoveryId`.
- **Nulls:** a JSON `null` throws for most fields (tags, attributes, dates, `managed`), while an absent field is fine.

## Why it matters
- A wrong-shaped id or a stray null doesn't fail at the server; it surfaces as a parse exception inside the app's SDK call, far from the cause. That is why responses omit nulls globally and ids are built in one place.
- A vault URL with a path prefix (e.g. hosting kvx under a sub-path) would break id parsing, so vaults must stay at the host root.
