# Upstream

This project ports the Rust `regex-syntax` crate pinned by
`upstream/Cargo.lock`.

```text
name = "regex-syntax"
version = "0.8.11"
checksum = "d6f6ff9a378485b298a5286656da665ba74413d36db0979633275d2e708145d4"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation establishes the parser, AST/HIR-facing syntax surface,
inline flag handling, repetition/group/alternation semantics, and Unicode
table provenance required by the regex conformance suite. Unicode data is
vendored under `upstream/` and advances only with the ripgrep reference pin.
