---
created: 2026-10-23
---

# B-MDJXFAWWOZWP6FIO Confirm error fidelity against a real vault

## Why
kvx promises that apps behave the same in dev and prod, and that includes their error handling. Some error responses aren't documented in the SDK source or the REST spec, so kvx's current answers are educated guesses. An app that branches on them could behave differently against real Azure.

## Notes
- To check: setting a deleted-but-recoverable name (kvx: 409 Conflict, inner ObjectIsDeletedButRecoverable); restoring over an existing name (kvx: 409 Conflict); listing versions of a missing secret (kvx: 404 SecretNotFound); an unknown deleted secret (kvx: 404 SecretNotFound); a bad backup blob (kvx: 400 BadParameter).
- Needs a real vault with soft delete. Recording the responses as fixtures would let the conformance tests assert them exactly.
