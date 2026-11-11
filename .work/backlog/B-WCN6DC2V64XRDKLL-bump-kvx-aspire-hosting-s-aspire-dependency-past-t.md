---
created: 2026-11-11
---

# B-WCN6DC2V64XRDKLL Bump Kvx.Aspire.Hosting's Aspire dependency past the GHSA-f7vh-h5g3-7jrh advisory

## Why
`dotnet pack` of Kvx.Aspire.Hosting warns NU1903: `Aspire.Hosting` 13.6.3, pulled by `Aspire.Hosting.Azure.KeyVault` 13.6.3, has a high-severity advisory (GHSA-f7vh-h5g3-7jrh). The package's dependency is a minimum, so consumers resolving a newer Aspire are unaffected, but 1.0.0 should not advertise a vulnerable floor. 1.0.0-alpha.1 already shipped with it (2026-11-11).

## Notes
- Bump in src/Kvx.Aspire.Hosting/Kvx.Aspire.Hosting.csproj to the first Aspire release that fixes the advisory, and re-run the Aspire tests (Aspire.Hosting.Testing) on all three OSes.
- Belongs before milestone v1-nuget's 1.0.0 tag (U-EEYVGWIWPQYCXCEU).
