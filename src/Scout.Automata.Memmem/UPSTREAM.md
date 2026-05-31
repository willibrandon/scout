# Upstream

This project ports the byte-search surface of the Rust `memchr` crate pinned by
`upstream/Cargo.lock`.

```text
name = "memchr"
version = "2.7.6"
checksum = "f52b00d39961fc5b2736ea853c9cc86238e165017a493d1d5c8eac6bdc4cc273"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
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
