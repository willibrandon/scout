# Upstream

This project ports behavior from ripgrep's workspace `globset` crate at the
repository commit pinned by `upstream/REFERENCE.md`.

```text
name = "globset"
version = "0.4.18"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation establishes byte-preserving glob and glob-set matching for
literals, wildcards, recursive wildcards, character classes, brace
alternatives, escaping, separator-aware matching, builder-style construction,
ordered match collection, matches-into reuse, and prepared candidate path
support, including required-extension and component-suffix candidate filtering.
Covered tests include the upstream match/non-match matrix cases for recursive
wildcards, brace alternatives, case-insensitive matching, backslash escaping,
literal-separator character classes, upstream literal, extension-only, and
required-extension extraction cases, plus Scout's prefix/suffix candidate
filters for recursive and wildcard patterns. This file records source
provenance and covered surfaces only. Scope is not parked here; any uncovered
upstream case must be represented by an implementation change, a conformance
test, or a zero-entry release ledger in `docs/PARITY.md`.
