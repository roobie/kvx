# Plan

One milestone, worked as one stream of commits; the API pieces share the repository and route table, so parallel slices would mostly collide.

## Steps
- [x] Seal/unseal for backup blobs alongside the store (distinct AAD)
- [x] Storage model for deleted secrets
- [x] Verify wire details from SDK source and the REST spec (paging, deleted bundle, operations' polling, backup shape, conflict errors)
- [x] Repository: list, versions, update, delete, deleted get/list, recover, purge, backup, restore; lazy purge of expired deleted secrets via TimeProvider
- [x] API: routes, paging (`nextLink`, keyset by id, the service's fixed maximum of 25), DTOs, error codes
- [x] Conformance tests per criterion, using a FakeTimeProvider for purge expiry
- [x] Extend the no-plaintext scan to the full lifecycle and to backup blobs
- [x] Full test run including Caddy E2E

## Verification
- Criteria 1–2: one test per operation through a stock SecretClient over TestServer; paging tests create more secrets/versions than one page holds (30 against the page size of 25).
- Criterion 3: delete, restart, get deleted; advance fake time past the purge date, then get deleted is 404 and the name is settable.
- Criterion 4: run every operation on canary name/value, then scan every file under the data dir and the backup bytes.
- Criterion 5: `dotnet test` including the E2E trait.
