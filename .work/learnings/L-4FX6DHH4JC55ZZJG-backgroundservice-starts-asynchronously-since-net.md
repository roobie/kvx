---
date: 2026-10-24
read_if:
  - testing or relying on a hosted service having done its work at StartAsync
refs:
  - src/Kvx.Server/Names/LoopbackDns.cs#ExecuteAsync
  - tests/Kvx.Tests/LoopbackDnsTests.cs#Ask
---

# L-4FX6DHH4JC55ZZJG BackgroundService starts asynchronously since .NET 10

## Observation
Since .NET 10, a host's `StartAsync` can return before a BackgroundService has done even the synchronous part of its work. The responder's takeover test sent its first query before the socket was bound and timed out.

## Why it matters
Anything that must be ready once start returns, such as a bound port, needs its own readiness signal, or callers retry the way a stub resolver does. This stays true unless the host goes back to running ExecuteAsync up to its first await.
