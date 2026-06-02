# Scout Benchmarks

`Scout.Benchmarks` contains BenchmarkDotNet microbenchmarks. `run-hyperfine.sh`
is the CLI benchsuite driver required by the design for comparing the pinned
release-LTO `rg` oracle against a Native AOT `scout` binary.

Smoke run:

```sh
bench/run-hyperfine.sh --smoke
```

Release-gate run:

```sh
eng/fetch-corpora.sh --all

SCOUT_BIN=artifacts/bin/osx-arm64/scout \
bench/run-hyperfine.sh --gate
```

GitHub's default `CI` workflow runs hosted cross-platform build, test, format,
fuzz, and native link checks. After `CI` succeeds on `main`, it dispatches the
`Release Gates` workflow with the exact successful commit SHA; the workflow can
also be started manually from Actions. The full pinned test pass and hyperfine
performance gate run on GitHub-hosted `macos-26` runners. The pinned CI runner
labels are `ubuntu-24.04, ubuntu-24.04-arm, macos-26-intel, macos-26,
windows-2025-vs2026, and windows-11-arm`; those are the only labels the
configuration allows. The workflow builds the pinned release-LTO `rg` oracle
from source, installs hyperfine with Homebrew, fetches the pinned corpora into
`artifacts/corpora`, and verifies every frozen hash from `tests/PREREQS.lock`
before measuring. Cancelled, failed, or stale CI completions do not queue
release work. It does not require any personal machine, privately managed
runner, or repository secret.

`eng/fetch-corpora.sh` prints replacement `[[corpus]]` blocks for
`tests/PREREQS.lock` after it downloads OpenSubtitles and the pinned Linux
archive, hashes the decompressed `en.txt`, and hashes the extracted Linux tree
manifest. The committed lockfile now contains frozen hashes, so
`run-hyperfine.sh` can use the lockfile paths by default, or
`SCOUT_BENCH_OPENSUBTITLES_EN` and `SCOUT_BENCH_LINUX_TREE` can override them.

The script enforces the wall-time gates from `docs/DESIGN.md` with hyperfine's
median wall time, plus a median per-run peak RSS gate of 1.5x or 32 MiB over rg,
whichever is larger. Median timing and median per-run peak RSS keep all
hosted-runner samples while preventing one noisy sample from deciding the
release gate. In gate mode, the OpenSubtitles workloads use five runs and two
warmups, and the Linux-tree workloads use five runs and three warmups by default
because hosted macOS filesystem timings are noisier; explicit `--runs` and
`--warmup` values still override those defaults.
