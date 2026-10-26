---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-26
success_criteria:
  - `kvx help` lists the commands and, under "What do you want to do?", one line per goal naming its topic (test)
  - each goal has one doc under docs/ (get started, passthrough modes including per-app principals, Aspire, user-secrets import, troubleshooting), and the README keeps the overview and links to them
  - `kvx help <topic>` prints that doc from inside the binary, offline, followed by its online link pinned to the release tag (test)
  - an unknown topic lists the known ones and exits 1; `kvx <command> --help` prints that command's usage and exits 0 (tests)
  - a test fails when a docs/ file has no topic or a topic has no file, so the binary and the repo never drift apart
---

# slice/goal-help

## Purpose
`kvx help` lists commands but gets no one started in any mode: first run, the passthrough modes and per-app principals live only in the README, and nothing points there. BR asked for help that routes by what the user wants to achieve.

## Scope
In: splitting the README into goal docs; embedding them; `kvx help <topic>`; per-command `--help`; the goal section in `kvx help`.
Out: rendering markdown in the terminal (plain text is enough); man pages; translating docs.

## Context & constraints
- BR, 2026-10-26: chose goal-categorized docs that help points to; agreed to open this as its own slice.
- Bundled docs match the installed version and work offline, as `chirp help` does; the printed online link is pinned to the release tag so it matches too.
- Docs are content, not code: one set of files, embedded at build, never duplicated in C# strings.
- U-PEPFZ7IO6GKWIV4F (per-app principal) is waiting on BR's live check and changes README text; keep its README section intact when splitting.

## Now
Initialized.

## Next
- Read the README sections and decide the topic list.
- Move each section into docs/; embed docs/*.md as resources.
- `kvx help [topic]`, per-command `--help`, tests.

## Evidence

## Harvest

## Log
- 2026-10-26 initialized at BR's request, after the per-app principal work
- 2026-10-26 phase initialize → execute
