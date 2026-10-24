---
date: 2026-10-24
read_if:
  - dating a reboot or measuring wall time across sleep
refs:
  - src/Kvx.Server/MachineBoot.cs#Time
---

# L-74VLY7JN2QMWLZF5 The uptime clock stops during sleep on Linux and macOS

## Observation
.NET's Environment.TickCount64 is CLOCK_MONOTONIC on Linux and mach_absolute_time on macOS; neither counts while the machine sleeps. Windows' tick count does. Each OS records its boot time itself (`/proc/stat` btime, `kern.boottime`).

## Why it matters
"Now minus uptime" puts the boot later after every lid close, so anything keyed to a reboot (the session token) would end on sleep. Stops being true only if .NET switches to a boot-inclusive clock.
