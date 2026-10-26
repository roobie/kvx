# Find out why something does not work

```sh
kvx doctor
```

Run it first. It checks what kvx needs from the machine (vault names resolving to loopback, a valid and trusted certificate for them, the key protector, a serving kvx, and each stored service principal signing in) and says how to fix each problem, because those problems otherwise show up as SDK errors that don't name the cause. Give it the same settings as the kvx you serve, for example `kvx doctor --Kvx:Domain=…`.

Problems doctor cannot see from outside the app:

- **An Azure client other than Key Vault fails with `AuthenticationFailedException`.** With the default passthrough, sign in in the session kvx runs in (`az login`, `azd auth login`, Visual Studio or `Connect-AzAccount`). With passthrough off, other services are not reachable through kvx by design. See `kvx help passthrough`.
- **The error names a client id kvx holds no principal for.** The app's environment has `AZURE_CLIENT_ID` set. Store a principal for it, add `--for <id>` to the one that stands in for it, or unset it. See `kvx help passthrough`.
- **A principal signs in but the app is denied.** A new principal has no role assignments; give it the ones production's identity has.
- **curl or another non-.NET tool rejects the certificate on Linux.** OpenSSL needs `SSL_CERT_DIR`; start the tool with `kvx run`, or set it with `kvx env`.
- **The store cannot be read on Windows after a password reset.** An administrator resetting your password (rather than you changing it) makes DPAPI-protected data, and with it the store, unreadable.

Setting up the certificate and name resolution is in `kvx help get-started`.
