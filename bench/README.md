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

`eng/fetch-corpora.sh` prints replacement `[[corpus]]` blocks for
`tests/PREREQS.lock` after it downloads OpenSubtitles and the pinned Linux
archive, hashes the decompressed `en.txt`, and hashes the extracted Linux tree
manifest. `--gate` intentionally fails until those external corpus hashes are
frozen instead of `resolved@fetch`. Once frozen, `run-hyperfine.sh` can use the
lockfile paths by default, or `SCOUT_BENCH_OPENSUBTITLES_EN` and
`SCOUT_BENCH_LINUX_TREE` can override them.

The script enforces the wall-time gates from `docs/DESIGN.md` and the peak RSS
ratio gate of 1.5x.
