# Scout

A feature-complete port of [ripgrep](https://github.com/BurntSushi/ripgrep) to C# / .NET Native AOT.

Scout recursively searches a directory tree for a regex pattern, respecting `.gitignore` by
default and skipping hidden and binary files. It aims to reproduce ripgrep's behavior — identical
exit codes and search output — while compiling ahead-of-time to native code. It is a genuine
reimplementation, not a wrapper: the regex engine, the directory walker, the glob and ignore
logic, and the PCRE2 integration are all ported, so Scout *behaves like* ripgrep rather than
calling it.

> **Status:** v0.2.3, tracking ripgrep 15.1.0 (commit `4857d6fa67`). Functional and fully
> tested — 3,479 tests pass on all six supported platforms. The release workflow publishes native
> binaries, .NET tool packages, Homebrew, Scoop, and winget.

## Example

```console
$ scout -n InvariantGlobalization Directory.Build.props
15:    <InvariantGlobalization>true</InvariantGlobalization>

$ scout -l NativeLib native/
native/build-app-unix.sh
native/build-app-windows.ps1
```

Scout's command line mirrors ripgrep's: if you know `rg`, you know `scout`. Run `scout --help`
for the full reference.

## Installation

Choose one installation method:

**.NET tool**

```sh
dotnet tool install -g Scout
```

**Homebrew**

```sh
brew install willibrandon/tap/scout
```

**Scoop**

```sh
scoop bucket add willibrandon https://github.com/willibrandon/scoop-bucket
scoop install scout
```

**winget**

```sh
winget install willibrandon.scout
```

Standalone archives are attached to each
[GitHub Release](https://github.com/willibrandon/scout/releases) for `linux-x64`,
`linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, and `win-arm64`. Windows releases
also include MSI installers for `win-x64` and `win-arm64`.

## Building

The managed projects build with the pinned .NET 10 SDK; the `scout` binary additionally needs a C
toolchain, since the launcher and PCRE2 are compiled and linked per platform.

Prerequisites:

- **.NET SDK 10.0.102** (pinned in `global.json`).
- **A C toolchain:** `clang` on Unix; a Visual Studio Developer Command Prompt (`cl.exe`,
  `link.exe`, `lib.exe`) on Windows.
- **For native differentials on Windows:** MSYS/Git Bash plus the decompression tools installed
  by `eng/install-windows-host-prereqs.ps1`.

```sh
# Managed unit tests:
dotnet test Scout.slnx

# Build the scout binary for your platform:
native/build-app-unix.sh osx-arm64     # -> artifacts/bin/osx-arm64/ (scout + scout-real)
#   native/build-app-windows.ps1 on Windows (single scout.exe)
```

The differential and conformance suites additionally need the pinned ripgrep oracle and benchmark
corpora; CI restores the captured oracle via `eng/restore-ripgrep-oracle.*`, fetches corpora via
`eng/fetch-corpora.sh --all`, and verifies the result with `eng/preflight.sh`.

Supported runtimes: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`.

## How it works

ripgrep's behavior and speed live as much in its dependencies as in its CLI, so Scout ports both.
The source tree maps recognizably onto ripgrep's crates. The pieces worth knowing:

- **Bytes, not strings.** Scout's search and output core works on `ReadOnlySpan<byte>`. OS
  arguments and paths use the platform's native form — raw bytes on Unix, UTF-16 on Windows, like
  ripgrep — so non-UTF-8 Unix input round-trips instead of being mangled.
- **A ported regex engine** (`Scout.Automata`, after Rust's `regex-automata`): a byte-oriented
  finite-automata engine with NFA/PikeVM, lazy and dense/sparse DFAs, a meta strategy selector,
  and `memchr`/Aho-Corasick/Teddy prefilters — not the BCL `Regex`, whose syntax and semantics
  differ from the Rust crate the tests are pinned to.
- **PCRE2** (`Scout.Pcre2`), statically linked, for `-P` — with the same engine selection and
  fallback as upstream.
- **Native AOT.** The tool compiles ahead-of-time: fast cold start, no JIT. On Unix it ships as a
  small C launcher (`scout`) that captures raw argument bytes and execs the AOT image
  (`scout-real`); on Windows, a single `scout.exe`.

## Compatibility

Scout's contract is *behavioral parity* with ripgrep, checked continuously. A differential suite
runs Scout and the pinned `rg` over a large corpus and compares exit codes and output; ripgrep's
own integration tests and the regex and encoding conformance corpora run alongside it — 3,479
tests, zero skipped, on every supported platform.

Exit codes and deterministic search output match ripgrep exactly. The differential harness
normalizes elapsed-time fields and nondeterministic path ordering, and has explicit presence-only
checks where the upstream test only requires output to exist. `stderr` is
intentionally Scout's own: its version banner, help text, error prefix, and `--debug` output
identify `scout`, not ripgrep. Any other difference is a bug, tracked in
[`docs/PARITY.md`](docs/PARITY.md).

## Performance

Measured against release-LTO ripgrep with `hyperfine` on the standard benchmark corpora
(OpenSubtitles, a Linux kernel tree), Scout must meet every hosted Release Gate threshold.
Wall-time ratio is Scout ÷ ripgrep; below 1.0 is faster than ripgrep.

| Workload | Release gate |
|---|--:|
| Literal scan, large file | ≤1.20× |
| Regex scan, large file | ≤1.20× |
| Recursive literal, large tree | ≤1.25× |
| Many small files, parallel | ≤1.30× |
| Cold start (`--version`) | ≤1.00× |
| Cold start (tiny search) | ≤1.00× |

The hosted Release Gates workflow is the source of truth for observed ratios. It measures
each workload in both command orders and uses combined median samples to avoid hosted-runner
order bias. Resident memory carries the managed runtime's fixed image cost above ripgrep's;
the accounting is documented in `docs/PARITY.md`.

## License

Scout's own code is released under the [MIT License](LICENSE). It is a port of ripgrep
(MIT OR Unlicense) and preserves the original author's copyright; PCRE2 (BSD-3-Clause) and every
other ported or bundled component have their licenses reproduced in
[`docs/THIRD-PARTY-NOTICES.md`](docs/THIRD-PARTY-NOTICES.md).
