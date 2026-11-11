---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-11-11
success_criteria:
  - HelpTests.A_topic_prints_its_guide_and_where_it_is_online normalizes both sides' line endings
  - ci.yml uses actions whose runtime is node24
  - CI green on ubuntu, windows and macos at the pushed commit
---

# task/ci-green-node24

## Purpose
CI is red on Windows (HelpTests compares a CRLF checkout of docs/passthrough.md against LF-normalized output) and its actions run on deprecated Node 20.

## Scope
In: TODO
Out: TODO

## Context & constraints
TODO: what is already true, what must stay true, resources available.


## Now
Commit and push; watch CI on all three OSes

## Next
- TODO

## Evidence
<!-- Proof against success_criteria: test runs, commits, measurements. -->
- Release build + HelpTests 8/8 pass locally on Linux (CRLF path not reproducible here; Windows CI is the check)
- action.yml at checkout v7.0.1, setup-dotnet v6.1.0, upload-artifact v7.0.2 all declare using: node24; release notes v5..v7 list no break affecting ci.yml's inputs

## Harvest
<!-- Candidates found along the way. Finalize promotes or drops every unchecked item:
     - [ ] decision: …   → record decision
     - [ ] learning: …   → record learning
     - [ ] backlog: …    → record backlog
     - [ ] process: …    → retrospective (change the process, or drop it)
-->

## Log
- 2026-11-11 initialized
- 2026-11-11 phase initialize → execute
