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
fuzz, and native link checks. After `CI` succeeds on `main`, the `Release Gates`
workflow starts automatically; it can also be started manually from Actions. The
full pinned test pass and hyperfine performance gate live there because they
require the self-hosted `scout/osx-arm64` runner with:

- `self-hosted`, `scout`, and `osx-arm64` labels.
- A local release-LTO ripgrep checkout matching `tests/PREREQS.lock`.
- The pinned macOS tools from `tests/PREREQS.lock`, including hyperfine 1.20.0
  at `/opt/homebrew/bin/hyperfine`.
- Enough disk for the pinned corpora, including the OpenSubtitles text file and
  Linux source tree under `artifacts/corpora`.

The workflow first checks for an online repository runner with the required
labels so missing runner setup fails immediately instead of leaving the gates
queued. That readiness check needs a `RELEASE_GATES_RUNNER_TOKEN` secret with
repository `Administration: read` permission because GitHub requires admin read
access to list repository self-hosted runners.

`eng/fetch-corpora.sh` prints replacement `[[corpus]]` blocks for
`tests/PREREQS.lock` after it downloads OpenSubtitles and the pinned Linux
archive, hashes the decompressed `en.txt`, and hashes the extracted Linux tree
manifest. The committed lockfile now contains frozen hashes, so
`run-hyperfine.sh` can use the lockfile paths by default, or
`SCOUT_BENCH_OPENSUBTITLES_EN` and `SCOUT_BENCH_LINUX_TREE` can override them.

The script enforces the wall-time gates from `docs/DESIGN.md` and the peak RSS
ratio gate of 1.5x.
