---
date: 2026-10-24
read_if:
  - running several kvx on Windows, or tests that start and stop kvx
refs:
  - tests/Kvx.Tests/LiveKvx.cs#Names
  - src/Kvx.Server/Names/LoopbackDns.cs#Retry
---

# L-GXSEOONO6VE5P4EJ Windows names fail while no process holds port 53

## Observation
With the NRPT rule, Windows asks only kvx's responder for `.localhost`. Between one kvx releasing :53 and a waiting kvx binding it, every lookup fails. CI hit this when a test kvx stopped while the next test was resolving a vault name.

## Why it matters
For a developer, kvx processes are long-lived and the gap is at most the short retry, so a brief takeover is acceptable. A test run starts and stops kvx constantly, so it holds one responder for its whole life and turns off the responder in each test kvx. Anything that cycles kvx quickly on Windows needs the same.
