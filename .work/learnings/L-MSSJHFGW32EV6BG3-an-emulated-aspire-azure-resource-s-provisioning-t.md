---
date: 2026-10-23
read_if:
  - an Aspire AppHost hangs waiting for a connection string or output of an emulated Azure resource
  - upgrading Aspire.Hosting.Azure
refs:
  - src/Kvx.Aspire.Hosting/KvxHostingExtensions.cs#MarkProvisioned
---

# L-MSSJHFGW32EV6BG3 An emulated Aspire Azure resource's provisioning task is armed in run mode and never completed

## Observation
In Aspire 13.6.3, reading a Bicep output waits on the resource's provisioning task. That task is created in run mode for every Azure resource, emulated or not, but the provisioner skips emulated ones and so never completes it. Reading the source suggested the opposite; only the live run showed the hang ("Waiting for value for connection string from resource …").

## Why it matters
Any integration that emulates an Azure resource and supplies its outputs must complete that task itself, and again whenever Aspire re-arms it before a resource starts. If a later Aspire completes it for emulated resources, `MarkProvisioned` becomes a harmless no-op.
