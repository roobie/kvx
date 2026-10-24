---
date: 2026-10-24
read_if:
  - configuring Kestrel certificates or investigating a hang while kvx starts
refs:
  - src/Kvx.Server/KvxApp.cs#Build
---

# L-EFA6TVY3SAIWZCD5 Kestrel's certificate file reload can deadlock at startup

## Observation
By default Kestrel watches the certificate file it serves and reloads it when the file changes. A hang dump from CI caught the watcher's lock and a file-change token's monitor each waiting on the other. The certificate had been written just before kvx started. It explains intermittent startup hangs seen locally and on CI, on every OS.

## Why it matters
Hot reload of the certificate is worth less than a kvx that always starts, and replacing a certificate in the store already needs a restart. kvx keeps reload off. Revisit if Kestrel fixes the lock ordering and hot reload becomes wanted.
