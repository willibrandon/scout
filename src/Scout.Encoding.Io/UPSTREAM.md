# Upstream

This project ports the streaming transcoder behavior of the Rust
`encoding_rs_io` crate pinned by `upstream/Cargo.lock`.

```text
name = "encoding_rs_io"
version = "0.1.7"
checksum = "1cc3c5651fb62ab8aa3103998dade57efdd028544bd300516baa31840c252a83"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation adapts `Scout.Encoding` decoders to the searcher read loop,
including BOM handling, streaming replacement behavior, and byte/line boundary
preservation for `-E` and auto-detected encodings.
