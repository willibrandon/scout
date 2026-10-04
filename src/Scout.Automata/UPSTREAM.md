# Upstream

This project ports the Rust `regex-automata` crate pinned by
`upstream/Cargo.lock` and replaces the high-level `regex` facade behavior that
ripgrep consumes.

```text
name = "regex-automata"
version = "0.4.15"
checksum = "1f388202e4b80542a0921078cc23b6333bcf1409c1e3f86404cae4766a6131db"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"

name = "regex"
version = "1.13.0"
checksum = "2a0e75113e14dc5acb068cd0786884f214f1312650a3d36d269f5c4f3cdee8a2"
```

The implementation covers NFA construction, PikeVM execution, prefilter
selection, bounded backtracking, dense/sparse/lazy/one-pass DFA tiers, SIMD byte
counting for line-number and multiline accounting, and the meta
engine/`PatternSet` surface used by glob and ignore matching.
