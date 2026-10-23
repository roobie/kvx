---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - an unmodified SecretClient passes conformance tests for list secrets and list versions across more than one page, update properties (enabled, content type, tags, expiry), delete (operation completes), get and list deleted, recover (operation completes, value readable), purge (name reusable) and backup → purge → restore (all versions back)
  - the SDK surfaces the service errors for setting a name that is deleted but recoverable, restoring over an existing name, and getting or purging an unknown deleted secret
  - deleted state survives a restart, and a deleted secret past its scheduled purge date is gone
  - after the full lifecycle, neither the data directory nor a backup blob contains a secret name or value in plaintext
  - the Caddy end-to-end test still passes
---

# milestone/secrets-conformance

## Purpose
The project's first success criterion: an app using any part of the secrets API, not just set/get, works unchanged against kvx. This milestone closes the API surface so the remaining milestones (credentials, protectors, packaging) build on a complete server.

## Scope
In: list secrets, list versions, update properties, delete/get deleted/list deleted/recover/purge, backup/restore, lazy expiry of deleted secrets, paging.
Out: managed (certificate-backed) secrets; the keys and certificates APIs; DefaultAzureCredential; behaviour of real Azure beyond what the SDK and REST spec state.

## Context & constraints
- No Azure subscription here, so fidelity is to the SDK source and the REST spec, not to recorded live traffic. Where they are silent, kvx picks the simplest behaviour and the gap is noted, not claimed.
- Backup blobs leave kvx, so they must be sealed like the store; sealing with the store's data key also matches Azure's rule that a backup restores only within its boundary (here: this machine's key).
- Everything still goes through the single sealed store; deleted secrets are part of it.

## Now
All criteria shown by tests; moving to finalize.

## Next
- Finalize: harvest, close, update the project.

## Evidence
- Wire details checked against Azure.Security.KeyVault.Secrets 4.11.2 source and the REST spec (2025-07-01): paging, item ids, operation polling, backup shape, null handling. The error codes for the conflict cases and the 404 for versions of a missing secret are not documented there; kvx's choices for them are unconfirmed against a real vault.
- `dotnet test` 17/17 (2026-10-23), 9 new in ConformanceTests, all through a stock SecretClient:
  - criterion 1: secrets and versions each listed across >1 page (30 items, page size 25); update of enabled/content type/expiry/tags, with disabled → 403 and the value untouched; delete and recover operations complete; get/list deleted; purge frees the name; backup → delete → purge → restore returns both versions.
  - criterion 2: set on a deleted name → 409 Conflict; restore over a live name → 409 Conflict; random bytes as backup → 400; get deleted, purge, recover and delete of unknown names → 404 SecretNotFound.
  - criterion 3: a deleted secret is still there after restart; after 90 days on a fake clock it is gone and its name is reusable.
  - criterion 4: canary name and value run through set, update, backup, delete, recover and delete again; neither appears in any file in the data dir or in the backup blob.
  - criterion 5: the Caddy E2E test is part of the 17.

## Harvest
<!-- Candidates found along the way. Finalize promotes or drops every unchecked item:
     - [ ] decision: …   → record decision
     - [ ] learning: …   → record learning
     - [ ] backlog: …    → record backlog
     - [ ] process: …    → retrospective (change the process, or drop it)
-->
- [ ] decision: backup blobs are sealed with the store's data key (restorable only on this machine), never plaintext
- [ ] learning: the SDK's delete/recover operations treat 404 as pending and 200/403 as done; delete completes at once when recoveryId is absent
- [ ] learning: the SDK follows nextLink verbatim (it must carry api-version), parses every id as /secrets/{name}[/{version}] even for deleted secrets, and throws on JSON null for most fields
- [ ] backlog: confirm error fidelity against a real vault: 409 codes for deleted-name reuse and restore-over-existing, and 404 vs empty list for versions of a missing secret

## Log
- 2026-10-23 initialized
- 2026-10-23 wire details verified from SDK source; phase initialize → execute
- 2026-10-23 full secrets API implemented; 17/17 tests
- 2026-10-23 phase initialize → execute
