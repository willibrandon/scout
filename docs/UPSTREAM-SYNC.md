# Upstream Sync Policy

Scout targets one ripgrep revision at a time. The current pin is:

```text
4857d6fa67db69a95cd4b6f2adda5d807d4d0119
```

To advance the pin:

1. Update the reference checkout at `/Users/brandon/src/ripgrep`.
2. Verify `git -C /Users/brandon/src/ripgrep rev-parse HEAD` is the intended
   new commit.
3. Replace `upstream/Cargo.lock` with the reference checkout's `Cargo.lock`.
4. Regenerate vendored Unicode data and update `upstream/UNICODE-VERSION`.
5. Update dependency `UPSTREAM.md` files for every affected ported dependency.
6. Rebuild the Native AOT entry spike for all six RIDs.
7. Run the full unit, integration, differential, encoding, regex, and
   performance gates.
8. Record any intentional byte-level deviation in `docs/PARITY.md` with a
   guarding test.

The pin never advances as part of a drive-by dependency update.

## Lockfile Entries With No Scout Port

The crates below appear in the pinned `upstream/Cargo.lock` but do not get a
Scout project or `UPSTREAM.md` because they have no shipped Scout behavioral
surface. If one of these starts affecting observable bytes, move it to the
project-specific provenance files and add conformance coverage before advancing
the pin.

| Crate(s) | Version(s) | Disposition |
|----------|------------|-------------|
| `arbitrary`, `derive_arbitrary` | `1.4.2` | Rust-side fuzz/dev support for upstream crates; Scout uses the `fuzz/Scout.Fuzz` SharpFuzz harness instead. |
| `cc`, `find-msvc-tools`, `jobserver`, `pkg-config`, `shlex` | `1.2.41`, `0.1.4`, `0.1.34`, `0.3.32`, `1.3.0` | Rust build-script support; Scout's native builds are checked-in scripts under `native/`. |
| `cfg-if` | `1.0.4` | Rust conditional-compilation helper; no standalone runtime behavior is ported. |
| `crossbeam-channel`, `crossbeam-epoch`, `crossbeam-utils` | `0.5.15`, `0.9.18`, `0.8.21` | Transitive support crates under `crossbeam-deque`/`ignore`; Scout pins observable walker behavior in `Scout.Ignore` tests. |
| `getrandom`, `r-efi`, `wasip2`, `wit-bindgen` | `0.3.4`, `5.3.0`, `1.0.1+wasi-0.2.4`, `0.46.0` | Pulled in through Rust build/test support; Scout has no equivalent shipped dependency. |
| `glob` | `0.3.3` | Standalone Rust path-expansion helper; Scout path enumeration lives in `Scout.Os` and search traversal lives in `Scout.Ignore`. |
| `proc-macro2`, `quote`, `syn`, `unicode-ident` | `1.0.101`, `1.0.41`, `2.0.107`, `1.0.20` | Rust procedural-macro implementation support for upstream derives; no Scout runtime surface. |
| `serde`, `serde_core`, `serde_derive` | `1.0.228` | Upstream serializer/derive support; Scout writes JSON explicitly in `Scout.Printing` and ports only byte-visible formatting. |
| `tikv-jemallocator`, `tikv-jemalloc-sys` | `0.6.1`, `0.6.1+5.3.0-1-ge13ca993e8ccb9ba9847cc330696e02839f328f7` | Upstream's musl allocator swap; allocator choice has no output surface and throughput is enforced by perf gates. |
| `windows-link` | `0.2.1` | Rust Windows binding link metadata; Scout declares OS calls directly with `LibraryImport`. |
