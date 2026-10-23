# Design

## Problem
An Aspire AppHost declares a Key Vault with `AddAzureKeyVault`, and in run mode Aspire provisions a real vault in the developer's subscription. Developers who want local secrets need a subscription, a login, and a vault per developer, and the secrets still leave the machine. kvx should stand in for that vault in run mode, with no change to the app.

## Approach
- **Redirect, not replace.** `RunAsKvx()` on the existing Azure Key Vault resource marks it as an emulator, so Aspire skips provisioning, and points its vault URI at `https://<resource name>.<kvx domain>:<port>`. Apps keep `WithReference` and Aspire's Key Vault client; publish mode is untouched.
- **One kvx per user.** A kvx resource in the dashboard stands for the user's kvx. When the AppHost starts, it first probes for a serving kvx the way the CLI does; if one answers, the resource shows it as external and the AppHost starts nothing. Otherwise the AppHost starts `kvx serve` and owns that process.
- **kvx describes itself.** The hosting package asks the kvx CLI for the endpoint, token and vault URI pattern instead of reproducing kvx's configuration rules, so its appsettings and overrides stay the one source of truth.
- **Identity per reference.** Every resource that references a kvx-backed vault gets the identity variables, waits for kvx, and has the competing credential variables cleared, as `kvx run` does.
- **Trust comes from the dev cert.** Aspire already makes its resources trust the dev cert; the integration only checks this holds and adds nothing of its own unless a test shows a gap.
- **Store lock.** kvx takes an exclusive lock on its data dir while serving, so start-or-reuse can never end with two writers.

## Alternatives considered
- A kvx per AppHost, with its own store and Aspire-assigned ports: isolated, but imported secrets and `kvx run` would see a different store. BR chose the shared one.
- Attach-only (Aspire never starts kvx): simplest, but fails the project criterion that the integration starts kvx.
- `AddKvx()` as a new resource type instead of redirecting `AddAzureKeyVault`: apps would need different wiring in dev and prod, which is what kvx exists to avoid.
- The hosting package reading kvx's appsettings itself: duplicates configuration resolution and drifts.

## Risks & open questions
- How the dashboard shows "reused, not started" depends on Aspire's custom-resource APIs; an executable resource cannot be skipped. Settle in the spike.
- The tool must be on PATH with a compatible version. Decide whether the package checks the version or `kvx doctor` covers it.
- Aspire's executables inherit the AppHost's environment. Whether setting a competing variable to empty is enough for the Azure SDK to ignore it needs checking.
- Passthrough means the app's other Azure resources still need the developer's login; the README's up-front caveat applies to Aspire too.
- Aspire's HTTPS and trust APIs are experimental (ASPIRECERTIFICATES001) and may change.
