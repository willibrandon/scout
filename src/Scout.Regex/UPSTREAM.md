# Upstream

This project ports ripgrep's `grep-regex` workspace crate.

```text
name = "grep-regex"
version = "0.1.14"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/regex"
```

The implementation owns regex configuration translation, line terminator
handling, multi-line mode, fixed-string promotion, Unicode/byte-mode switches,
case-folding decisions, and the bridge from CLI options to `Scout.Automata`.
