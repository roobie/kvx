---
created: 2026-11-11
---

# B-NIZUSQYWZQYYOPTR Real-machine Windows name-resolution cases CI can't show

## Why
Milestone windows-macos (U-VFQBGPLYZ3PEJFQG) closed on CI plus BR's Windows 11 machine (2026-11-11). Some Windows setups were in neither, and kvx's name resolution or `doctor` advice could be wrong there unnoticed.

## Notes
- the NRPT rule on a Home edition (research found no edition limit);
- Group Policy NRPT rules overriding the local rule (L-K2NFBEP4B6XGJAXB): `doctor` should land on the hosts-file fix;
- ICS (SharedAccess) holding port 53 when Hyper-V, WSL or Docker run;
- `kvx doctor` on a machine without the NRPT rule, whose fix text CI never shows.

Worth doing when a user reports one of them, or when a machine with that setup is at hand.
