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
  - `kvx help <topic>` prints that doc from inside the binary, offline, followed by its online link pinned to the commit the binary was built from (test)
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
- Bundled docs match the installed version and work offline, as `chirp help` does; the printed online link is pinned to the build's commit so it matches too (the repo has no release tags).
- Docs are content, not code: one set of files, embedded at build, never duplicated in C# strings.
- U-PEPFZ7IO6GKWIV4F (per-app principal) is waiting on BR's live check and changes README text; keep its README section intact when splitting.

## Now
Docs, `kvx help`, per-command `--help` and tests done; 103/103 pass.

## Next
- BR reads the guides and `kvx help` output; then finalize.

## Evidence
- 1: tests/Kvx.Tests/HelpTests.cs#Help_lists_the_commands_and_one_line_per_goal
- 2: docs/ holds get-started, passthrough, aspire, import, troubleshoot; tests/Kvx.Tests/HelpTests.cs#The_readme_links_every_guide
- 3: tests/Kvx.Tests/HelpTests.cs#A_topic_prints_its_guide_and_where_it_is_online (link matches a 40-hex sha)
- 4: tests/Kvx.Tests/HelpTests.cs#An_unknown_topic_lists_the_known_ones, #A_command_with_help_prints_its_usage
- 5: tests/Kvx.Tests/HelpTests.cs#Every_doc_is_a_topic_and_every_topic_a_doc
- `dotnet test`: 103 passed, 0 failed (2026-10-26)

## Harvest
- [ ] decision: guides are docs/*.md embedded at build and listed by their title line, so help, README and binary cannot drift; online link pinned to the build's commit since there are no tags
- [ ] decision: command synopses live in Help.Commands beside dispatch, and each command's usage error reuses them
- [ ] decision: `--help` is honoured only directly after the verb, so `kvx run app --help` reaches the app
- [ ] backlog: pin online links to a release tag once releases are tagged

## Log
- 2026-10-26 initialized at BR's request, after the per-app principal work
- 2026-10-26 phase initialize → execute
- 2026-10-26 README split into five goal guides; `kvx help <topic>` and `<command> --help` added
