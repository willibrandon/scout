# Upstream

This project ports the Rust `regex-syntax` crate pinned by
`upstream/Cargo.lock`.

```text
name = "regex-syntax"
version = "0.8.8"
checksum = "7a2d987857b319362043e95f5353c0535c1f58eec5336fdfcf626430af7def58"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation establishes the parser, AST/HIR-facing syntax surface,
inline flag handling, repetition/group/alternation semantics, and Unicode
table provenance required by the regex conformance suite. Unicode data is
vendored under `upstream/` and advances only with the ripgrep reference pin.
