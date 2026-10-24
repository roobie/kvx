---
date: 2026-10-24
read_if:
  - choosing where kvx keeps state on macOS
refs:
  - src/Kvx.Server/MacUserDirs.cs#Cache
---

# L-FNGZKPQZQZDEW7Y7 macOS purges the per-user temp dir after days without access

## Observation
The confstr(3) man page says files in `_CS_DARWIN_USER_TEMP_DIR` (`/var/folders/…/T`, .NET's Path.GetTempPath) may be removed when not accessed for 3 days; dirhelper does it. Apple DTS says the details are not API and vary by release. The sibling `_CS_DARWIN_USER_CACHE_DIR` (`…/C`) is equally private and not auto-cleaned.

## Why it matters
A long-running kvx touches its token file only at start, so after a quiet weekend its clients would find no token. Anything kvx keeps on macOS that must outlive idleness belongs in the cache dir (or Application Support), never in temp.
