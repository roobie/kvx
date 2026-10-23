---
status: accepted
date: 2026-10-23
read_if:
  - changing how kvx opens or guards its data dir
  - running more than one kvx, or kvx in tests
refs:
  - src/Kvx.Server/Storage/SealedStore.cs#Lock
  - src/Kvx.Server/Storage/SealedStore.cs#StoreInUseException
---

# D-ZSYSODY6DTOL5U54 One serving kvx per store, enforced by an OS file lock held while serving

## Context
A second kvx writing the same store silently loses the first one's updates (D-L4OU7YMFJ6W6OENA). Once an Aspire AppHost can start kvx on its own, a developer running their own kvx alongside it makes that likely, not rare.

## Decision
kvx holds an exclusive OS lock on a file in the data dir for as long as it serves. A second server refuses, and says which kvx holds the store.

## Why
- A PID file goes stale on a crash and needs liveness guessing; PIDs get reused.
- A port probe says something is listening, not that it owns this store, and races at startup.
- The OS releases the lock when the process dies, however it dies, so it is never stale and needs no cleanup.

## Consequences
- Clients still never open the store; only the server takes the lock.
- Tests that run kvx in-process hold the store, so a second kvx in the same test must use another data dir or expect the refusal.
- Start-or-reuse (D-CRKLZYSNBG2LVRW7) builds on the refusal.
