# Upstream

This project ports the Rust `regex-automata` crate pinned by
`upstream/Cargo.lock` and replaces the high-level `regex` facade behavior that
ripgrep consumes.

```text
name = "regex-automata"
version = "0.4.13"
checksum = "5276caf25ac86c8d810222b3dbb938e512c55c6831a10f3e6ed1c93b84041f1c"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"

name = "regex"
version = "1.12.2"
checksum = "843bc0191f75f3e22651ae5f1e72939ab2f72a4bc30fa80a066bd66edefc24d4"
```

The implementation covers NFA construction, PikeVM execution, prefilter
selection, bounded backtracking, dense/sparse/lazy/one-pass DFA tiers, and the
meta engine/`PatternSet` surface used by glob and ignore matching.
