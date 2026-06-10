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
also be started manually from Actions. Manual performance-only reruns should set
`gate=performance` and `checkout_ref` to the commit SHA under test. Release
Gates repeat the native
entrypoint and final `scout` executable smoke checks on all six hosted release
RIDs: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, and
`win-arm64`. Full pinned test passes currently run on the hosted Unix RIDs with
complete oracle/tool pin support: `linux-x64`, `linux-arm64`, and `osx-arm64`.
The hyperfine performance gate runs on GitHub-hosted `macos-26` arm64. The
pinned CI runner labels are `ubuntu-24.04, ubuntu-24.04-arm, macos-26-intel,
macos-26, windows-2025-vs2026, and windows-11-arm`; those are the only labels
the configuration allows. The workflow restores the captured pinned release-LTO `rg` oracle
archive for the hosted RID, installs hyperfine with Homebrew where
required, fetches the pinned corpora into `artifacts/corpora`, and verifies every frozen hash from
`tests/PREREQS.lock` before measuring. Cancelled, failed, or stale CI
completions do not queue release work. It does not require any personal machine,
privately managed runner, or repository secret.

`eng/fetch-corpora.sh` prints replacement `[[corpus]]` blocks for
`tests/PREREQS.lock` after it downloads OpenSubtitles and the pinned Linux
archive, hashes the decompressed `en.txt`, and hashes the extracted Linux tree
manifest. The committed lockfile now contains frozen hashes, so
`run-hyperfine.sh` can use the lockfile paths by default, or
`SCOUT_BENCH_OPENSUBTITLES_EN` and `SCOUT_BENCH_LINUX_TREE` can override them.

The script enforces the wall-time gates from `docs/DESIGN.md` with hyperfine's
median wall time. In gate mode, every workload is measured in both command
orders (`rg` then `scout`, and `scout` then `rg`); the timing gate uses the
combined median samples for each binary. This removes command-order bias from
hosted macOS runners, where hyperfine 1.20 runs each command group in input
order instead of interleaving individual runs.

The OpenSubtitles regex workload pins `--threads 4` so the segmented regex path
is measured against a stable worker count. The gate also prints and checks the
line-aligned 128 KiB byte-segment distribution before measuring, which catches
corpus or chunking changes that would create uneven worker input.

Median peak RSS is capped at 1.5x rg plus the measured Native AOT fixed-image
floor recorded in `docs/PARITY.md`: the script first measures an rg and
`scout-real` tiny `--mmap -n` literal RSS floor and allows the measured Scout
Native AOT floor in addition to the 1.5x rg limit for every RSS gate. Peak RSS
is judged from the rg-first run so the later rg command cannot inherit an
earlier scout process peak on macOS.
In gate mode, the OpenSubtitles workloads use five runs and five warmups, and the
Linux-tree workloads use five runs and five warmups by default because hosted
macOS filesystem timings are noisier; explicit `--runs` and `--warmup` values
still override those defaults.
