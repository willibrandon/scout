# Upstream Reference

Scout follows ripgrep 15.2.0, release commit
`e89fff89ac9af12e8d4ce9d5fd07beb408ca730f`.

`upstream/Cargo.lock` and `upstream/ripgrep-e89fff89/tests/` are copied
verbatim from that release. Dependency snapshots use the versions and checksums
in the release lockfile.

The local `/Users/brandon/src/ripgrep` checkout is a read-only source of Git
objects. Its current HEAD can advance independently. Reference builds use a
separate checkout under `artifacts/`; preflight inspects the release object and
its Cargo lockfile without changing the local reference checkout.
