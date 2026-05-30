# Upstream Reference

Scout is ported against the local ripgrep checkout at:

```text
/Users/brandon/src/ripgrep
```

The required `HEAD` is:

```text
4857d6fa67db69a95cd4b6f2adda5d807d4d0119
```

`upstream/Cargo.lock` is vendored verbatim from that checkout. The preflight
script in `eng/preflight.sh` verifies the checkout and SDK pins before porting
or regenerating artifacts.
