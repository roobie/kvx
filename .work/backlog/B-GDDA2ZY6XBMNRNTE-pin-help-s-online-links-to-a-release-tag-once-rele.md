---
created: 2026-10-26
---

# B-GDDA2ZY6XBMNRNTE Pin help's online links to a release tag once releases are tagged

## Why
The online link in `kvx help <topic>` points at a commit sha because the repo has no tags. A tag reads better and survives history rewrites. See D-S74OUJIWELCKTGJE.

## Notes
Do it when release tagging starts; derive the tag from the package version.
