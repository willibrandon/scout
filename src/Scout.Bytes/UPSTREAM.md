# Upstream

This project ports the byte-string behavior of the Rust `bstr` crate pinned by
`upstream/Cargo.lock`.

```text
name = "bstr"
version = "1.13.0"
checksum = "1f7dc094d718f2e1c1559ad110e27eeaae14a5465d3d56dd6dbd793079fbd530"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation owns byte-preserving string operations used by CLI parsing,
ignore/glob matching, searcher input, printer output, lossy display, ASCII
casing, and raw path/argument interop.
