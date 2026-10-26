---
status: accepted
date: 2026-10-26
read_if:
  - adding or renaming a help topic or guide
  - changing how kvx help finds guides or builds the online link
refs:
  - src/Kvx.Server/Cli/Help.cs#Topics
  - src/Kvx.Server/Cli/Help.cs#Online
  - src/Kvx.Server/Kvx.Server.csproj
---

# D-S74OUJIWELCKTGJE Guides are docs/*.md embedded in the binary, linked online at the build's commit

## Context
`kvx help` listed commands but got no one started in any mode; the how-to lived only in the README, which nothing pointed to. BR wanted help routed by what the user wants to achieve.

## Decision
One markdown guide per goal in docs/, built into the binary; `kvx help` lists them by their titles and `kvx help <topic>` prints one, followed by its online copy at the commit the binary was built from.

## Why
- Help that only links to the web fails offline and can describe a different version than the one installed. Bundling, as `chirp help` does, avoids both.
- Guide text held in C# strings would duplicate the docs and drift from them. Docs are content and stay as files.
- The repo has no release tags, so the build's commit is the only reference that matches the binary. `main` would drift.

## Consequences
- A new goal is one new file in docs/ with a `# ` title; it shows up in help with no code change.
- Tests tie docs/, the binary's topics and the README links together, so renaming a guide means updating the README in the same change.
- A build outside git links to `main`.
