---
status: accepted
date: 2026-10-23
read_if:
  - adding a kvx subcommand
  - touching the store from outside the server
refs:
  - src/Kvx.Server/Cli/KvxCli.cs#Dispatch
  - src/Kvx.Server/Cli/KvxClient.cs#Connect
---

# D-L4OU7YMFJ6W6OENA kvx is one binary; client commands go through the serving kvx

## Context
Developers need helpers (run, env, import) beside the server. The store is one sealed file held in memory by the server, and the test host starts the entry point with no arguments.

## Decision
One `kvx` tool. No verb, or an argument that is not a verb, serves. The helper verbs are clients of a kvx that is already serving: they reach it over its loopback listener with the session token, and never open the store.

## Why
A separate client binary would mean two packages and two settings files to keep in step. If the helpers opened the store directly, a second writer would race the server's in-memory copy and lose updates. Proving the server is reachable before acting also means `kvx run` never starts an app that would silently fall back to another credential. Serving by default keeps WebApplicationFactory and `kvx --urls=…` working unchanged.

## Consequences
Every helper fails when kvx is not serving, and says how to start it. Helpers and server must agree on `urls` and `Kvx:TokenFile`, so both read the same configuration next to the binary. A new verb must not collide with a configuration argument.
