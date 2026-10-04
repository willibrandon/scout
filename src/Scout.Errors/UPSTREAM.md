# Upstream

This project ports the user-visible error rendering behavior that ripgrep gets
from the Rust `anyhow` crate pinned by `upstream/Cargo.lock`.

```text
name = "anyhow"
version = "1.0.103"
checksum = "2a4385e2e34eb35d6b3efe798b9eb88096925d87726c0798709bf56d9ed84af3"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
```

The implementation preserves cause-chain rendering, context joining, and the
verbatim call-site message strings that are compared as stderr bytes by the
differential suite.
