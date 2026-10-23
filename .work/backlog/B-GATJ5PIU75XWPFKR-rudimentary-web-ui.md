---
created: 2026-10-23
---

# B-GATJ5PIU75XWPFKR rudimentary web UI

## Why
BR wants it (2026-10-24). Developers coming from Azure look at and edit secrets in the portal; with only the SDK and the CLI, seeing what a local vault holds takes a command per question.

## Notes
- Scope to settle with BR when it is picked up: read-only or editing too; which vaults, versions and deleted secrets it shows.
- It is served by kvx itself, so it must not open a way to read secrets that the token-gated API doesn't have.
