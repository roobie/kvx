---
created: 2026-10-23
---

# B-GATJ5PIU75XWPFKR rudimentary web UI

## Why
BR wants it (2026-10-24). Developers coming from Azure look at and edit secrets in the portal; with only the SDK and the CLI, seeing what a local vault holds takes a command per question.

## Notes
- BR (2026-10-24): two modes. The default only shows what exists (vaults, names, versions, dates), so a screen share or a glance over the shoulder leaks nothing. An explicit mode adds revealing values and basic create, update and delete; the parts that touch values sit together behind one switch.
- BR (2026-10-24): very austere: the operations, no eye-candy. It follows the OS light/dark theme and nothing more.
- BR (2026-10-24), from a sketch: one page per vault with vaults as tabs on top; a secrets table (name, enabled, type, updated, expires) with a filter; a secret page with attributes, tags and versions; a deleted-secrets page. Edit mode is per tab, shown by a banner with a countdown, and expires on a timer back to view mode. View mode never sends a value to the browser; a reveal fetches one value on request.
- BR (2026-10-24): the UI lives in `kvx serve` and is closed until `kvx ui` opens it. A separate UI process would hold the full-access dev token anyway, so it buys little isolation for a second web server's worth of code; closed-until-opened leaves almost nothing exposed while unused. UI routes answer nothing without a session; `kvx ui` gets a one-time code over the token-gated channel and opens the browser with it; sessions expire.
- Browser-facing requirements, because any page in the browser can send requests to loopback: Host header check (DNS rebinding); SameSite=Strict, origin check and per-form token (CSRF); `frame-ancestors 'none'`; a host of its own (e.g. `kvx.localhost`) with a host-only cookie, since cookies on 127.0.0.1 are shared across ports; names, tags and types escaped under a strict CSP with no inline script; revealed values `Cache-Control: no-store`, never in URLs, autocomplete off.
- It is served by kvx itself, so it must not open a way to read secrets that the token-gated API doesn't have.
