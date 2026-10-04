# ripgrep 15.2.0 release audit

The comparison is from `4857d6fa67db69a95cd4b6f2adda5d807d4d0119` to release
`e89fff89ac9af12e8d4ce9d5fd07beb408ca730f`. Sources and crate archives were
verified against their lockfile checksums before comparison.

## Workspace changes

| Surface | Scout disposition and evidence |
|---|---|
| Git global/system configuration | `GlobalGitIgnore` uses upstream's byte-regex extraction, release lookup order, fallthrough, tilde expansion, and UTF-8 validation. Foundation fixtures cover configuration combinations and raw paths; the new CLI integration case covers GIT_CONFIG_GLOBAL. |
| Ignore context and entry inventory | Each root owns its IgnoreStack path context. Existing entry-based discovery preserves enabled filters and probe fallback. New public library tests and both new CLI root-order regressions cover both orders and serial/parallel traversal; existing tests cover custom ignores, Git/JJ and linked worktrees. |
| Walk APIs and errors | Empty/collection factories materialize roots once. Buffered parallel reads discover ignores before visiting, retain successful entries with errors, and avoid reads at depth limits. WalkException and error callbacks preserve path/depth. Native fault injection and real filesystem regressions cover partial reads, metadata failures, visitor ordering and worker completion. |
| Glob and types | MatchesAll was already correct; strategy matrix and owned candidate regressions verify equivalence. All new types/alias participate in normal public selection, negation, clearing and CLI type output. |
| Mapping | Shared madvise helper advises each acquired mapping base and full view length. Advice failure logs at debug level and remains nonfatal; native success/failure and mapped-window tests cover the path. |
| Completions and documentation | All seven artifacts are regenerated from the release and transformed by the existing Scout identity rules. Zsh option inventory uses upstream's completion test; shell syntax and representative completion behavior are exercised. |
| Option termination | The standard `--` terminator preserves subsequent arguments as raw positional values. A parser regression and the upstream Zsh completion inventory exercise the contract. |
| CLI source cleanup | Core changes to cloning/statistics, lint attributes, import syntax and spelling do not change CLI dispatch or output. Generated documentation incorporates applicable text fixes. |
| Distribution | linux-musl-arm64 is represented in native/PCRE2 builds, runtime selection, default/PCRE2 reference capture, tool packages, archives and release workflows. ABI offsets are checked against Alpine headers; managed, native, package and installation checks execute inside the ARM64 Alpine SDK container on the existing Ubuntu ARM64 runner. |
| Upstream CI and release scripts | Rust build infrastructure is represented by Scout's existing .NET/native workflow conventions and the added musl target. No Rust-specific allocator or build-script API is added to the public libraries. |

## Changed dependency inventory

Every changed lockfile entry is accounted for below. Unchanged Unicode 16.0.0
and encoding data are verified separately by the existing regeneration and
corpus checks. The regex corpus contains the same 839 cases; dependency unit
regressions are added alongside it.

