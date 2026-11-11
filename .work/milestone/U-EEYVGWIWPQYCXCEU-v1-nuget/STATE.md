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
- Both package ids are unclaimed on nuget.org (flat-container index 404, 2026-11-11). `kvx` is short and could be taken before release.
- Both projects are at 0.1.0 and the repo has no tags. `Kvx.Aspire.Hosting` tells users to `dotnet tool install -g kvx`, which only works once the tool is published (src/Kvx.Aspire.Hosting/KvxDescription.cs).
- Help's online links point at a commit sha because there are no tags (D-S74OUJIWELCKTGJE); this milestone's tag lets them pin to it.

### Origin
Promoted from backlog: B-GDDA2ZY6XBMNRNTE Pin help's online links to a release tag once releases are tagged

### Why
The online link in `kvx help <topic>` points at a commit sha because the repo has no tags. A tag reads better and survives history rewrites. See D-S74OUJIWELCKTGJE.

### Notes
Do it when release tagging starts; derive the tag from the package version.

## Now
TODO: what is in flight right now. Rewrite this freely; it is a cursor, not a history.

## Next
- Verify whether nuget.org Trusted Publishing (GitHub OIDC) works for BR's account; otherwise an API key secret scoped to the two ids.
- Consider reserving the ids early (a prerelease push) given `kvx` is short.
- Slice candidates: license-and-metadata; store-format-version; stable-surface decision; release-job; published-install-check; docs-and-tag-links.

## Evidence
<!-- Proof against success_criteria: test runs, commits, measurements. -->

## Harvest
<!-- Candidates found along the way. Finalize promotes or drops every unchecked item:
     - [ ] decision: …   → record decision
     - [ ] learning: …   → record learning
     - [ ] backlog: …    → record backlog
     - [ ] process: …    → retrospective (change the process, or drop it)
-->

## Log
- 2026-11-11 initialized
- 2026-11-11 phase initialize → execute
