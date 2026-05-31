# Upstream

This project ports ripgrep's `grep-cli` workspace crate and the terminal color
behavior from the pinned lockfile.

```text
name = "grep-cli"
version = "0.1.12"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/cli"

name = "termcolor"
version = "1.4.1"
checksum = "06794f8f6c5c898b3275aebefa6b8a1cb24cd2c6c79397ab15774837a0bc5755"

name = "winapi-util"
version = "0.1.11"
checksum = "c2a7b1c03c876122aa43f3020e6c3c3ee5c05081c9a00739faf7503aeba10d22"
```

The implementation owns the one-type-per-file flag definitions, short/long flag
lexing surface, CLI byte escape/unescape helpers, human-size parsing, terminal
color decisions, stdin/stdout/stderr probing, and Windows console helper
behavior replaced by direct `LibraryImport` declarations.
