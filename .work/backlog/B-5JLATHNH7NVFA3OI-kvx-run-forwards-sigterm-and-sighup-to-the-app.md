---
created: 2026-10-23
---

# B-5JLATHNH7NVFA3OI kvx run forwards SIGTERM and SIGHUP to the app

## Why
When `kvx run` is stopped by something other than a terminal Ctrl-C (an IDE stop button, `kill`, a closed terminal), only kvx gets the signal. The app may be orphaned or never shut down gracefully.

## Notes
Ctrl-C already reaches the app through the process group, and kvx ignores it and waits. Forwarding could use PosixSignalRegistration to relay the signal to the child and then wait for its exit code.
