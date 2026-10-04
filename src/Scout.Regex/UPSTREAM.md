# Upstream

This project ports ripgrep's `grep-regex` workspace crate.

```text
name = "grep-regex"
version = "0.1.14"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/regex"
```

The implementation owns regex configuration translation, line terminator
handling, multi-line mode, fixed-string promotion, Unicode/byte-mode switches,
case-folding decisions, and the bridge from CLI options to `Scout.Automata`.
