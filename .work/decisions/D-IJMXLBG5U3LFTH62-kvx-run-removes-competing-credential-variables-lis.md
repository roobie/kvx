---
status: accepted
date: 2026-10-23
read_if:
  - an app under kvx run authenticates as something other than kvx
  - changing Kvx:RunUnset
refs:
  - src/Kvx.Server/Cli/RunCommand.cs#Execute
  - src/Kvx.Server/appsettings.json
---

# D-IJMXLBG5U3LFTH62 kvx run removes competing credential variables, listed in configuration

## Context
Some credentials sit ahead of managed identity in DefaultAzureCredential's chain: environment service principals and workload identity. Inside the managed-identity source, other sources (MSI, IMDS, Service Fabric) are also checked before App Service. Developer shells often carry these variables, and then the app quietly authenticates as something other than kvx.

## Decision
`kvx run` and `kvx env` remove the variables listed in `Kvx:RunUnset` from the app's environment, and name each one they remove.

## Why
The other options were to only warn, which leaves the app broken, or to refuse to run, which forces the developer to clean up their shell. Removal fixes the app run without touching the developer's shell. Naming each removal keeps it visible. The list follows the Azure SDK's choices, which change between releases, so it is content and can be edited without a rebuild.

## Consequences
An app that really needs one of these variables under `kvx run` needs an override that drops it from `Kvx:RunUnset`. New SDK variables that outrank managed identity have to be added to the list.
