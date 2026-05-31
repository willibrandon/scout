# Upstream

This project ports the user-visible error rendering behavior that ripgrep gets
from the Rust `anyhow` crate pinned by `upstream/Cargo.lock`.

```text
name = "anyhow"
version = "1.0.100"
checksum = "a23eb6b1614318a8071c9b2521f36b424b2c83db5eb3a0fead4a6c0809af6e61"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation preserves cause-chain rendering, context joining, and the
verbatim call-site message strings that are compared as stderr bytes by the
differential suite.