| Dependency | Previous | Release | Disposition |
|---|---|---|---|
| `aho-corasick` | `1.1.3` | `1.1.4` | Rust borrow/lint maintenance; Scout owns pattern bytes and uses its existing general automaton. Existing AhoCorasick conformance covers the shipped surface. |
| `anyhow` | `1.0.100` | `1.0.103` | Rust error wrapper support; Scout retains typed errors and diagnostic context. |
| `bstr` | `1.12.0` | `1.13.0` | The new Chars::count ASCII fast path has no corresponding shipped Scout iterator API. Existing byte counters and malformed UTF-8 decoding remain covered; no new unrelated API is introduced. |
| `cc` | `1.2.41` | `1.2.67` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `crossbeam-channel` | `0.5.15` | `0.5.16` | Rust concurrency support; Scout uses its existing .NET worker primitives. |
| `crossbeam-deque` | `0.8.6` | `0.8.7` | Rust worker support is replaced by per-walk ConcurrentStack workers; traversal, quit/skip, error propagation, and completion are tested. |
| `crossbeam-epoch` | `0.9.18` | `0.9.20` | Rust reclamation support; managed GC owns Scout worker data. |
| `crossbeam-utils` | `0.8.21` | `0.8.22` | Rust concurrency support; Scout uses .NET atomics and worker primitives. |
| `find-msvc-tools` | `0.1.4` | `0.1.9` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `getrandom` | `0.3.4` | `0.4.3` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `globset` | `0.4.18` | `0.4.19` | MatchesAll and owned path semantics were already equivalent; the complete strategy regression matrix is covered by exported library tests. |
| `grep-matcher` | `0.1.8` | `0.1.9` | Rust test/lint maintenance; IMatcher and existing matcher conformance retain the same runtime contract. |
| `grep-pcre2` | `0.1.9` | `0.1.10` | Rust error conversion cleanup; the native PCRE2 bindings and real PCRE2 differential suite cover the same behavior. |
| `grep-searcher` | `0.1.16` | `0.1.17` | Sequential Unix madvise is applied to both live mapping paths; native advice failure remains nonfatal. Existing buffered fallback and windowed mapping coverage remains authoritative. |
| `ignore` | `0.4.25` | `0.4.29` | Global Git configuration, raw paths, per-root ignore context, buffered traversal, empty/collection walkers, and contextual errors. |
| `itoa` | `1.0.15` | `1.0.18` | Rust integer formatting support is replaced by Scout's byte writer; integer boundaries and JSON output have existing coverage. |
| `jobserver` | `0.1.34` | `0.1.35` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `lexopt` | `0.3.1` | `0.3.2` | Adds an optional set_short_equals switch whose default remains enabled; ripgrep does not change that setting. Existing short-option and attached-value parser conformance remains applicable. |
| `libc` | `0.2.177` | `0.2.186` | Rust native declarations; Scout uses LibraryImport and verifies native filesystem ABI and operations on musl ARM64. |
| `log` | `0.4.28` | `0.4.33` | Rust logging support; DiagnosticLogger supplies CLI debug/trace semantics and tests. |
| `memchr` | `2.7.6` | `2.8.3` | Owned finders already copy their needles. Span bounds avoid the packed-pair pointer underflow; oversized needles, unaligned SIMD tails, reverse searches, and ownership are tested. Rust pointer safety annotations and architecture-specific mask construction do not change Scout output. |
| `memmap2` | `0.9.9` | `0.9.11` | Rust mapping wrapper maintenance is replaced by MemoryMappedFile and SafeMemoryMappedViewHandle; sequential advice is provided by the shared native helper. |
| `pkg-config` | `0.3.32` | `0.3.33` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `proc-macro2` | `1.0.101` | `1.0.106` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `quote` | `1.0.41` | `1.0.46` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `r-efi` | `5.3.0` | `6.0.0` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `regex` | `1.12.2` | `1.13.0` | The new lazy regex! macro is a Rust facade convenience. Scout keeps ByteRegex.Compile and validates the unchanged 839-case corpus on both frameworks. |
| `regex-automata` | `0.4.13` | `0.4.15` | Capture-slot clearing and declared group indexing were already equivalent. Zero-repetition, oversized slot buffers, and repeated cache use have explicit regressions. |
| `regex-syntax` | `0.8.8` | `0.8.11` | Property comparison negation is implemented in shared syntax and character-class token parsing. Scout batches range collection and canonicalizes once, avoiding the old upstream repeated sort; canonical merging, algebra, UTF-8 boundaries, and folding have direct regression coverage. |
| `ripgrep` | `15.1.0` | `15.2.0` | CLI identity, generated artifacts, three new integration regressions, and musl ARM64 distribution. |
| `serde_json` | `1.0.145` | `1.0.150` | Rust float formatting now uses zmij instead of ryu. Scout writes JSON directly; JSON differentials validate byte-visible fields, with existing elapsed-time normalization retained. |
| `shlex` | `1.3.0` | `2.0.1` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `syn` | `2.0.107` | `2.0.119` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `tikv-jemalloc-sys` | `0.6.1+5.3.0-1-ge13ca993e8ccb9ba9847cc330696e02839f328f7` | `0.7.1+5.3.1-0-g81034ce1f1373e37dc865038e1bc8eeecf559ce8` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `tikv-jemallocator` | `0.6.1` | `0.7.0` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `unicode-ident` | `1.0.20` | `1.0.24` | Rust build, procedural-macro, allocator, or development support; no shipped Scout behavioral API. Native builds use the checked-in .NET/C toolchain and scripts. |
| `zmij` | `added` | `1.0.23` | New Rust serde_json float-formatting backend. Scout retains its existing direct JSON writer and elapsed-seconds formatting. |

Removed Rust support entries: `ryu`, `wasip2`, `wit-bindgen`. Scout's direct writer and build scripts remain responsible for their existing surfaces.

## Validation

The resolving PR links validation for the final candidate commit. It must
include all supported frameworks and seven targets, actual package consumers
executed with JIT/trim/AOT, generated artifact and shell checks, native default
and PCRE2 comparisons, and the existing full performance gate. Focused interval
compilation and traversal benchmarks supplement that gate without changing its
thresholds or resource limits.

Matching changes use shared syntax and general upstream algorithms. There are
no pattern, corpus, or benchmark recognizers. SDK policy and issue #65 remain
outside this release synchronization.
