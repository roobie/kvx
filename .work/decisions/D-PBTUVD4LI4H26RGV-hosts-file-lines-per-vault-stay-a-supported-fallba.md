---
status: accepted
date: 2026-11-11
read_if:
  - changing kvx doctor's name-resolution check
  - a user cannot use port 53 or NRPT
refs:
  - src/Kvx.Server/Cli/DoctorCommand.cs#HostsNames
  - src/Kvx.Server/Cli/DoctorCommand.cs#NameResolution
---

# D-PBTUVD4LI4H26RGV Hosts-file lines per vault stay a supported fallback on every OS

## Context
The responder needs port 53 and, on Windows, a local NRPT rule. ICS can hold port 53 when Hyper-V, WSL or Docker run; Group Policy NRPT rules replace local ones; some users have no admin rights. BR chose this on 2026-10-24.

## Decision
A line per vault in the OS hosts file remains a supported way to resolve vault names. `kvx doctor` names the hosts file in its fix and does not fail a user whose existing vaults resolve through it.

## Why
It is the one mechanism every OS has and corporate policy rarely blocks. `doctor` cannot open the store to list vaults, so the hosts file's own lines under the vault domain stand for that user's vaults; its probe name having no line is not a fault.

## Consequences
Hosts-file users add a line for each new vault themselves; `doctor` warns that a new vault needs one.
