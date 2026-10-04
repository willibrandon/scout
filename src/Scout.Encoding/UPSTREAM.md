# Upstream

This project ports WHATWG-compatible decoding behavior from the Rust
`encoding_rs` crate pinned by `upstream/Cargo.lock`.

```text
name = "encoding_rs"
version = "0.8.35"
checksum = "75030f3c4f45dafd7586dd6780965a8c7e8e285a5ecb86713e63a79c5b2766f3"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation owns label lookup, UTF-8/UTF-16 handling, single-byte
tables, Japanese/Korean/Chinese legacy decoders, replacement semantics, and the
encoding conformance vectors used by the search pipeline.
