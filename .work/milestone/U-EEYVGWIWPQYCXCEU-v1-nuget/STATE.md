---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-11-11
success_criteria:
  - kvx and Kvx.Aspire.Hosting 1.0.0 are on nuget.org under MPL-2.0 (LICENSE in the repo, PackageLicenseExpression), with readme, repository and project URL in the package metadata
  - the packages are pushed only by a GitHub Actions job triggered by the v1.0.0 tag, after the 3-OS test matrix passes at that commit
  - on fresh ubuntu, windows and macos runners, kvx installed from nuget.org with dotnet tool install -g serves and round-trips an unmodified DefaultAzureCredential app
  - the store file carries a format version, and a test loads a store written by 1.0 from a checked-in fixture
  - 'a decision record names the surface 1.x keeps stable: commands, flags, Kvx:* settings and the store format'
  - README and docs/get-started install from nuget.org, and kvx help's online links point at the release tag
  - slice per-app-principal is delivered before the v1.0.0 tag
---

# milestone/v1-nuget

## Purpose
kvx is installable today only by packing it from source. 1.0 puts `kvx` and `Kvx.Aspire.Hosting` on nuget.org, so the audience gets it with one `dotnet tool install`, and makes the promise a 1.0 implies: a store, command line and configuration that 1.x keeps working.

## Scope
In: MPL-2.0 license; package metadata; a tag-triggered release job; fresh-machine install checks against the published packages on all three OSes; store format versioning with a 1.0 fixture; a decision naming the 1.x-stable surface; docs installing from nuget.org; help links pinned to the release tag.
Out: signing or notarising beyond nuget.org's repository signature; installers other than the dotnet tool; other target frameworks than net10.0 (decide here only if an Aspire AppHost on an older TFM turns out to need it).

## Context & constraints
- BR chose (2026-11-11): license MPL-2.0; release by CI on a `vX.Y.Z` tag after the 3-OS matrix passes; all four 1.0 promises (store format, CLI and config stability, nuget.org install docs, fresh-machine install check).
- slice/per-app-principal (U-PEPFZ7IO6GKWIV4F) is finished before the 1.0.0 tag (BR, 2026-11-11), so 1.0 ships the per-app principal story the docs already describe.
- Both package ids are claimed by BR's account: 1.0.0-alpha.1 of each was pushed by hand on 2026-11-11 to reserve them (`kvx` is short). A pushed version can be unlisted but never deleted or changed, so every version string is spent once.
- `Kvx.Aspire.Hosting` pulls `Aspire.Hosting` 13.6.3, which `dotnet pack` flags with a high-severity advisory (GHSA-f7vh-h5g3-7jrh); alpha.1 shipped with it as the minimum.
- Both projects are at 0.1.0 and the repo has no tags. `Kvx.Aspire.Hosting` tells users to `dotnet tool install -g kvx`, which only works once the tool is published (src/Kvx.Aspire.Hosting/KvxDescription.cs).
- Help's online links point at a commit sha because there are no tags (D-S74OUJIWELCKTGJE); this milestone's tag lets them pin to it.

### Origin
Promoted from backlog: B-GDDA2ZY6XBMNRNTE Pin help's online links to a release tag once releases are tagged

### Why
The online link in `kvx help <topic>` points at a commit sha because the repo has no tags. A tag reads better and survives history rewrites. See D-S74OUJIWELCKTGJE.

### Notes
Do it when release tagging starts; derive the tag from the package version.

## Now
Both ids reserved with 1.0.0-alpha.1 (push accepted 2026-11-11 ~17:28; nuget.org still indexing at 17:29, flat-container 404). License done; readme and the Aspire dependency bump are the rest of criterion 1's package work.

## Next
- Confirm both alpha.1 packages are listed after validation (nuget.org emails on failure).
- Verify whether nuget.org Trusted Publishing (GitHub OIDC) works for BR's account, now that the ids exist; otherwise a CI secret holding an API key scoped to the two ids.
- Package readme (PackageReadmeFile) for both packages; pack warns "Readme missing".
- Slice candidates: store-format-version; stable-surface decision; release-job; published-install-check; docs-and-tag-links.

## Evidence
- 1 (partial): LICENSE holds Mozilla's MPL-2.0 text; both csproj carry PackageLicenseExpression MPL-2.0 and RepositoryUrl (6c6590b; nuspecs checked after pack). No readme yet.
- 1.0.0-alpha.1 of kvx and Kvx.Aspire.Hosting packed at 6c6590b and pushed by hand with BR's API key (`dotnet nuget push`: "Created", exit 0 for both).

## Harvest
- [ ] decision: prereleases are 1.0.0-alpha.N (sorting below 1.0.0) rather than 0.x; the first was pushed by hand only to reserve the ids, and later ones follow the release job
- [x] backlog: bump Aspire.Hosting.Azure.KeyVault past 13.6.3 (GHSA-f7vh-h5g3-7jrh in Aspire.Hosting) before 1.0.0 → B-WCN6DC2V64XRDKLL

## Log
- 2026-11-11 initialized
- 2026-11-11 phase initialize → execute
- 2026-11-11 MPL-2.0 LICENSE and package license metadata (6c6590b)
- 2026-11-11 1.0.0-alpha.1 of both packages pushed by hand to reserve the ids
