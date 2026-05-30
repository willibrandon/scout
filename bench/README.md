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
SCOUT_BIN=artifacts/bin/osx-arm64/scout \
SCOUT_BENCH_OPENSUBTITLES_EN=/path/to/en.txt \
SCOUT_BENCH_LINUX_TREE=/path/to/linux \
bench/run-hyperfine.sh --gate
```

`--gate` intentionally fails until the external corpus hashes in
`tests/PREREQS.lock` are frozen instead of `resolved@fetch`. When those
corpora are frozen, the script enforces the wall-time gates from `docs/DESIGN.md`
and the peak RSS ratio gate of 1.5x.
