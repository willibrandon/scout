# Upstream

This project ports ripgrep's observable logging behavior and the Rust `log`
crate call-site shape used by `--debug` and `--trace`.

```text
name = "log"
version = "0.4.33"
checksum = "0ceec5bc11778974d1bcb055b18002eba7f4b3518b6a0081b3af5f21666da9ad"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

Scout does not port a generic logging framework. It preserves the byte-visible
diagnostic levels, prefixes, and message placement that the differential suite
compares against the pinned `rg` binary.
