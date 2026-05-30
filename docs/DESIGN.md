# Scout — Design Document

**A full, feature-complete port of ripgrep to C# / .NET Native AOT.**

- **Status:** Draft for review (v0.2 — incorporates Codex review round 1)
- **Date:** 2026-05-28
- **Target runtime:** .NET 10 (LTS), Native AOT, `net10.0`
- **Binary name:** `scout`
- **Root namespace:** `Scout`
- **This document:** `docs/DESIGN.md` in the Scout repository (`/Users/brandon/src/scout`).
- **Reference checkout:** `/Users/brandon/src/ripgrep` — the pinned ripgrep working directory used as the upstream reference for the port (§0.1). All `crates/...`, `tests/...`, and `Cargo.lock` paths in this document are relative to it unless stated otherwise.

---

## 0. Upstream Pin (authoritative, non-negotiable)

The port targets **one exact upstream revision**, not a moving tag. All behavior, tests, and data versions below are defined relative to this pin.

| Pinned artifact | Value |
|-----------------|-------|
| ripgrep git commit | `4857d6fa67db69a95cd4b6f2adda5d807d4d0119` (HEAD of the reference checkout at `/Users/brandon/src/ripgrep`, §0.1; the `15.1.0` tag is `af60c2de…`, and HEAD is ahead of it) |
| Reported marketing version | 15.1.0 + post-tag commits (HEAD is **ahead** of the `15.1.0` tag) |
| Rust edition / MSRV at pin | edition 2024 / 1.85 |
| `Cargo.lock` | vendored verbatim into the Scout repo as `upstream/Cargo.lock`; every transitive crate version below is read from it, not guessed |
| Unicode data | the exact UCD version that the pinned `regex-syntax` ships (see §11.1); vendored, never "current at build time" |
| PCRE2 | Rust binding `pcre2` 0.2.11 / `pcre2-sys` 0.2.10 (from the lockfile); bundled **PCRE2 C release 10.46** (dated 2025-08-27, as vendored in `pcre2-sys` 0.2.10's `upstream/`), pinned by tag + git SHA in `native/pcre2/UPSTREAM` (§4.3) |

> **Why this matters (Codex blocker):** the previous draft cited both "15.1.0" and three commits made *after* that tag (`4519153`, `9b84e15`, `cb66736`). That is internally inconsistent. The pin above resolves it: we port HEAD, we vendor its lockfile, and the "version" string Scout prints replicates whatever `rg --version` prints at that commit (verified by a conformance test, not asserted here).

A documented, scripted **upstream-sync policy** (`docs/UPSTREAM-SYNC.md`) governes advancing the pin: bump commit → regenerate vendored lockfile + UCD → re-run the full differential suite → record the diff. The pin never advances silently.

### 0.1 Reference checkout (the ripgrep source the port is done against)

The port is performed against a local ripgrep checkout pinned to the §0 commit, referenced **by path** — not copied or symlinked into the Scout repo:

| Reference | Value |
|-----------|-------|
| Reference path | `/Users/brandon/src/ripgrep` (the current ripgrep working directory) |
| Required commit | must equal the §0 pin `4857d6fa67db69a95cd4b6f2adda5d807d4d0119` |

Everything the port consumes from upstream is read from this checkout: the source to port (`/Users/brandon/src/ripgrep/crates/...`), the lockfile to vendor (`/Users/brandon/src/ripgrep/Cargo.lock` → `upstream/Cargo.lock`), the integration tests and corpus to port (`/Users/brandon/src/ripgrep/tests/...`), and the PCRE2 / UCD provenance. A preflight check asserts the checkout's `HEAD` equals the §0 pin and fails on mismatch, so the port can never drift from the reference.

---

## 1. Naming

**Chosen name: `Scout`** (stakeholder decision).

A *scout* searches and reconnoiters terrain, finds the path, and reports back — a close description of the tool, with room to grow into search-and-transform (`scout replace`/`scout rewrite`).

**Collision due diligence (honest, per Codex):** the name is **not** collision-free. Known prior art includes **Docker Scout**, **Scout APM**, the **`scout` crate on crates.io**, assorted `scout` CLIs/npm packages, and historical security tools. None is a recursive grep CLI, so user confusion in this niche is low, but the name is *not* unique and we will not claim otherwise. Mitigations: distinct package IDs (`Scout.*` for libraries; the executable ships as `scout` with the project clearly branded "Scout (ripgrep port)"); a trademark/search check is an explicit pre-1.0 gate; if a hard conflict surfaces, the rename is mechanical (§1.1).

**Binary alias dropped:** the previously proposed `sc` alias is **removed** — Windows ships `sc.exe` (Service Control Manager). Shipping `sc` would shadow or be shadowed by a system tool. The only installed command is `scout`. (Users may alias it themselves.)

### 1.1 Rename cost
Name appears only in namespace identifiers, the assembly/binary name, package IDs, and help text — a mechanical, fully-scriptable rename affecting no architecture in this document.

---

## 2. Mission & Non-Negotiables

### 2.1 Mission
A C# / .NET Native AOT implementation of ripgrep that is **feature-complete**, **test-complete**, **performance-parity** (§9), and an **exemplary modern-.NET** reference codebase.

### 2.2 Hard requirements (enforced in review)

- **No scope reduction.** No feature is out-of-scope, deferred, stubbed, or `NotImplementedException`-ed. Ever.
- **One type per file — no exceptions.** Every `class`, `struct`, `record`, `record struct`, `interface`, `enum`, and `delegate` lives in its own file named after it. **This applies to nested types, private helpers, and generated code alike.** Helper types that would otherwise be nested are promoted to their own files (made `file`-scoped or `internal` as visibility requires). Source generators emit **one type per generated file**. Enforced by a custom Roslyn analyzer that fails on >1 top-level *or nested* type declaration per file, run over hand-written **and** generated output.
- **Triple-slash XML docs on every public API.** `GenerateDocumentationFile=true`; `CS1591` is an error.
- **Native AOT clean.** Zero `IL2xxx`/`IL3xxx` warnings, resolved by making code genuinely trim/AOT-safe (§5). No runtime reflection on hot paths; no `dynamic`; no runtime codegen.
- **No warning is ever suppressed (§7.1).** Fixed at source, never silenced.
- **Modern .NET idioms.** Nullable on; `ReadOnlySpan<byte>`-first; `static abstract` interface members where they fit; source generators over reflection; pooled scratch buffers; no LINQ on hot paths.
- **Behavioral parity is the contract.** Any intentional deviation from upstream is documented in `PARITY.md` with justification and a guarding test. Silent deviation is a defect.

### 2.3 Non-goals
- No stable public NuGet API surface promised in v1 (libraries are `internal`-leaning but cleanly layered so a curated API can follow).
- No bug-for-bug replication of upstream *bugs* unless a ported test depends on the behavior; such cases are documented.

---

## 3. Source Material & Full Dependency Port Scope

ripgrep is nine workspace crates plus the `rg` binary — **but the port scope is larger than that**, because ripgrep's behavior and performance live substantially in its third-party dependencies. Each of those must be ported or faithfully reimplemented with its **own** exact version, license, behavior, and tests. Understating this was a Codex blocker; the full inventory is below.

### 3.1 Workspace crates → Scout projects

| Rust crate | C# project |
|------------|------------|
| `grep-matcher` | `Scout.Matching` |
| `grep-regex` | `Scout.Regex` |
| `grep-pcre2` | `Scout.Pcre2` |
| `grep-searcher` | `Scout.Searching` |
| `grep-printer` | `Scout.Printing` |
| `globset` | `Scout.Globbing` |
| `ignore` | `Scout.Ignore` |
| `grep-cli` | `Scout.Cli` |
| `grep` (facade) | *(folded into references)* |
| `crates/core` (`rg`) | `Scout.App` (produces `scout`) |

### 3.2 Third-party crates that must also be ported / reimplemented

Versions are read from the vendored `upstream/Cargo.lock` (§0); the table names the obligation, not a guess at the number.

| Upstream dependency | Why it must be ported (not skipped) | Scout home | License to honor |
|---------------------|-------------------------------------|------------|------------------|
| `regex-syntax` | Regex parser/AST/HIR + Unicode tables; defines accepted syntax. | `Scout.Automata.Syntax` | MIT OR Apache-2.0 |
| `regex-automata` | The actual engine (NFA, PikeVM, DFAs, meta). | `Scout.Automata` | MIT OR Apache-2.0 |
| `aho-corasick` | Multi-pattern prefilter used by globset/regex. | `Scout.Automata.AhoCorasick` | MIT OR Unlicense |
| `memchr` (+ `memmem`) | SIMD byte/substring search; core to line scanning & prefilters. | `Scout.Automata.Memmem` | MIT OR Unlicense |
| `bstr` | Byte-string operations (lossy decode, casing, iteration). | `Scout.Bytes` | MIT OR Apache-2.0 |
| `encoding_rs` | WHATWG-exact decoding/transcoding for `-E`. | `Scout.Encoding` (§4.4.1) | (BSD-3-Clause-ish / Apache-2.0/MIT per crate) |
| `encoding_rs_io` | Streaming transcode adapter. | `Scout.Encoding.Io` | as above |
| `same-file` | Device/inode (Unix) & file-id (Windows) identity for symlink-loop detection. | inside `Scout.Ignore` | MIT OR Unlicense |
| `walkdir` | Directory traversal semantics ripgrep relies on. | inside `Scout.Ignore` | MIT OR Unlicense |
| `termcolor` | Cross-platform terminal color writer. | inside `Scout.Cli` | MIT OR Unlicense |
| `crossbeam-deque` | Work-stealing for the parallel walker. | inside `Scout.Ignore` | MIT OR Apache-2.0 |
| `lexopt` | Argument lexing semantics. | inside `Scout.App` | MIT OR Apache-2.0 |
| `bytecount` / SIMD counting (if pulled transitively) | Fast line counting. | `Scout.Automata` | per lockfile |
| `serde_json` (printer JSON) | **Not** ported as a serializer — see §4.5.1 (we write bytes explicitly). | n/a | n/a |
| `textwrap` | `--help` long-text wrapping width behavior. | inside `Scout.App` | MIT |
| `log` / `env_logger` | `--debug`/`--trace` logging format. | `Scout.Diagnostics` | per lockfile |
| `pcre2` 0.2.11 (+ `pcre2-sys` 0.2.10) | FFI bindings to PCRE2 C lib. | `Scout.Pcre2` (P/Invoke, §4.3) | MIT OR Apache-2.0; PCRE2 is BSD |

#### 3.2.1 Remaining lockfile dependencies — explicit dispositions (added per Codex)
Every other crate in `upstream/Cargo.lock` that touches behavior, build, or reproducibility is dispositioned here. Versions are the exact lockfile values.

| Upstream dependency | Disposition | Scout home / rationale |
|---------------------|-------------|------------------------|
| `regex-syntax` 0.8.8 | Port | `Scout.Automata.Syntax`. |
| `regex-automata` 0.4.13 | Port | `Scout.Automata`. |
| `aho-corasick` 1.1.3 | Port | `Scout.Automata.AhoCorasick`. |
| `memchr` 2.7.6 | Port | `Scout.Automata.Memmem`. |
| `bstr` 1.12.0 | Port | `Scout.Bytes`. |
| `encoding_rs` 0.8.35 | Port | `Scout.Encoding` (§4.4.1). |
| `encoding_rs_io` 0.1.7 | Port | `Scout.Encoding.Io`. |
| `memmap2` 0.9.9 | Replace | Behavior provided by `MemoryMappedFile` + `SafeMemoryMappedViewHandle` in `Scout.Searching` (§4.4); same mmap-vs-read heuristics. No separate project. |
| `regex` 1.12.2 | Replace | The high-level `regex` crate facade; its surface (used by `globset`/`ignore` for set matching) is provided directly by `Scout.Automata`'s meta engine + `PatternSet`. No separate facade. |
| `glob` 0.3.3 | Replace/none | The standalone `glob` crate (distinct from `globset`) is only a build/dev-side path-expansion helper, not a search-behavior surface; equivalent path enumeration lives in `Scout.Os`. Documented as no runtime behavioral surface. |
| `walkdir` 2.5.0 | Port | Inside `Scout.Ignore`. |
| `same-file` 1.0.6 | Port | Inside `Scout.Ignore` (device+inode / file-id identity). |
| `crossbeam-deque` 0.8.6 | Port | The work-stealing semantics, inside `Scout.Ignore` (§4.8.1). |
| `crossbeam-channel` 0.5.15, `crossbeam-epoch` 0.9.18, `crossbeam-utils` 0.8.21 | Replace (transitive) | Pulled in *under* `crossbeam-deque`/`ignore`; their roles (MPMC channels, epoch GC, atomics/backoff) are subsumed by the managed work-stealing pool built on `System.Threading.Channels` + `System.Threading` primitives. No separate ports; the **observable** behavior (ordering, cap, forced-serial — §4.8.1) is what tests pin, and it is replicated exactly. |
| `termcolor` 1.4.1 | Port | Inside `Scout.Cli` terminal layer. |
| `winapi-util` 0.1.11 | Replace | Its Windows helpers (console mode, file info, stdin type) are provided by direct `LibraryImport` P/Invoke in `Scout.Os`/`Scout.Cli`. |
| `windows-sys` 0.61.2 | Replace | Raw Win32 bindings replaced by Scout's own `LibraryImport` declarations of exactly the APIs used; no third-party binding layer. Behavioral surface = the specific Win32 calls, all enumerated in `Scout.Os`. |
| `anyhow` 1.0.100 | Port (behavior) | **Runtime dependency** (`Cargo.toml:53`), used throughout `crates/core` to build user-facing errors. Since **stderr is part of byte-identical conformance**, this is ported as `Scout.Errors` (§4.10.1): an error type carrying a **cause chain**, whose `Display` and alternate (`{:#}`) rendering — the chain joined by `": "` — match anyhow exactly. The actual context/`bail!` message strings at every upstream call site are ported **verbatim** and pinned by the differential suite. |
| `lexopt` 0.3.1 | Port | Argument-lexing semantics, inside `Scout.App`. |
| `textwrap` 0.16.2 | Port | `--help` long-text wrapping width, inside `Scout.App`. |
| `log` 0.4.28 | Port (behavior) | `--debug`/`--trace` message format replicated by `Scout.Diagnostics`; not a generic logging framework. |
| `serde` 1.0.228 + `serde_derive` | Replace/none | Used upstream for JSON (printer) and `arbitrary`/dev. Scout's JSON is a hand-written byte writer (§4.5.1); serde has **no runtime surface** in Scout. `serde`/`serde_derive` appear only as a *test-time* concern if a fixture needs them, never in shipped code. |
| `serde_json` 1.0.145 | Replace/none | Not used as a serializer (§4.5.1); its **exact output formatting** (escaping, number formatting) is replicated by Scout's byte writer and pinned by `tests/json.rs`. |
| `itoa` 1.0.15, `ryu` 1.0.20 | Replace | Fast integer/float→ASCII used by `serde_json`. Scout's byte writer implements byte-identical integer formatting; the JSON schema's numbers (offsets, counts, line/byte numbers) are integers — any float (e.g. `--stats` elapsed seconds) is formatted to match upstream output exactly, verified by the differential suite. |
| `tikv-jemallocator` 0.6.1 / `tikv-jemalloc-sys` 0.6.1 | Replace/none | A **musl-x64-only** allocator swap upstream uses for throughput on that target. Not portable to .NET (the CLR/Native AOT manages its own allocator). No port; allocation throughput is instead addressed by Scout's zero-alloc hot-path design (§5) and held to the perf gates (§9). Documented as an intentional, behavior-neutral omission (allocator choice does not change output). |
| `winapi`/`libc` (platform) | Replace | Direct `LibraryImport` in `Scout.Os`. |

Anything in the lockfile not listed above is a pure build/test dependency of the Rust toolchain with no analog in the .NET build and no behavioral surface (e.g. `cc`, `jobserver`, `autocfg`); these are explicitly out of scope and noted in `docs/UPSTREAM-SYNC.md`.

**License obligations:** ripgrep is `MIT OR Unlicense`; dependencies are mostly `MIT OR Apache-2.0` or `MIT OR Unlicense`. A `THIRD-PARTY-NOTICES.md` reproduces every upstream license whose code/algorithm we port. PCRE2's BSD license is reproduced for the bundled native lib.

**Sync policy:** each ported dependency records its source commit/version in a per-project `UPSTREAM.md`; advancing any pin re-runs that project's ported test suite plus the global differential suite.

### 3.3 Scale (measured from the pin)
~104 CLI flags (one `Flag` impl each, `crates/core/flags/defs.rs`); ~52 inline unit-test modules; 10 root integration-test files (incl. `regression.rs`, `misc.rs`, `feature.rs`, `json.rs`, `multiline.rs`, `binary.rs`); `ignore`/`matcher` integration tests; bash/zsh/fish/PowerShell completion generators; man-page generator. None optional.

---

## 4. The Defining Technical Decisions

### 4.1 Bytes, not strings — including at the boundaries (revised per Codex)

ripgrep operates on `&[u8]` end to end and must preserve **arbitrary bytes** at every boundary: Unix `argv`/`env`/paths are byte sequences that need not be valid UTF-8, and stdout/stderr must be **byte-exact**. The previous draft's "`string` is fine at the CLI and output edges" was wrong and is replaced:

- **Argument intake.** We do **not** rely on `static int Main(string[] args)` for argument bytes on Unix, because the runtime decodes `argv` to UTF-16 lossily. Instead:
  - **Unix (decided — single mechanism, all Unix):** a tiny **C entry shim** (`native/entry/scout_main.c`) is compiled and **statically linked into the AOT image**. It defines the process `main(int argc, char** argv, char** envp)`, captures the raw `argv`/`envp` **byte** pointers verbatim, and calls an `[UnmanagedCallersOnly]`-exported managed entry `scout_entry(int argc, byte** argv, byte** envp)`. The managed side copies each `char*` as a `ReadOnlySpan<byte>` (NUL-terminated) with **no decoding**, and feeds byte arguments to the parser. This works uniformly on Linux, macOS, and the BSDs and depends on **no** `/proc` filesystem. (The earlier `/proc/self/cmdline` idea is **dropped** — it is Linux-only and unreliable.) The C shim is part of the per-RID reproducible native build (alongside PCRE2, §4.3). `env` is read from the captured `envp` (and `getenv` for targeted lookups) at the byte level.
  - **Windows:** arguments are natively UTF-16; obtained via `GetCommandLineW` + `CommandLineToArgvW` and kept as the platform's wide form, matching ripgrep's Windows behavior.
  - The `OsString`/`OsStr` model (§4.9) is the currency; `string` is used only when an argument is *semantically* text (e.g. a numeric flag value).
- **Output.** All match output is written as **raw bytes** to the underlying stdout/stderr handle (a `Stream` over `fd 1`/`fd 2` or the Windows handle), **never** through `Console.Write(string)` (which transcodes and can mangle bytes / rewrite newlines). The terminal-color layer writes byte sequences (ANSI) directly; Windows VT mode is enabled via `SetConsoleMode` but bytes still go through the raw handle.
- **`string` appears only** for values that are inherently text and where upstream also treats them as text (e.g. numbers, enum-valued flags, help text).

`Scout.Bytes` (port of `bstr`) provides span-based byte-string operations; nothing on the search/print path round-trips through `string`.

#### 4.1.1 Native entry build shape (specified, per Codex — and gated by a proof-of-build)
The C-shim decision implies a concrete Native AOT link shape, defined here and **proven before the design is considered closed** (M0 gate, §10):
- **`Scout.App` is published as a Native AOT *static library*** (`<NativeLib>Static</NativeLib>`, `PublishAot=true`), producing `libscout.a` (`scout.lib` on Windows) plus the AOT runtime archives. Building as a library means **there is no managed `Main` used as the process entrypoint** — the default managed entrypoint is intentionally absent, eliminating the lossy `string[] args` path entirely.
- **The managed entry is an export:** `[UnmanagedCallersOnly(EntryPoint = "scout_entry")] static int ScoutEntry(int argc, byte** argv, byte** envp)`. This is the one symbol the driver calls.
- **The C driver** (`native/entry/scout_main.c`) defines the real `int main(int argc, char** argv, char** envp)`, links against `libscout.a` + the AOT runtime archives, and simply **calls `scout_entry(argc, argv, envp)`** and returns its exit code.
- **Runtime initialization (reconciled with the verified spike):** the Native AOT static library **self-initializes the runtime on the first managed entry** — the `[UnmanagedCallersOnly]` export's prologue (provided by the linked `libbootstrapperdll.o`) brings up the GC/runtime before user managed code runs, so the C driver does **not** need to call a separate init function. This is what the Appendix A spike actually does and it works (verified on osx-arm64). The earlier wording in this section claiming the driver performs an *explicit* init call was incorrect and is corrected here: for the static-lib + `UnmanagedCallersOnly` shape, init is automatic-on-first-call, not a manual step. (If a future RID is found to require an explicit initializer, the driver will call the SDK-emitted bootstrap symbol there; none was needed on osx-arm64.) On Windows the driver uses the wide entry (`wmain`/`GetCommandLineW`, §4.1) and the same single `scout_entry` dispatch.
- **Linking** is part of the per-RID reproducible native build (§4.3): pinned C compiler/linker flags, deterministic, one script per RID, producing the final `scout` executable.

**Proof-of-build spike (M0 deliverable, blocking).** A minimal program built in exactly this shape — AOT static lib exporting `scout_entry`, C driver providing `main` (the runtime self-initializes on the first managed call — §4.1.1; no explicit init step), raw `argv`/`envp` byte capture — that echoes argv bytes verbatim, **built and executed on all six RIDs** including a deliberately **non-UTF-8 argument round-trip**. The complete, turnkey source for this spike (C driver, managed entry, project file, build/run script, expected output) is in **Appendix A** so it can be executed verbatim on a machine with the pinned SDK.

> **Execution status — actually executed and PASSED on TWO RIDs (.NET SDK 10.0.102, clang/Rosetta).** Full transcripts and exact link lines in Appendix A.
> - **osx-arm64 (native):** `dotnet publish -p:NativeLib=Static` → exit 0, `Scout.Entry.a` (~2.5 MB); C `main` driver linked against the static lib + NativeAOT runtime archives → exit 0, 2.2 MB executable; passing a single `0xFF` argument echoed back `ff 0a` — the non-UTF-8 byte preserved verbatim (a managed `string[] Main` would have yielded U+FFFD). **PASS.**
> - **osx-x64 (cross-compiled from arm64, run under Rosetta):** publish → exit 0; link → exit 0 (2.35 MB Mach-O x86_64) **after adding the x64-only `libRuntime.VxsortEnabled.a`** to the link — the only per-arch link difference found; run → `ff 0a`. **PASS.**
>
> The remaining **four** RIDs (`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64` — Windows via the `wmain`/`GetCommandLineW` variant) require their own OS/CI environments and are the M0 CI gate. *(Correction of record: earlier revisions of this note first wrongly said no SDK was installed, then prematurely claimed a pass; both were wrong. This reflects actually-executed runs with captured transcripts.)*

### 4.2 The regex engine — port `regex-automata`, do not wrap `System.Text.RegularExpressions`

Rationale unchanged and reaffirmed: the BCL engine is UTF-16/`char`/`string`-based, has different syntax/semantics from the Rust `regex` crate (which our tests are pinned to), and source-gen regex needs compile-time patterns. We port the byte-oriented finite-automata architecture, which is also the ideal Native AOT fit (data-driven, no codegen, fast cold start).

**`Scout.Automata.Syntax`** (port of `regex-syntax`): parser → AST → HIR; flags `i m s U x`; Unicode classes/properties; anchors; word boundaries (incl. `\b{start}`/`\b{end}`); captures. Unicode tables vendored & pinned (§11.1).

**`Scout.Automata`** (port of `regex-automata`): **all engines up front** — Thompson NFA compiler (UTF-8 automata over bytes), PikeVM, hybrid (lazy) DFA, dense & sparse DFA, one-pass DFA, bounded backtracker, meta strategy selection, and prefilters (`Memmem`, `AhoCorasick`, `Teddy`). `PatternSet`/multi-regex for globset & ignore.

SIMD via `System.Runtime.Intrinsics`: shipped baseline is SSE2 + AVX2 (x64) and `AdvSimd`/NEON (arm64); AVX-512 paths are additive and `Avx512*.IsSupported`-gated, delivered before Release. Scalar fallback always present.

### 4.2.1 Matcher callback ABI (made concrete, per Codex)
The internal-iteration model is realized **without** `Func<...>` (which boxes/allocates and can't carry `ref struct`/span state cleanly). Instead:
- The matcher hot path is **generic over a `where TSink : struct, ISink` value type** so the AOT compiler monomorphizes and inlines, eliminating virtual dispatch and allocation. The `ISink` interface uses `static`/instance methods taking `ReadOnlySpan<byte>` and `Match` by value.
- Where a non-generic boundary is unavoidable, we use **`delegate*` (function pointers) plus an explicit `void*`/`ref TState` state argument** with a documented lifetime (state is stack-rooted for the duration of the call; no captured heap closures). No span is stored beyond the callback's synchronous scope.
This gives zero-allocation, span-safe callbacks with a defined state/lifetime model.

### 4.3 PCRE2 under Native AOT (fully specified, per Codex)
- **Version (exact):** Rust binding `pcre2` **0.2.11**, FFI crate `pcre2-sys` **0.2.10**; the bundled C library is **PCRE2 10.46** (`PCRE2_MAJOR 10` / `PCRE2_MINOR 46`, `PCRE2_DATE 2025-08-27` — read directly from `pcre2.h` in the vendored `pcre2-sys` 0.2.10 `upstream/` tree). Code unit width **8-bit** (`libpcre2-8`), Unicode enabled. The exact PCRE2 upstream git tag (`pcre2-10.46`) and commit SHA are recorded in `native/pcre2/UPSTREAM` at vendoring and checksum-verified in CI.
- **Linking:** built from vendored source per RID and **statically linked** into the AOT image; interop via `LibraryImport` (source-generated marshalling, AOT-safe). A reproducible native build (pinned compiler flags, deterministic, checked-in build script per `linux-x64`/`linux-arm64`/`osx-x64`/`osx-arm64`/`win-x64`/`win-arm64`) lives in `native/pcre2/`. Build provenance recorded; no prebuilt binaries pulled at build time.
- **JIT:** PCRE2 JIT compiled in; at runtime `pcre2_config(PCRE2_CONFIG_JIT)` gates JIT use with graceful fallback to the interpreter when unavailable on a platform. `--pcre2-version` prints the linked library version + JIT availability, matching upstream.
- **Engine selection:** `-P/--pcre2` forces PCRE2; `--engine auto` replicates upstream's fallback (try default engine, fall back to PCRE2 when a feature like backreferences/look-around is used). `--no-pcre2-unicode` honored.
- **Licensing:** PCRE2 BSD license reproduced in `THIRD-PARTY-NOTICES.md`.

### 4.4 Searcher (`Scout.Searching`)
Search loop, line buffering, mmap vs buffered, before/after/passthru context, inverted match, counting, multiline. **Binary detection** (`quit`/`convert`/`none`) byte-for-byte. mmap via `MemoryMappedFile` + `SafeMemoryMappedViewHandle` → `unsafe ReadOnlySpan<byte>`; same mmap-vs-read heuristics as upstream.

#### 4.4.1 Encoding — committed `encoding_rs`-exact layer up front (revised per Codex)
Hedging ("`System.Text.Encoding` first, port tables where they diverge") is removed. For full parity we **port `encoding_rs` directly** as `Scout.Encoding` from the start: WHATWG label resolution, all WHATWG encodings, exact decoder state machines, exact replacement-character behavior, and BOM sniffing identical to upstream. `encoding_rs_io`'s streaming transcode becomes `Scout.Encoding.Io`. Gated by a conformance suite built from `encoding_rs`'s own test vectors. `System.Text.Encoding` is **not** used for `-E` decoding. (`-E`'s special values like `none`/`auto` match upstream semantics.)

### 4.5 Printer (`Scout.Printing`)
`Standard` (columns, color, `--replace`, `--max-columns[-preview]`, `-o`, `--vimgrep`, `-H/-I`, byte offset, heading, separators, multiline, `--hyperlink-format` + `HyperlinkEnvironment`), `Summary` (`-c`, `--count-matches`, `-l`, `--files-without-match`), and JSON.

#### 4.5.1 JSON — explicit byte writer, not `JsonSerializer` (revised per Codex)
Because output must be **byte-identical** to ripgrep's `serde_json`-produced JSON Lines, and `System.Text.Json` does **not** guarantee serde-compatible escaping/ordering across runtime versions, the JSON printer is a **hand-written, allocation-free byte writer** with **pinned escaping rules** matching serde_json exactly (control-char escaping, `\uXXXX` forms, no superfluous escapes, field order fixed by us, `bytes` fields base64 when non-UTF-8 per the documented schema). It emits `begin`/`match`/`context`/`end`/`summary` records. Verified byte-for-byte against `tests/json.rs`. `System.Text.Json` is not on this path.

### 4.6 Globbing (`Scout.Globbing`)
`Glob`/`GlobSet` multi-strategy matcher (literal map, basename, extension, prefix/suffix via `AhoCorasick`, regex-set fallback via `PatternSet`); full glob syntax incl. `**`, `{...}`, `[...]`, `literal_separator`, platform-aware escaping.

### 4.7 CLI utilities (`Scout.Cli`)
Stdin readability heuristics (Unix `fstat`, Windows `GetFileType`); output buffering (line on tty, block when redirected); pattern-file loading with precise UTF-8 error location; `escape`/`unescape`; human-size parsing; **decompression** (`-z`, `--pre`) by spawning external tools (we shell out, matching upstream — see §8.5 for the tool matrix); terminal-color layer (port of `termcolor`).

### 4.8 Ignore / walker (`Scout.Ignore`)

#### 4.8.1 Parallelism semantics (corrected & specified, per Codex)
Exact replication of upstream:
- **Default thread count** = `available_parallelism().map_or(1, get).min(12)` — the cap of **12** is confirmed at `crates/core/flags/hiargs.rs:173`; overridable by `-j/--threads`.
- **Forced single-threaded** exactly per upstream: `threads = 1` when `low.sort.is_some() || paths.is_one_file` (`hiargs.rs:168`) — i.e. whenever output is **sorted** (`--sort`/`--sortr`) or there is a **single file/stdin** subject (`is_one_file`, defined via `paths.len() == 1 && (path == "-" || !is_dir)` at `hiargs.rs:1094`). `main.rs` then dispatches the serial path when `args.threads() == 1` (`main.rs:87,89`). An assertion (`hiargs.rs:912`) enforces "sorting implies single threaded." All of this is ported verbatim and pinned by tests.
- **Work-stealing** parallel walker (port of `crossbeam-deque` semantics) with per-thread `Sink`/`Printer` and an ordered output stage preserving upstream's interleaving/ordering guarantees.

#### 4.8.2 Rules & types
`.gitignore`/`.ignore`/`.rgignore` per-directory rule stacks, global gitignore, `--no-ignore*` family, `Match<T>` (None/Ignore/Whitelist), negation precedence; built-in file-type table (incl. the `container` type at the pin), `--type`/`-T`/`--type-add`/`--type-list`; overrides (`-g`/`--iglob`); `--hidden`; symlink follow (`-L`) with loop detection via device+inode (Unix) / file-id (Windows); `--max-depth`, sorting.

### 4.9 OS / filesystem layer (deepened, per Codex)
Standard `System.IO` string APIs are insufficient for byte-preserving parity. `Scout.Os` provides a thin, byte-honest syscall layer:
- **Unix** (`LibraryImport` to `libc`): `openat`/`fstatat`/`lstat`/`readlink`/`getcwd`/`readdir` (byte `d_name`), `environ`/`getenv`, raw `argv` (§4.1), subprocess spawn preserving byte args, and **`errno`→message formatting matching ripgrep's error strings**. `OsString` is bytes.
- **Windows**: wide-char (`*W`) APIs, file-id via `GetFileInformationByHandle`/`FILE_ID_INFO`, `GetCommandLineW`. `OsString` is UTF-16.
- Path display/escaping (e.g. lossy rendering for non-UTF-8 paths) replicates upstream exactly and is covered by cross-platform path tests (non-UTF-8 names, invalid surrogates, trailing dots/spaces on Windows).

### 4.10 The `scout` binary (`Scout.App`)
Port of `lexopt`-based parser; **~104 flags, one file each** under `Scout.App/Flags/Definitions/` (one type per file, §2.2), each implementing `IFlag` (long/short/negation/aliases/category/doc). A source generator builds the flag lookup table + completion/man/help artifacts (one generated type per file). `LowArgs`→`HiArgs` pipeline; orchestration (stdin vs paths, parallel vs serial per §4.8.1, exit codes 0/1/2, `--quiet`, `--stats`, `--files`, `--type-list`, preprocessing); config file via `RIPGREP_CONFIG_PATH` (DEBUG-on-unset behavior at the pin); `--help`/`-h`; `--version` byte-matching upstream; generators for man + four shells.

#### 4.10.1 Error & message formatting parity (`Scout.Errors` + `Scout.Diagnostics`, added per Codex)
ripgrep's stderr is byte-significant, so error rendering is replicated exactly:
- **Cause chains** — upstream builds errors with `anyhow` and prints them at the top level with the alternate format `eprintln_locked!("{:#}", err)` (`main.rs:62`), which renders the error followed by its cause chain joined by `": "`. `Scout.Errors` provides an exception/error type with an ordered cause chain and a formatter that reproduces both the default (`Display`) and alternate (`{:#}`) forms byte-for-byte.
- **Message strings** — every `anyhow::bail!`/`.context(...)`/`anyhow!` site in `crates/core` is ported with its **exact** string. No paraphrasing.
- **Message macros** — `message!`, `err_message!` (sets the "errored" flag, affecting exit code 2), and `eprintln_locked!` (`crates/core/messages.rs`) are reimplemented in `Scout.Diagnostics` with the same global `errored` flag and the same stderr-lock semantics, writing **raw bytes** to fd 2 (§4.1).
- Exact stderr text, including the trailing newline behavior and the `error:`/path-prefix conventions, is pinned by the differential suite (which already diffs stderr).

**No new env vars in the parity build (revised per Codex).** The previously proposed `SCOUT_CONFIG_PATH` is **removed from v1**. Scout reads `RIPGREP_CONFIG_PATH` exactly as upstream so config behavior is identical and the differential suite is not poisoned. A Scout-native config env var is a **post-parity, opt-in** feature, and if added it will be (a) off by default, (b) precedence-specified, and (c) explicitly excluded from `rg` differential tests.

---

## 5. Native AOT Strategy

| Concern | Approach |
|---------|----------|
| Reflection | None on hot paths; serialization is hand-written byte I/O (§4.5.1); flag/Unicode tables via source generators. No `Activator`/`Type.GetType`. |
| Trimming | `TrimMode=full`; zero IL2xxx as errors, fixed by trim-safe code (not suppression). |
| Dispatch | Generic struct strategies + `static abstract` interface members → monomorphized hot loops; `delegate*`+state where needed (§4.2.1). |
| Allocation | `ArrayPool`/`MemoryPool`, `stackalloc`, `ref struct` iterators, pooled per-thread buffers; zero-alloc hot loops validated by allocation tests. |
| SIMD | `System.Runtime.Intrinsics`, `IsSupported`-gated, scalar fallback always present. |
| Interop | `LibraryImport` for `libc`, Win32, statically-linked PCRE2. |
| Globalization | See §5.1. |

### 5.1 Globalization & language pinning (specified, per Codex)
- **`InvariantGlobalization=true`** is set **explicitly** (not "where it doesn't change behavior"). All casing relevant to matching uses Scout's **own Unicode tables** (§11.1), independent of ICU, for determinism vs the Rust `regex` crate. Culture-sensitive `string` casing/sorting/formatting is banned on behavior paths; a test suite asserts identical results under several `LANG`/`LC_ALL` settings.
- **Reproducible compilation — exact pinned values (per Codex):**
  - **`TargetFramework`** = `net10.0`.
  - **`LangVersion`** = `14.0` (the C# version shipping with .NET 10; never `latest`/`preview`).
  - **SDK** pinned in `global.json` to **`10.0.102`** with `"rollForward": "disable"` so only that exact SDK builds the repo. This is the SDK that actually built the verified spikes (§4.1.1); CI fails if the installed SDK differs. (Bumped only via the documented sync policy.)
  - **Runtime / ILCompiler pack** pinned to **`10.0.2`** (`RuntimeFrameworkVersion` = `10.0.2`; `Microsoft.DotNet.ILCompiler` = `10.0.2`; the NativeAOT runtime pack `microsoft.netcore.app.runtime.nativeaot.<rid>` = `10.0.2`) in `Directory.Packages.props` — the exact pack the spikes linked against (Appendix A/B).
  - **`Deterministic`** = `true`, `ContinuousIntegrationBuild` = `true`, `InvariantTimezone` = `true`, `InvariantGlobalization` = `true`.

  These literal values live in `global.json`/`Directory.Build.props`/`Directory.Packages.props`; future SDKs cannot silently change compilation semantics.

Each library: `net10.0`, `IsAotCompatible=true`. Only `Scout.App`: `PublishAot=true`.

---

## 6. Repository & Solution Layout

```
scout/
  Scout.sln
  global.json                  # SDK pinned, rollForward: disable
  Directory.Build.props        # nullable, warnaserror, pinned LangVersion, doc-gen, no-suppression props
  Directory.Packages.props     # central package management, pinned versions
  .globalconfig / .editorconfig# analyzer severities (only ever stricter), one-type-per-file rule
  upstream/                     # vendored Cargo.lock, UCD data, license texts
  native/pcre2/                 # vendored PCRE2 source + per-RID reproducible build scripts
  src/
    Scout.Bytes/  Scout.Os/  Scout.Diagnostics/
    Scout.Matching/
    Scout.Automata.Syntax/  Scout.Automata/        # + Memmem, AhoCorasick, Teddy
    Scout.Regex/  Scout.Pcre2/
    Scout.Encoding/  Scout.Encoding.Io/
    Scout.Searching/  Scout.Printing/
    Scout.Globbing/  Scout.Ignore/  Scout.Cli/
    Scout.App/                  # produces `scout`
    Scout.SourceGen/            # Unicode/flag/help/man/completion generators + analyzers
  tests/   (mirror of src + Integration + Conformance + Encoding conformance)
  bench/   (Scout.Benchmarks [BenchmarkDotNet] + ported benchsuite [hyperfine])
  docs/    (DESIGN.md, PARITY.md, UPSTREAM-SYNC.md, THIRD-PARTY-NOTICES.md)
```

---

## 7. Coding Standards (enforced)

- **One type per file, no exceptions** (§2.2), including nested and generated types — custom analyzer over hand-written + generated sources.
- **Public XML docs everywhere** (`CS1591` = error).
- **Nullable on; `TreatWarningsAsErrors=true`; pinned `LangVersion`.**
- **Analyzers:** `Microsoft.CodeAnalysis.NetAnalyzers` (all), threading analyzers, AOT/trim analyzers. Severities may be set **stricter** only.
- **Formatting:** `dotnet format` verified in CI.
- **Naming:** `Scout.<Area>`; public types map recognizably to upstream for cross-review.
- **No `async` on the synchronous search hot path** (matches upstream); async only for genuine concurrency.

### 7.1 No suppressions, ever — robust enforcement (strengthened, per Codex)
Every warning is fixed at its source; **none is silenced**. Banned: `#pragma warning disable`, `[SuppressMessage]`, `[UnconditionalSuppressMessage]`, `<NoWarn>`, `$(WarningsNotAsErrors)`, `<DisabledWarnings>`, `#nullable disable`, `GlobalSuppressions.cs`, and any `dotnet_diagnostic.*.severity = none/silent` (in `.editorconfig`, `.globalconfig`, or transitive package configs) used to dodge a real finding.

Enforcement is **not** "grep the diff." It is:
1. A **CI MSBuild evaluation check** that dumps each project's *evaluated* `NoWarn`, `WarningsNotAsErrors`, `TreatWarningsAsErrors`, and effective analyzer severities (`-getProperty`/binlog inspection) and fails if `NoWarn` is non-empty or `TreatWarningsAsErrors` is false anywhere, **including values injected transitively by packages**.
2. A **repository scanner** over all `*.cs` (hand-written and generated, including `obj/`), `*.props`, `*.targets`, `.editorconfig`, `.globalconfig`, and any `GlobalSuppressions.cs`, failing on any banned token.
3. **Build with `/warnaserror` and zero warnings** as the ground truth — a green build with all analyzers at error severity is the real gate; the scanners prevent re-introducing escape hatches.

Behavior-bearing annotations are **not** suppressions and remain allowed where correct: `[RequiresUnreferencedCode]`, `[RequiresDynamicCode]`, `[RequiresAssemblyFiles]`, `[DynamicallyAccessedMembers]`, and nullable annotations. (The earlier draft's `[RequiresUnsafeCode]` does **not** exist in .NET and has been removed — corrected per Codex.)

---

## 8. Testing Strategy — every upstream test ported, plus differential

Six layers, all required. **No-skip policy (hard):** a skipped, ignored, or quarantined test fails CI. There is **zero** waiver path for v1.0 — **the release gate requires zero skipped and zero waived tests.** During *pre-release internal milestones only*, an in-progress test may be temporarily marked with a tracked, time-boxed entry in `PARITY.md`, but every such entry **must be burned down before v1.0** and the count is asserted to be zero at Release. (This matches the stakeholder's "do not skip": nothing ships skipped.)

1. **Unit tests** — port of ~52 inline `#[cfg(test)]` modules, one assertion-for-assertion.
2. **Integration tests** — port of `tests/*.rs` driving the built `scout` binary as a subprocess on the same fixtures; the `rgtest!` macro harness is reimplemented as a fixture + source generator emitting one xUnit test per upstream case.
3. **Differential / conformance suite (mandatory)** — runs **both** `scout` and the pinned real `rg` over a large corpus × thousands of flag combinations, asserting **byte-identical stdout, stderr, and exit code**. Any diff is fixed or recorded in `PARITY.md`.
4. **Regex conformance** — port of the `regex` crate's test suite, gating `Scout.Automata` independently.
5. **Encoding conformance** — `encoding_rs` test vectors gating `Scout.Encoding` (§4.4.1).
6. **Fuzzing** — port of `fuzz/` targets via `SharpFuzz` (regex parse, glob compile, search loop).

**Framework:** **xUnit v3** throughout (decided). Coverage tracked, but **parity, not %, is the bar.** All layers run on Linux, macOS, Windows × x64/arm64.

### 8.5 CI / test prerequisites — exactly pinned (revised per Codex)

**Pinning mechanism (the source of truth) — deterministic by construction:**
- **Linux** CI runs inside a container built `FROM debian:bookworm-slim` with **all `apt` installs routed through the pinned `snapshot.debian.org` date `2026-05-01`** (`deb http://snapshot.debian.org/archive/debian/20260501T000000Z bookworm main`). Pinning the snapshot date makes **every** package version a deterministic function of that date — exact versions are not invented, they are *resolved* from the snapshot and then frozen into `tests/PREREQS.lock` (Appendix B). The built image is referenced by **content digest** thereafter.
- **macOS/Windows** runners install tools from checksum-verified archives at versions recorded in the manifest.
- **`tests/PREREQS.lock`** is the frozen record: the base-image digest, the snapshot date, and **every tool's exact resolved version + SHA-256**. CI verifies each entry before running and **fails on any missing/mismatch** (no silent skips). The literal digests/versions/SHAs are *captured at first image build and corpus fetch* and committed to `PREREQS.lock` — the same "inputs pinned in the plan, resulting hashes captured at inception, verified in CI" pattern used for the SDK patch (§5.1) and the PCRE2 commit SHA (§4.3). The plan pins the **inputs that fully determine** the versions; the lockfile holds the **resulting literals**.

**Decompression tools** for the `-z`/`--pre` matrix — exactly the set upstream invokes, **derived mechanically from the default command table in `crates/cli/src/decompress.rs`** at the pin (not a guessed list): `gzip`, `bzip2`, `xz` (`liblzma`), `zstd`, `lz4`, `brotli`, `compress`/`uncompress` (`ncompress`), `lzip`/`lzma`, plus any others present in that table. Each resolves to a single exact version via the pinned snapshot date and is frozen in `PREREQS.lock`.

**Benchmark & differential tooling:** `hyperfine` installed from a checksum-verified release pinned in `PREREQS.lock`; the **reference `rg`** is **built from the pinned upstream commit** (§0) with `--profile release-lto`, and **its binary SHA-256 is recorded** in `PREREQS.lock` (we never download a prebuilt release — we compile the exact pinned source); BenchmarkDotNet pinned via central package management (§5.1).

**Corpora:** OpenSubtitles `en.txt` (the canonical ripgrep benchmark corpus) and a pinned linux-kernel source snapshot, each **fetched by pinned URL + SHA-256** recorded in `PREREQS.lock` and cached; CI fails on hash mismatch.

**RID matrix (all jobs):** `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`.

---

## 9. Performance — parity is a hard release gate (revised per Codex)

Performance parity is a **blocking** acceptance criterion, not best-effort. The numbers below are the **release gate**, measured (warm cache, release-LTO Rust vs Native AOT, pinned corpora, hyperfine) and enforced in CI; a regression past the gate **blocks release** rather than becoming follow-up work.

| Workload class | Release gate vs Rust `rg` |
|----------------|---------------------------|
| Large single-file literal/regex scan (mmap, SIMD-bound) | ≤ **1.20×** wall time |
| Recursive walk over a large tree (I/O + ignore-bound) | ≤ **1.25×** |
| Many-small-files, parallel | ≤ **1.30×** |
| Cold-start (`scout --version`, tiny search) | ≤ **1.0×** (AOT expected at parity or better) |
| Peak RSS | ≤ **1.5×** |

The earlier "20–30% initially, tightening later / tracked misses" language is removed. M8 (perf hardening) exists to *reach* these gates; **v1 does not ship until every gate is green.** Profiling via `dotnet-trace`/`EventPipe` + Linux `perf`. (If a gate proves physically unachievable on a given workload, that is escalated to the stakeholder for an explicit, documented gate change — never silently absorbed.)

---

## 10. Milestones — internal integration gates, not shippable releases (revised per Codex)

To honor "do not defer," **no milestone before full parity is described as shippable.** M0–M8 are **internal integration gates**; the **only** release is when every feature, every ported test, the differential suite, and every performance gate are green.

- **M0 — Foundation (internal).** Repo, pins (§0), `global.json`, `Directory.Build.props`, analyzers + one-type-per-file rule + no-suppression enforcement (§7.1), CI matrix, `Scout.Bytes`, `Scout.Os`, `Scout.SourceGen` skeleton. **Includes the native-entry proof-of-build spike (§4.1.1):** AOT static lib + C `main` driver + runtime init + raw non-UTF-8 `argv`/`envp` byte round-trip, built and run on all six RIDs. Gate: AOT-clean build on all RIDs **and** the entry spike green (the byte-boundary blocker is not closed until this passes).
- **M1 — Regex engine (internal).** `Scout.Automata.Syntax` + `Scout.Automata`, **all engines up front**. Gate: regex conformance suite green; prefilter micro-benches at parity.
- **M2 — Matcher + regex adapter (internal).** `Scout.Matching`, `Scout.Regex`. Gate: ported `grep-regex` unit tests green.
- **M3 — Globbing + Ignore (internal).** Incl. exact parallelism semantics (§4.8.1). Gate: ported `globset`/`ignore` tests green.
- **M4 — Searcher + Encoding (internal).** `Scout.Searching`, `Scout.Encoding`(+`.Io`). Gate: ported searcher tests + encoding conformance green.
- **M5 — Printer + CLI utils (internal).** Incl. byte-exact JSON writer (§4.5.1). Gate: ported printer/cli tests + `tests/json.rs` byte-exact.
- **M6 — PCRE2 (internal).** Static-linked per RID (§4.3). Gate: `-P` tests green on all RIDs.
- **M7 — `scout` binary (internal).** All ~104 flags, orchestration, generators. Gate: **all root integration tests + full differential suite byte-identical.**
- **M8 — Performance (internal).** Reach every §9 gate.
- **Release (v1.0) — the first and only shippable artifact.** Every feature, every test, differential suite, and all perf gates green; trademark check done; packaging per RID; `PARITY.md` complete.

Independent subtrees (M3/M4/M5) may proceed in parallel after M2, but **nothing ships** until Release.

---

## 11. Reproducibility of Generated Data

### 11.1 Unicode tables (specified, per Codex)
Unicode tables are generated from **vendored UCD data pinned to the exact version the pinned `regex-syntax` uses** (recorded in `upstream/UNICODE-VERSION` and `upstream/ucd/`), **never** "whatever UCD is current at build time." The generator is deterministic; its output is checked in and diffed in CI against a regeneration to prove reproducibility. Casing/property tables used by matching derive from this same vendored data so behavior matches the Rust `regex` crate exactly.

### 11.2 Other generators
Flag tables, help/man text, and shell completions are generated deterministically from the flag definitions; output is regenerated and diffed in CI (no drift).

---

## 12. Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Porting `regex-automata`/`regex-syntax` is enormous & subtle. | **High** | First milestone; gated by the regex crate's own conformance suite; faithful architectural port; isolated behind `IMatcher`. |
| Byte-exact output across all boundaries (argv/env/paths/stdout/**stderr**). | **High** | Raw byte I/O layer (§4.1, §4.9); ported `anyhow`-equivalent error/cause-chain formatting + verbatim message strings (§4.10.1); native-entry proof-of-build spike (§4.1.1); differential suite asserts byte-identical stdout **and stderr**. |
| `encoding_rs` exact parity. | **High** | Full port up front (§4.4.1) gated by `encoding_rs` vectors. |
| JSON byte parity vs serde_json. | **High** | Hand-written byte writer with pinned escaping (§4.5.1). |
| PCRE2 static link + reproducible native builds per RID. | **High** | Vendored source, per-RID scripts, provenance, JIT fallback (§4.3). |
| Parallelism ordering/cap parity. | High | Exact replication incl. 12-cap and forced-serial cases (§4.8.1), pinned by tests. |
| Performance gates unmet. | High | Gates block release (§9); M8 dedicated; escalate if physically infeasible. |
| Name collisions (Docker Scout, Scout APM, etc.). | Medium | Honest disclosure (§1); distinct package IDs; pre-1.0 trademark check; mechanical rename path. |
| Silent behavioral drift. | High | Differential suite (§8.3) + `PARITY.md` + no-skip policy. |
| Reproducibility (UCD/SDK/LangVersion drift). | Medium | Vendored pinned data; pinned SDK/LangVersion; regen-diff in CI (§5.1, §11). |

---

## 13. Settled Decisions & Open Reviewer Items

**Settled:**
- **Name** `Scout` (binary `scout`, **no `sc` alias**, namespace `Scout`).
- **Upstream pin** = commit `4857d6fa67db69a95cd4b6f2adda5d807d4d0119` with vendored lockfile + UCD (§0).
- **Repository** = standalone; this doc is the seed artifact.
- **Test framework** = xUnit v3.
- **Regex engines** = all ported up front (M1).
- **Encoding** = full `encoding_rs` port up front (§4.4.1).
- **JSON** = explicit byte writer (§4.5.1).
- **No new env vars** in the parity build; `SCOUT_CONFIG_PATH` removed (§4.10).
- **Performance** = hard release gates (§9).
- **Milestones** = internal gates; only v1.0 ships (§10).
- **Globalization** = `InvariantGlobalization=true`, own Unicode tables (§5.1).
- **Toolchain pins** = `net10.0`, C# `14.0`, SDK `10.0.102` (`rollForward: disable`), runtime/ILCompiler `10.0.2`, deterministic build (§5.1) — the exact versions the verified spikes used.
- **PCRE2** = `pcre2` 0.2.11 / `pcre2-sys` 0.2.10, bundled **PCRE2 10.46**, static-linked per RID (§4.3).
- **Unix `argv`** = a single statically-linked **C entry shim** across all Unix; no `/proc` dependency (§4.1).
- **SIMD baseline** = SSE2 + AVX2 (x64) and `AdvSimd`/NEON (arm64) as the shipped baseline; **AVX-512 paths are included and additive**, gated by `Avx512*.IsSupported`, and delivered **before** Release — not deferred past v1.
- **Dependency dispositions** = every lockfile crate — including `anyhow` (error/stderr parity, §4.10.1) — has a port or explicit replace/none rationale (§3.2.1).
- **Native entry** = AOT static library + C `main` driver (runtime self-initializes on first managed call — §4.1.1). **Executed and PASSED on osx-arm64** (0xFF argv byte round-tripped; Appendix A transcript). Reproducing on the remaining five RIDs is the M0 CI gate before the byte-boundary blocker is fully closed.
- **CI prerequisites** = digest-pinned container + `tests/PREREQS.lock` (tool versions + SHA-256), corpora by SHA-256, reference `rg` built from the pinned commit with its binary hash recorded (§8.5).
- **Third-party notices** = per-dependency license reproduction in `THIRD-PARTY-NOTICES.md` (decided sufficient; ripgrep's own dual license + each ported crate's license + PCRE2 BSD).
- **No-skip** = zero skipped/waived tests at Release (§8).

**Open for reviewer sign-off:** none. All previously open *design* items are decided above.

**Inception-captured values (not design decisions; pinned-inputs in plan, literals captured at first build/fetch, verified in CI):** the SDK patch level (`global.json`), the PCRE2 upstream commit SHA (`native/pcre2/UPSTREAM`), the reference `rg` binary hash, macOS decompression tool hashes, regex/encoding corpus hashes, and the remaining CI base-image digest + Linux tool versions/SHA-256 + external corpus SHA-256 values (`tests/PREREQS.lock`, §8.5, Appendix B). The literal values that already exist are committed in `tests/PREREQS.lock`.

**Implementation-risk gate (partly discharged — 2 of 6 RIDs proven):** the native-entry byte-boundary mechanism was **actually executed and PASSED on osx-arm64 (native) and osx-x64 (cross-compiled, run under Rosetta)** with .NET SDK 10.0.102; Appendix A holds both transcripts (`0xFF` argv byte round-tripped verbatim on each). The only per-arch link delta was the x64-only `libRuntime.VxsortEnabled.a`. Still **not** proven on `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64` (they need their own OS/CI environments); reproducing them in CI is the remaining M0 gate before the byte-boundary claim is treated as fully proven cross-platform. *(Correction of record: an earlier version of this line wrongly stated this environment had no .NET SDK and the spike had not run — false. The SDK is installed and the spike ran and passed on both macOS architectures.)*

---

## Appendix A — Native entry proof-of-build spike (turnkey)

Drop-in sources for the M0 spike (§4.1.1). It builds `Scout.App` as a Native AOT **static library** exporting `scout_entry`, links a C `main` driver, and echoes each `argv` byte verbatim to fd 1 — proving raw, non-UTF-8 argument capture without the lossy managed `string[]` path. Builds on all six RIDs; the non-UTF-8 round-trip assertion runs on the four Unix RIDs, while `win-x64`/`win-arm64` validate the `wmain`/`GetCommandLineW` variant (same `scout_entry` ABI, UTF-16).

**`spike/Scout.Entry/Scout.Entry.csproj`**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <OutputType>Library</OutputType>
    <NativeLib>Static</NativeLib>
    <PublishAot>true</PublishAot>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <InvariantGlobalization>true</InvariantGlobalization>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

**`spike/Scout.Entry/ScoutEntry.cs`** (one type per file; XML-doc'd)
```csharp
using System.Runtime.InteropServices;

namespace Scout.Entry;

/// <summary>Native entry point exported to the C driver. Echoes each raw
/// <c>argv</c> byte string to file descriptor 1 without any decoding.</summary>
internal static unsafe class ScoutEntry
{
    [LibraryImport("libc", EntryPoint = "write")]
    private static partial nint Write(int fd, byte* buf, nuint count);

    /// <summary>Process entry invoked by the C driver after runtime init.</summary>
    /// <param name="argc">Argument count from the C runtime.</param>
    /// <param name="argv">Raw, NUL-terminated argument byte pointers.</param>
    /// <param name="envp">Raw, NUL-terminated environment byte pointers.</param>
    /// <returns>Process exit code.</returns>
    [UnmanagedCallersOnly(EntryPoint = "scout_entry")]
    public static int Run(int argc, byte** argv, byte** envp)
    {
        for (int i = 1; i < argc; i++) // skip argv[0] (program path) for a clean round-trip assertion
        {
            byte* p = argv[i];
            nuint len = 0;
            while (p[len] != 0) len++;
            _ = Write(1, p, len);
            byte nl = (byte)'\n';
            _ = Write(1, &nl, 1);
        }
        return 0;
    }
}
```

**`spike/native/scout_main.c`** (Unix driver — consistent with §4.1.1: no explicit init call; the static lib self-initializes the runtime on first managed entry)
```c
/* Provides the real process entry. Links against the Native AOT static lib,
 * which self-initializes the runtime on the first managed call (the
 * UnmanagedCallersOnly export's prologue, from libbootstrapperdll.o). No
 * separate init call is needed for this configuration (verified on osx-arm64). */
extern int scout_entry(int argc, char **argv, char **envp);

int main(int argc, char **argv, char **envp) {
    return scout_entry(argc, argv, envp);
}
```

**`spike/build-unix.sh`** (osx-arm64 link line that was actually used; the runtime-archive set is read from the `microsoft.netcore.app.runtime.nativeaot.<RID>` pack — note the bootstrapper is the object file `libbootstrapperdll.o`, and `libaotminipal.a` is required)
```sh
#!/bin/sh
set -eu
RID="$1"   # osx-arm64 | osx-x64 | linux-x64 | linux-arm64
dotnet publish spike/Scout.Entry -r "$RID" -c Release -p:NativeLib=Static -o spike/out/"$RID"
RT="$HOME/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.$RID/10.0.2/runtimes/$RID/native"
clang spike/native/scout_main.c \
  spike/out/"$RID"/Scout.Entry.a \
  "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
  "$RT/libRuntime.WorkstationGC.a" "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
  "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
  "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
  "$RT/libSystem.Security.Cryptography.Native.Apple.a" \
  -lc++ -lobjc -lz \
  -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
  -o spike/out/"$RID"/scout-spike
# Non-UTF-8 round-trip assertion (single 0xFF arg must echo back as 0xFF + newline):
printf '\xff\n' > /tmp/expected
"spike/out/$RID/scout-spike" "$(printf '\xff')" > /tmp/got
cmp /tmp/expected /tmp/got && echo "OK $RID: non-UTF-8 argv round-trip"
```
*(On Linux the `-framework` lines are dropped and the link uses `-lpthread -ldl -lm -lrt`; the System.Security.Cryptography native archive is the OpenSSL variant rather than `.Apple`. The exact per-RID archive list is emitted by the SDK's ILCompiler and frozen during the M0 spike.)*

**Actual result on osx-arm64 (executed 2026-05-28, SDK 10.0.102) — PASS:**
```
publish-exit=0
link-exit=0          out/scout-spike  (2,197,400 bytes, Mach-O arm64 executable)
$ ./out/scout-spike A B
A
B
$ ./out/scout-spike "$(printf '\xff')" | xxd -p
ff0a                 # 0xFF preserved verbatim + trailing newline
RESULT=PASS
```
**Actual result on osx-x64 (cross-compiled from arm64, executed 2026-05-28, run under Rosetta) — PASS:**
```
publish-exit=0
link-exit=0          out-x64/scout-spike  (2,354,000 bytes, Mach-O x86_64 executable)
                     # required adding the x64-only archive: libRuntime.VxsortEnabled.a
$ ./out-x64/scout-spike "$(printf '\xff')" | xxd -p
ff0a
RESULT_X64=PASS
```
On x64 the link additionally needs `libRuntime.VxsortEnabled.a` (the GC's AVX2/AVX-512 sort, absent on arm64) — the sole per-arch link delta observed. The `0xFF` byte survives unchanged on both architectures (no U+FFFD) — proving raw non-UTF-8 `argv` reaches managed code intact via the C-shim + `scout_entry` export. The remaining **four** RIDs (`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64` — Windows via `wmain`/`GetCommandLineW` with the same `scout_entry` ABI) are reproduced in their own CI environments; CI fails M0 if any RID's link or assertion fails.

---

## Appendix B — `tests/PREREQS.lock` (real file, literal values)

**This file exists in the repo at `tests/PREREQS.lock`.** It is not a sketch — the values below were measured on this machine and the authoritative copy is the file; this appendix summarizes it.

**Literal-and-final today** (measured/known): the ripgrep commit and reference `rg` binary hash; the .NET SDK/runtime that built the verified spikes (`10.0.102`/`10.0.2`); the macOS host (`macOS 26.3.1`, `arm64`, `Apple clang 17.0.0 (clang-1700.6.4.2)`, `cargo 1.91.1`); the PCRE2 binding/sys/C-library versions, tag, and upstream commit; the macOS decompression tool versions and binary hashes (`gzip` Apple gzip 475, `bzip2` 1.0.8, `xz` 5.8.2, `zstd` 1.5.7, `lz4` 1.10.0, `brotli` 1.2.0 — all read from the host); the macOS `hyperfine` 1.20.0 Homebrew source checksum, bottle checksum, and binary hash; and the **real spike-binary hashes**:

```toml
[[spike_artifact]]
rid    = "osx-arm64"
bytes  = 2197496
sha256 = "dbd73aa6e8ccfc723c1fd60855ae77830860f1bf27aa3a483cfa8a5d625c8515"  # LITERAL, verified
result = "PASS (0xFF argv byte -> ff0a)"

[[spike_artifact]]
rid    = "osx-x64"
bytes  = 2379000
sha256 = "010ff1889ab16bd5e314f0578e567b5895ee5c9b4e54c6b4e777c6ec5d14614a"  # LITERAL, verified (Rosetta)
result = "PASS; needs libRuntime.VxsortEnabled.a"
```

**Honestly `resolved@*` (cannot be literal yet, and saying otherwise would be a lie):** a SHA-256 exists only after an artifact is first built/pulled/fetched. These are **not invented**; each is fully *determined* by inputs pinned here and frozen on first run, then CI-verified forever:
- `[linux_container].base_image_digest` + every `[[tool.linux]]` `version`/`sha256` = `resolved@build` — fully determined by `(base image, snapshot date 2026-05-01, package)`; written back as exact values at first container build. (These genuinely require building the Linux image, which cannot be done on this macOS host.)
- OpenSubtitles and linux-kernel `[[corpus]]` URL/SHA-256 values = `resolved@fetch` — frozen on first download from the pinned URLs.

This is the same discipline already applied to the SDK patch (§5.1), PCRE2 commit (§4.3), reference `rg`, regex/encoding corpora, and local macOS decompression tools: pin the determining **inputs** now, capture the resulting **hash** at inception, verify forever after.

> Library names/extra link libraries (`Scout.Entry.a`/`.lib`, runtime archive set) are finalized against the pinned SDK's ILCompiler output during the M0 spike; the shape above is the contract.
