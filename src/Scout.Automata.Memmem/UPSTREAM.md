# Upstream

This project ports the byte-search surface of the Rust `memchr` crate pinned by
`upstream/Cargo.lock`.

```text
name = "memchr"
version = "2.8.3"
checksum = "cf8baf1c55e62ffcace7a9f06f4bd9cd3f0c4beb022d3b367256b91b87513d98"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation establishes the byte-preserving public surface used by later
regex, glob, and searcher ports, including single-result, materialized, and
span-enumerator forward/reverse searches for one, two, and three bytes plus substring searches.
Substring search also includes span enumerators and reusable forward and reverse finder types for
fixed needles. One-, two-, and three-byte forward and reverse searches have
explicit `System.Runtime.Intrinsics` paths for AVX-512, AVX2, SSE2, and
AdvSimd/NEON, all `IsSupported`-gated with scalar fallback. The remaining
byte-search forms keep the same byte-preserving API and scalar/span fallback
behavior.
