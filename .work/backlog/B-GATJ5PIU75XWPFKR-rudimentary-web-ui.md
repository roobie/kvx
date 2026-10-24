---
created: 2026-10-23
---

# B-GATJ5PIU75XWPFKR rudimentary web UI

## Why
BR wants it (2026-10-24). Developers coming from Azure look at and edit secrets in the portal; with only the SDK and the CLI, seeing what a local vault holds takes a command per question.

## Notes
- BR (2026-10-24): two modes. The default only shows what exists (vaults, names, versions, dates), so a screen share or a glance over the shoulder leaks nothing. An explicit mode adds revealing values and basic create, update and delete; the parts that touch values sit together behind one switch.
- BR (2026-10-24): very austere: the operations, no eye-candy. It follows the OS light/dark theme and nothing more.
- Still to settle with BR before building: layout, deleted secrets (list, recover, purge), how the mode is switched on (per session in the UI, or a flag at serve time).
- It is served by kvx itself, so it must not open a way to read secrets that the token-gated API doesn't have.
