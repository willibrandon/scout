# Scout Benchmarks

`Scout.Benchmarks` contains BenchmarkDotNet microbenchmarks. `run-hyperfine.sh`
is the CLI benchsuite driver required by the design for comparing the pinned
release-LTO `rg` oracle against a Native AOT `scout` binary.

`BoundedAssignmentBenchmarks` tracks issue #30 through the public `ByteRegex`
API. It measures `Find`, `Count`, and `FindCaptures` in optimized and
automata-only modes with 1, 100, and 800 repeated required-literal candidates.

`LargeBoundedUnicodeClassBenchmarks` tracks issue #32 through the same public
API. It compares Unicode and ASCII-equivalent compilation, then measures a cold
compile and no-match search for upper repetition bounds from 50 through 1,000.

`BoundedUrlCaptureBenchmarks` tracks issue #34's exact bounded connection-URL
pattern and matching input. It compares warmed `Find` and `FindCaptures` calls
in optimized, general, and automata-only modes, including their managed
allocations. Run it directly with:

```sh
dotnet run -c Release --project bench/Scout.Benchmarks -- \
  --filter '*BoundedUrlCaptureBenchmarks*'
```

This microbenchmark is intentionally not a hyperfine Release Gate workload;
process startup would obscure the in-process capture replay being measured.

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

The script enforces the wall-time gates from `docs/DESIGN.md` with paired ABBA
rounds. One fresh Hyperfine process measures `rg`, Scout, Scout, then `rg`, one
run per entry. Each round's ratio is the geometric mean of its two Scout/rg
comparisons, and the gate uses the median round ratio. Five measured rounds
therefore retain ten timing samples for each binary while reducing order and
phase bias as filesystem-cache and hosted-runner conditions change. Warmup
rounds use the same ABBA order. Raw per-round JSON and the aggregated wall, user
CPU, system CPU, and RSS samples remain in the output directory for diagnosis.

If a workload exceeds its timing or RSS gate, the script repeats only that
workload up to two times and requires a retry to pass the same gates. This keeps
stable regressions blocking without requiring a complete release-gate rerun for
one noisy hosted-runner sample. Set `SCOUT_GATE_RETRY_FAILED_WORKLOADS=N` to
change the retry count, or set it to `0` to disable retries.

The OpenSubtitles regex workload is a public benchmark workload. It pins
`--threads 4` so the segmented regex path is measured against a stable worker
count. The gate also prints and checks the line-aligned 128 KiB byte-segment
distribution before measuring, which catches corpus or chunking changes that
would create uneven worker input.

The Linux held-out regex workloads run Scout with
`SCOUT_REGEX_SPECIALIZATION_MODE=general`. That mode keeps structural regex
specializations but disables domain, benchmark-family, and corpus-specific
recognizers, so the gate continues to measure regex performance that is
independent of the public OpenSubtitles pattern family. The non-capturing
workload measures general alternation and prefilter behavior, and the
replacement workload measures the same class of search through capture output.

The generated `bounded_assignment_no_match` workload uses issue #30's exact
pattern and an 800-line input containing repeated `bitbucket` candidates but no
credential. `-U --count-matches --no-messages` reproduces the reported CLI path
while presenting the complete file to the regex engine as one haystack. A
no-match exit code of `1` is normalized for hyperfine; an unexpected match or
any search failure still fails the workload. Its median paired ABBA ratio must
remain at or below 1.50x the pinned `rg` oracle.

The generated `large_bounded_unicode_class_no_match` workload uses issue #32's
exact `x[\w-]{50,1000}` pattern and 5,000 deterministic no-match candidates.
Scout runs with `SCOUT_REGEX_SPECIALIZATION_MODE=general`, so the comparison
measures the general automata implementation without domain or benchmark-family
recognizers. Its median paired ABBA ratio must remain at or below 1.50x the
pinned `rg` oracle.

Median peak RSS is capped at 1.5x rg plus the measured Native AOT fixed-image
floor recorded in `docs/PARITY.md`: the script first measures an rg and
`scout-real` tiny `--mmap -n` literal RSS floor and allows the measured Scout
Native AOT floor in addition to the 1.5x rg limit for every RSS gate. On macOS,
child peak RSS is cumulative within one Hyperfine process. Every ABBA round
therefore starts a fresh process and takes RSS only from its leading rg/Scout
pair; the trailing rg cannot contaminate the oracle samples after Scout runs.
In gate mode, the bounded-assignment, large bounded Unicode-class,
OpenSubtitles, and Linux-tree workloads use five measured ABBA rounds and five
warmup ABBA rounds by default. That produces ten measured samples and ten
warmup executions per binary. Explicit `--runs` and `--warmup` values still
override the number of rounds.
