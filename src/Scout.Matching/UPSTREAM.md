# Upstream

This project ports ripgrep's `grep-matcher` workspace crate.

```text
name = "grep-matcher"
version = "0.1.9"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/matcher"
```

The implementation owns the matcher abstraction, match spans, sink callbacks,
line iteration contracts, and the byte-offset semantics consumed by regex,
PCRE2, searcher, and printer layers.
