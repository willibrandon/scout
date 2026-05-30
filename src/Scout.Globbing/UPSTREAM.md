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
Later milestone work extends this project with the complete upstream strategy
matrix and the ported upstream test suite.
