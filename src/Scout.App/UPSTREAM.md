# Upstream

This project ports ripgrep's `rg` binary surface from the pinned reference
checkout and owns the command-line execution flow.

```text
name = "ripgrep"
version = "15.1.0"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/core"
```

It also ports the argument-lexing and help-wrapping behavior used by the
binary:

```text
name = "lexopt"
version = "0.3.1"
checksum = "9fa0e2a1fcbe2f6be6c42e342259976206b383122fc152e872795338b5a3f3a7"

name = "textwrap"
version = "0.16.2"
checksum = "c13547615a44dc9c452a8a534638acdf07120d4b6847c8178705da06306a3057"
```

The implementation covers native raw-argument intake, top-level option
application, search dispatch, generated completions/man-page output, version
output, and user-facing diagnostic formatting through `Scout.Errors` and
`Scout.Diagnostics`.
