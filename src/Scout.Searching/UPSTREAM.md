# Upstream

This project ports ripgrep's `grep-searcher` workspace crate and replaces the
memory-map dependency with .NET-native mapping primitives.

```text
name = "grep-searcher"
version = "0.1.17"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/searcher"

name = "memmap2"
version = "0.9.11"
checksum = "d1219ed1b7f229ee7104d281dd01d6802fe28bb6e95d292942c4daacdeb798c0"
```

The implementation owns mmap-vs-read heuristics, binary detection, line
iteration, before/after context coordination, match callbacks, multi-line
buffering, thread planning, and the search-loop fuzz target.
