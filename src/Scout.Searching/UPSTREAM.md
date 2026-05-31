# Upstream

This project ports ripgrep's `grep-searcher` workspace crate and replaces the
memory-map dependency with .NET-native mapping primitives.

```text
name = "grep-searcher"
version = "0.1.16"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/searcher"

name = "memmap2"
version = "0.9.9"
checksum = "744133e4a0e0a658e1374cf3bf8e415c4052a15a111acd372764c55b4177d490"
```

The implementation owns mmap-vs-read heuristics, binary detection, line
iteration, before/after context coordination, match callbacks, multi-line
buffering, thread planning, and the search-loop fuzz target.
