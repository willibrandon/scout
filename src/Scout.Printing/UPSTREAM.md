# Upstream

This project ports ripgrep's `grep-printer` workspace crate and replaces the
JSON serializer stack with a byte-identical writer.

```text
name = "grep-printer"
version = "0.3.1"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/printer"

name = "serde_json"
version = "1.0.150"
checksum = "e8014e44b4736ed0538adeecded0fce2a272f22dc9578a7eb6b2d9993c74cfb9"

name = "itoa"
version = "1.0.18"
checksum = "8f42a60cbdf9a97f5d2305f08a87dc4e09308d1276d28c869c684d7777685682"

name = "zmij"
version = "1.0.23"
checksum = "29666d0abbfad1e3dc4dcf6144730dd3a3ab225bbbdac83319345b1b44ccfc1b"
```

The implementation owns standard, color, JSON, vimgrep, stats, replacement,
context, and summary output. JSON is written directly as bytes to preserve
escaping, integer formatting, and elapsed-time normalization rules.
