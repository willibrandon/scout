# Upstream

This project ports the Rust `aho-corasick` crate pinned by
`upstream/Cargo.lock`.

```text
name = "aho-corasick"
version = "1.1.4"
checksum = "ddd31a130427c27518df266943a5308ed92d4b226cc639f5a8f1002816174301"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation establishes byte-preserving automaton construction,
materialized and span-enumerator standard non-overlapping search, standard
overlapping search, leftmost modes, ASCII case-insensitive matching, and
anchored/unanchored start-kind enforcement for the supported builder options.
This file records source provenance and covered surfaces only. Scope is not
parked here; any uncovered upstream case must be represented by an
implementation change, a conformance test, or a zero-entry release ledger in
`docs/PARITY.md`.
