---
created: 2026-10-23
---

# B-GATJ5PIU75XWPFKR rudimentary web UI

## Why
BR wants it (2026-10-24). Developers coming from Azure look at and edit secrets in the portal; with only the SDK and the CLI, seeing what a local vault holds takes a command per question.

## Notes
- BR (2026-10-24): values stay hidden by default; revealing them is a separate, explicit mode. The UI is mostly for seeing what exists (vaults, names, versions, dates), and a screen share or a glance over the shoulder must not leak values.
- Looks and behaviour are still to be settled with BR before building: layout, read-only or editing, deleted secrets, how reveal is switched on (per value, per session, a CLI flag at serve time).
- It is served by kvx itself, so it must not open a way to read secrets that the token-gated API doesn't have.
