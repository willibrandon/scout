# Upstream Sync Policy

Scout currently follows ripgrep 15.2.0 at
`e89fff89ac9af12e8d4ce9d5fd07beb408ca730f`.

For an upstream update:

1. Inspect the release Git object in the read-only reference checkout. Build
   reference executables in a separate artifact checkout with
   `eng/checkout-ripgrep-reference.sh`.
2. Copy the release lockfile, integration tests, dependency regressions, and
   source-generation inputs. Review every changed dependency and record the
   behavioral disposition.
3. Verify unchanged Unicode and encoding data, or regenerate changed data.
4. Update project provenance, notices, CLI identity, and generated artifacts.
5. Capture default and PCRE2 references on all seven native targets, import
   their verified archives and receipts, and run the library, CLI, package,
   native, completion, and existing performance gates.
6. Record intentional byte-level deviations in `docs/PARITY.md` with guarding
   tests. Link results for the final candidate commit in the resolving PR.

The local reference checkout's HEAD does not need to equal the release.
SDK policy updates are separate work.

## Lockfile Entries With No Scout Port

The crates below appear in the pinned `upstream/Cargo.lock` but do not get a
Scout project or `UPSTREAM.md` because they have no shipped Scout behavioral
surface. If one of these starts affecting observable bytes, move it to the
project-specific provenance files and add conformance coverage before advancing
the pin.

| Crate(s) | Version(s) | Disposition |
|----------|------------|-------------|
| `arbitrary`, `derive_arbitrary` | `1.4.2`, `1.4.2` | Rust-side fuzz/dev support for upstream crates; Scout uses the `fuzz/Scout.Fuzz` SharpFuzz harness instead. |
| `cc`, `find-msvc-tools`, `jobserver`, `pkg-config`, `shlex` | `1.2.67`, `0.1.9`, `0.1.35`, `0.3.33`, `2.0.1` | Rust build-script support; Scout's native builds are checked-in scripts under `native/`. |
| `cfg-if` | `1.0.4` | Rust conditional-compilation helper; no standalone runtime behavior is ported. |
| `crossbeam-channel`, `crossbeam-epoch`, `crossbeam-utils` | `0.5.16`, `0.9.20`, `0.8.22` | Transitive support crates under `crossbeam-deque`/`ignore`; Scout pins observable walker behavior in `Scout.Ignore` tests. |
| `getrandom`, `r-efi` | `0.4.3`, `6.0.0` | Pulled in through Rust build/test support; Scout has no equivalent shipped dependency. |
| `glob` | `0.3.3` | Standalone Rust path-expansion helper; Scout path enumeration lives in `Scout.Os` and search traversal lives in `Scout.Ignore`. |
| `proc-macro2`, `quote`, `syn`, `unicode-ident` | `1.0.106`, `1.0.46`, `2.0.119`, `1.0.24` | Rust procedural-macro implementation support for upstream derives; no Scout runtime surface. |
| `serde`, `serde_core`, `serde_derive` | `1.0.228`, `1.0.228`, `1.0.228` | Upstream serializer/derive support; Scout writes JSON explicitly in `Scout.Printing` and ports only byte-visible formatting. |
| `tikv-jemallocator`, `tikv-jemalloc-sys` | `0.7.0`, `0.7.1+5.3.1-0-g81034ce1f1373e37dc865038e1bc8eeecf559ce8` | Upstream's musl allocator swap; allocator choice has no output surface and throughput is enforced by perf gates. |
| `windows-link` | `0.2.1` | Rust Windows binding link metadata; Scout declares OS calls directly with `LibraryImport`. |
