---
phase: execute
parent: U-RPH4YXDO25THRKKI
owner:
  - BR
  - claude
created: 2026-10-23
success_criteria:
  - through Caddy, one unmodified DefaultAzureCredential process gets kvx's token for Key Vault and the developer's own token (from the upstream login) for another Azure resource
  - when the developer's login is unavailable, a request for another resource fails with 400 and a message saying how to sign in; never a status the client retries
  - an empty upstream credential list in configuration turns passthrough off and restores the refusal
  - the README states the remaining limits accurately and still leads with them
---

# milestone/az-passthrough

## Purpose
Let apps that use Key Vault plus other Azure services adopt kvx with no code change. Without this, the "unmodified app" pitch excludes most real apps.

## Scope
In: passthrough of non-kvx resources to the developer's own credentials (az, azd, Visual Studio), configurable and able to be disabled; clear failures; README.
Out: caching upstream tokens in kvx (the app's MSAL already caches per resource); choosing tenants per app; managed-identity client_id/object_id selection.

## Context & constraints
- MSAL caches managed-identity tokens per requested scope (msal 4.90 `ManagedIdentityAuthRequest.cs` sets the cache scope from the request, not the response). Tokens from different issuers therefore coexist in one app.
- Security: kvx gives a real token only to callers that hold the session token. That file is readable only by the same user, who could run `az account get-access-token` themselves, so passthrough grants nothing beyond what the user already has. A browser page cannot set X-IDENTITY-HEADER without a CORS preflight, which kvx does not answer.
- kvx must not use DefaultAzureCredential for the upstream: if kvx itself ran with IDENTITY_ENDPOINT set, it would ask itself. The upstream is an explicit list of developer credentials.
- Upstream errors must map to 400, never 5xx, because the client retries 5xx.

### Origin
Promoted from backlog: B-ONCYO4FXWOO2VFDB Let kvx-backed apps reach other Azure services

## Now
All criteria hold; ready to finalize.

## Next
- Finalize: harvest, close.

## Evidence
- Criterion 1: `CaddyEndToEndTests.Unmodified_app_round_trips_through_caddy` passes. In one process, the sample app's DefaultAzureCredential gets kvx's token for Key Vault, then the developer-login token for `https://storage.azure.com/.default`. The developer login is a fake, so tests never reach a real az session.
- Criterion 2:
  - `IdentityTests.Without_a_developer_login_other_resources_are_400_saying_how_to_sign_in` passes.
  - Live smoke test on 2026-10-23, real kvx with the default credential list and no CLIs installed: HTTP 400 in 0.25s, listing each credential tried and why it failed.
- Criterion 3: `IdentityTests.With_passthrough_off_other_resources_are_400_with_a_reason` and `Passthrough_config_rejects_unknown_credentials` pass.
- Criterion 4: the README warning now names the remaining limits: a login is required, the app acts as the developer, and the session token reaches Azure tokens.
- `mise exec -- dotnet test`: 26/26.

## Harvest
- [ ] decision: passthrough is on by default; it grants nothing beyond the user's own login (see constraints)
- [ ] backlog: kvx reads appsettings.json from the current directory, so starting the built dll from elsewhere fails validation; the packaged tool must pin its content root

## Log
- 2026-10-23 initialized from B-ONCYO4FXWOO2VFDB; MSAL per-scope caching verified from source; phase initialize → execute
