---
created: 2026-10-24
---

# B-S4QOEVFRPNPP2T5P portable export and import of a vault

## Why
The data key is bound to this machine's keyring, and backups are sealed with it (D-S5MKFASWUWOQWFH7), so losing or replacing the machine loses the secrets, and a teammate can't be handed a vault. The project design lists this as a risk (DESIGN.md, Risks). `kvx import` today only takes user-secrets.

## Notes
- Open: what protects the exported file (passphrase, a recipient's public key, or plaintext with a loud flag); whether export carries versions and deleted secrets or only current values; whether the web UI (B-GATJ5PIU75XWPFKR) offers it.
- The export path is the one deliberate exception to "no plaintext at rest", so its default must not write plaintext.
