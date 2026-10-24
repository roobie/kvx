---
date: 2026-10-24
read_if:
  - writing a PowerShell step in a GitHub workflow
refs:
  - .github/scripts/trust-test-ca.ps1
---

# L-5CXDZHTMFVYCIM5K Inline pwsh steps can't start with using namespace

## Observation
The Actions runner puts its own line ahead of an inline `shell: pwsh` step. PowerShell accepts `using namespace` only as a script's first statement, so such a step fails before it runs.

## Why it matters
Any step that needs `using` has to live in a script file. This holds as long as the runner keeps adding that line.
