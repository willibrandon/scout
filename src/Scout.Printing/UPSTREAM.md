# Upstream

This project ports ripgrep's `grep-printer` workspace crate and replaces the
JSON serializer stack with a byte-identical writer.

```text
name = "grep-printer"
version = "0.3.1"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/printer"

name = "serde_json"
version = "1.0.145"
checksum = "402a6f66d8c709116cf22f558eab210f5a50187f702eb4d7e5ef38d9a7f1c79c"

name = "itoa"
version = "1.0.15"
checksum = "4a5f13b858c8d314ee3e8f639011f7ccefe71f97f96e50151fb991f267928e2c"

name = "ryu"
version = "1.0.20"
checksum = "28d3b2b1366ec20994f1fd18c3c594f05c5dd4bc44d8bb0c1c632c8d6829481f"
```

The implementation owns standard, color, JSON, vimgrep, stats, replacement,
context, and summary output. JSON is written directly as bytes to preserve
escaping, integer formatting, and elapsed-time normalization rules.
