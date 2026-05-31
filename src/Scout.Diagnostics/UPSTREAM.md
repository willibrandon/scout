# Upstream

This project ports ripgrep's observable logging behavior and the Rust `log`
crate call-site shape used by `--debug` and `--trace`.

```text
name = "log"
version = "0.4.28"
checksum = "34080505efa8e45a4b816c349525ebe327ceaa8559756f0356cba97ef3bf7432"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

Scout does not port a generic logging framework. It preserves the byte-visible
diagnostic levels, prefixes, and message placement that the differential suite
compares against the pinned `rg` binary.
