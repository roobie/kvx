---
status: accepted
date: 2026-10-23
read_if:
  - changing token lifetime or storage
  - debugging 401s after a kvx restart or relogin
refs:
  - src/Kvx.Server/Auth/DevTokens.cs#DevTokens
---

# D-ELGOBWMLQBGY3M42 Dev tokens live for the login session

## Context
Apps get the token through DefaultAzureCredential, which caches it in memory for as long as the app runs. Under the earlier rule, where a token died with the kvx process, every kvx restart caused 401s in every running app until that app was restarted.

## Decision
One random token per login session, kept in the per-user runtime directory and reused across kvx restarts. The same value is the IDENTITY_HEADER (BR, 2026-10-23).

## Why
- Per process: secure, but every restart breaks the dev loop.
- Persistent on disk: convenient, but it leaves a long-lived bearer secret at rest, which is exactly what kvx exists to avoid.
- Login session: the runtime directory is tmpfs, private to the user and wiped at logout or reboot, so it keeps restart tolerance without anything outliving the session.

## Consequences
- Anything running as the user in that session can read the token. That is the same trust boundary as the keyring that unlocks the store.
- Deleting the token file revokes it at the next kvx start.
- A real identity provider would replace this rather than extend it.
