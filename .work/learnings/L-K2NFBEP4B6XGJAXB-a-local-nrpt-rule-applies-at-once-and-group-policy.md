---
date: 2026-11-11
read_if:
  - a Windows user's vault names do not resolve
  - changing the Windows name-resolution setup
refs:
  - src/Kvx.Server/Cli/DoctorCommand.cs#NameResolution
---

# L-K2NFBEP4B6XGJAXB A local NRPT rule applies at once, and Group Policy NRPT rules silently replace all local ones

## Observation
A rule added with `Add-DnsClientNrptRule` took effect immediately on Windows Server 2025, with no restart or cache flush (spike run 40037860344). The milestone's research found that when any Group Policy NRPT rule exists, the local rules are ignored; that part has not been tested on a real machine.

## Why it matters
`kvx doctor` can tell a user to add the rule and re-check at once. On a domain-joined machine with policy NRPT rules, the local rule does nothing and the hosts file is the way.
