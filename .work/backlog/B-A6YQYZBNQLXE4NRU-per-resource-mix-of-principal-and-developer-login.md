---
created: 2026-10-26
---

# B-A6YQYZBNQLXE4NRU Per-resource mix of principal and developer login in passthrough

## Why
An app might need the principal for some resources and the developer's login for others, e.g. where no principal role exists yet. The principal is now the only source, to avoid silent fallback to the developer (D-WHMYBXHZAGDFWJNC).

## Notes
Any mix must be explicit per resource, never a fallback chain.
