---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - in an AppHost, `AddAzureKeyVault("<name>").RunAsKvx()` plus `WithReference` lets an unmodified app (Aspire's Key Vault client integration with its defaults) set and get secrets in vault `<name>` on kvx, in run mode, with no Azure subscription or provisioning
  - if the user's kvx is already serving, the AppHost uses it, and a secret put there with `kvx import` is visible to the app; otherwise the AppHost starts kvx, shows it with its logs in the dashboard, and stops it when the AppHost stops
  - two kvx servers can never both write one store; a second `kvx serve` on the same data dir refuses and says which kvx holds it, shown by a test
  - a .NET project and a non-.NET executable that reference the vault both complete a trusted TLS handshake with no trust setup beyond `dotnet dev-certs https --trust`, on Linux
  - in publish mode `RunAsKvx` changes nothing, so the real Key Vault is provisioned as without it, shown by a test
  - the hosting package is packed beside the tool, the README has an Aspire section, and a sample AppHost runs end to end on BR's machine with the installed tool
---

# milestone/aspire-hosting

## Purpose
Aspire is where most of kvx's audience composes their dev environment. With an Aspire integration, an app that already uses Aspire's Key Vault client gets kvx in local runs and a real Key Vault when published, with no code change and no Azure subscription for local secrets.

## Scope
In: a hosting package (`RunAsKvx` on the Azure Key Vault resource); start-or-reuse of the user's kvx; identity and trust wiring for the resources that reference the vault; a store lock in kvx; a machine-readable description of a serving kvx if the hosting package needs one; tests through Aspire.Hosting.Testing; README; a sample AppHost.
Out: a client integration package (Aspire's own is used unchanged); publish-mode behaviour beyond "unchanged"; containers that reference the vault (their trust and loopback reachability differ; backlog if wanted); Windows/macOS.

## Context & constraints
- BR chose (2026-10-23) to use the user's kvx: reuse it if it is serving, otherwise start it. Not a kvx per AppHost, and not attach-only.
- Clients never open the store, and a second writer loses updates (D-L4OU7YMFJ6W6OENA). Today nothing stops a second server on the same data dir, and an AppHost starting kvx makes that likely.
- Apps reach kvx as their managed identity through IDENTITY_ENDPOINT and IDENTITY_HEADER (D-KKZWEBYCP6FAKSHB). DefaultAzureCredential then asks kvx for every resource (L-GPX6ZOUCE2IAT4SR), so the app's other Azure resources depend on passthrough (D-BGGXSS3A5S3YOP24). Competing credential variables must go (D-IJMXLBG5U3LFTH62).
- Vault hosts are `<vault>.dev.localhost`, served with the dev cert (D-LLPISGPW4SP4BB4K); challenge verification is a suffix match (L-7BL5CLN57QWZWCQU).
- Aspire 13.6.3 is current. It serves and trusts the ASP.NET Core dev cert itself, and adds dev-cert trust to non-.NET resources through SSL_CERT_DIR. The community Key Vault emulator's `RunAsEmulator` is prior art for redirecting the vault resource.

## Now
README has the Aspire section; `dotnet pack src/Kvx.Aspire.Hosting -o ./artifacts` packs beside the tool (artifacts/ now ignored).

## Next
- Smoke on BR's machine: reinstall the tool (the installed one lacks `describe` and `--reuse`), run the sample AppHost against the developer's kvx and the dev cert.

## Evidence
- Criterion 3: `A_second_server_on_the_same_store_refuses_and_names_the_first` passes; the test run holds the store, and a second `kvx` process exits 1 naming its process id. 47/47 tests.
- Start-or-reuse lives in kvx: `Reuse_uses_the_serving_kvx_and_serves_once_it_stops` and `Reuse_refuses_a_holder_that_answers_with_other_settings` pass; `kvx describe` is covered by `Describe_says_where_apps_find_kvx_without_connecting`. 50/50.
- Spike (reading Aspire 13.6.3 source): an `EmulatorResourceAnnotation` keeps a Bicep resource out of provisioning, role assignments and the Azure commands; a `vaultUri` written into `Outputs` is what `UriExpression` and the connection string resolve to. Correction from the live run: the resource's provisioning task *is* armed in run mode and nothing completes it, so `RunAsKvx` completes it (at start and again before each resource starts).
- Criteria 1, 2, 4, 5 through Aspire.Hosting.Testing on the sample AppHost (53/53):
  - `Starts_kvx_when_none_serves_and_an_unmodified_app_round_trips`: the AppHost starts kvx; the worker (Aspire's Key Vault client, defaults, with AZURE_CLIENT_SECRET etc. set on it) sets and reads a secret; curl gets an HTTP status over trusted TLS; after the AppHost stops, the store is free again.
  - `Uses_the_kvx_already_serving_and_its_imported_secrets`: with a kvx already serving and `kvx import` done, the AppHost's kvx defers to it, the worker sees the imported secret, and the developer's kvx outlives the AppHost.
  - `Publish_mode_is_untouched`: same annotations and resources as without `RunAsKvx`.
- Trust: Aspire 13.6 sets its own SSL_CERT_DIR on every resource (a per-resource certs dir plus the system dirs), overwriting one set by an environment callback. kvx's certificates therefore go in through a `CertificateAuthorityCollection` on each referencing resource, and Aspire writes them out in a form both .NET and curl accept.

## Harvest
- [ ] decision: one serving kvx per store, enforced by an OS file lock held while serving (released by the OS on a crash, so never stale), not a PID file or a port probe
- [ ] decision: start-or-reuse is kvx's own `serve --reuse`, not logic in the hosting package: DCP keeps owning the process and its logs, the AppHost never decides in advance, and the AppHost's kvx takes over if the developer's stops
- [ ] decision: tools learn kvx's endpoints from `kvx describe` (kvx's own layered configuration) instead of reading appsettings or assuming ports
- [ ] decision: redirect the Azure resource (emulator annotation + `vaultUri` output) rather than add a new resource type, so publish mode and the app's client integration stay Aspire's own
- [ ] learning: an emulated Azure resource's provisioning task is still armed in run mode and never completed, so its outputs hang until the integration completes it
- [ ] decision: kvx's trust reaches Aspire resources as a CertificateAuthorityCollection, not SSL_CERT_DIR, because Aspire owns that variable and writes certs in a form non-.NET clients accept

## Log
- 2026-10-23 initialized; BR chose start-or-reuse of the user's kvx
- 2026-10-23 phase initialize → execute
- 2026-10-23 hosting package, sample and Aspire tests pass end to end (two Aspire surprises: armed provisioning task, Aspire-owned SSL_CERT_DIR)
