# Upstream

This project ports the Rust `aho-corasick` crate pinned by
`upstream/Cargo.lock`.

```text
name = "aho-corasick"
version = "1.1.3"
checksum = "8e60d3430d3a69478ad0993f19238d2df97c507009a52b3c10addcd7f6bcb916"
```

The implementation establishes byte-preserving automaton construction,
materialized and span-enumerator standard non-overlapping search, standard
overlapping search, leftmost modes, ASCII case-insensitive matching, and
anchored/unanchored start-kind enforcement for the supported builder options.
Later milestone work extends this project with the full upstream builder
matrix, packed prefilters, DFA variants, stream search, and the ported
upstream conformance suite.
