# Upstream

This project ports the Rust `aho-corasick` crate pinned by
`upstream/Cargo.lock`.

```text
name = "aho-corasick"
version = "1.1.3"
checksum = "8e60d3430d3a69478ad0993f19238d2df97c507009a52b3c10addcd7f6bcb916"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation establishes byte-preserving automaton construction,
materialized and span-enumerator standard non-overlapping search, standard
overlapping search, leftmost modes, ASCII case-insensitive matching, and
anchored/unanchored start-kind enforcement for the supported builder options.
This file records source provenance and covered surfaces only. Scope is not
parked here; any uncovered upstream case must be represented by an
implementation change, a conformance test, or a zero-entry release ledger in
`docs/PARITY.md`.
