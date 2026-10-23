---
created: 2026-10-23
---

# B-4YCFBMDYVWGYKPV6 Pin kvx's content root for the packaged tool

## Why
ASP.NET resolves appsettings.json against the current directory. Starting the built kvx from anywhere else fails option validation with "Domain is required" and similar errors, which is confusing for anyone running the tool from their project directory.

## Notes
- Seen on 2026-10-23 during the passthrough smoke test.
- Fix it as part of dotnet tool packaging.
