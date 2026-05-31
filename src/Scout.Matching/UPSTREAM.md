# Upstream

This project ports ripgrep's `grep-matcher` workspace crate.

```text
name = "grep-matcher"
version = "0.1.8"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/matcher"
```

The implementation owns the matcher abstraction, match spans, sink callbacks,
line iteration contracts, and the byte-offset semantics consumed by regex,
PCRE2, searcher, and printer layers.
