# Upstream

This project ports the streaming transcoder behavior of the Rust
`encoding_rs_io` crate pinned by `upstream/Cargo.lock`.

```text
name = "encoding_rs_io"
version = "0.1.7"
checksum = "1cc3c5651fb62ab8aa3103998dade57efdd028544bd300516baa31840c252a83"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation adapts `Scout.Encoding` decoders to the searcher read loop,
including BOM handling, streaming replacement behavior, and byte/line boundary
preservation for `-E` and auto-detected encodings.
