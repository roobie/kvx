---
date: 2026-10-23
read_if:
  - claiming a command changes nothing on the machine
refs:
  - src/Kvx.Server/Cli/DoctorCommand.cs#KeyProtector
---

# L-R6BNNVTPKKC3UCCC The systemd-creds probe can create the user credential key

## Observation
The only way to know whether the key protector works is to use it. On a machine where nothing has used `systemd-creds --user` yet, that first use creates the user's credential key.

## Why it matters
"The doctor changes nothing" is true once kvx has run, which is when people usually need the doctor; before that, the probe makes the same change kvx's first start would. Don't promise a strictly read-only probe unless the protector offers a check that doesn't encrypt anything.
