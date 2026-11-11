---
date: 2026-11-11
read_if:
  - changing the CI runner images
  - the Linux key protector fails in CI
refs:
  - .github/workflows/ci.yml
---

# L-PWT6I4EOU3KMW7XO GitHub's ubuntu-24.04 runner has systemd 255, too old for systemd-creds --user

## Observation
`ubuntu-latest` (24.04) ships systemd 255, whose `systemd-creds` has no `--user`. `ubuntu-26.04` ships 259, where `systemd-creds --user` round trips as the runner user with only the system's `systemd-creds.socket`, no user manager (spike run 40037860344).

## Why it matters
Linux CI is pinned to `ubuntu-26.04` until `ubuntu-latest` moves to it; on an older image every store test fails at the key protector, not for a kvx reason.
