# Upstream

This project ports ripgrep's `rg` binary surface from the pinned reference
checkout and owns the command-line execution flow.

```text
name = "ripgrep"
version = "15.2.0"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/core"

name = "grep"
version = "0.4.1"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/grep"
disposition = "workspace facade folded into Scout project references"
```

It also ports the argument-lexing and help-wrapping behavior used by the
binary:

```text
name = "lexopt"
version = "0.3.2"
checksum = "803ec87c9cfb29b9d2633f20cba1f488db3fd53f2158b1024cbefb47ba05d413"

name = "textwrap"
version = "0.16.2"
checksum = "c13547615a44dc9c452a8a534638acdf07120d4b6847c8178705da06306a3057"
```

The implementation covers native raw-argument intake, top-level option
application, search dispatch, generated completions/man-page output, version
output, and user-facing diagnostic formatting through `Scout.Errors` and
`Scout.Diagnostics`.
