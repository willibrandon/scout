# Upstream

This project ports the byte-string behavior of the Rust `bstr` crate pinned by
`upstream/Cargo.lock`.

```text
name = "bstr"
version = "1.12.0"
checksum = "234113d19d0d7d613b40e86fb654acf958910802bcceab913a4f9e7cda03b1a4"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation owns byte-preserving string operations used by CLI parsing,
ignore/glob matching, searcher input, printer output, lossy display, ASCII
casing, and raw path/argument interop.
