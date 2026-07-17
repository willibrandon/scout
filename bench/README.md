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

Run one release-gate workload with the same sampling and limits:

```sh
SCOUT_BIN=artifacts/bin/osx-arm64/scout \
bench/run-hyperfine.sh --gate --workload linux_heldout_capture_general
```

The focused form validates the selected workload's corpus, measures the RSS
floor required by its memory limit, and writes the same aggregate JSON as the
full gate. `bench/run-hyperfine.sh --list` prints the accepted names.
Set `SCOUT_ORACLE_ENVIRONMENT=github-actions` to use the hosted pinned rg
binary during a local comparison when that oracle archive has been restored.

GitHub's default `CI` workflow runs hosted cross-platform build, test, format,
fuzz, and native link checks. After `CI` succeeds on `main`, it dispatches the
`Release Gates` workflow with the exact successful commit SHA; the workflow can
also be started manually from Actions. Manual performance-only reruns should set
`gate=performance` and `checkout_ref` to the commit SHA under test. Release
Gates repeat the native
entrypoint and final `scout` executable smoke checks on all six hosted release
RIDs: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, and
`win-arm64`. Full pinned test passes run on all six hosted release RIDs with
captured oracle and tool pin support.
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

The script enforces the wall-time gates from `docs/DESIGN.md` with paired
ABBA/BAAB cycles. One fresh Hyperfine process runs `rg`, Scout, Scout, `rg`; the
next runs Scout, `rg`, `rg`, Scout. A cycle ratio is the geometric mean of those
two round ratios, and the gate uses the median cycle ratio. Six valid measured
rounds therefore provide twelve timing samples for each binary while balancing
every command position as filesystem-cache and hosted-runner conditions change.
Warmup rounds alternate in the same way. A zero-valued measured wall time is a
timer-resolution artifact, so the harness discards the entire balanced round
and repeats the same ABBA or BAAB position schedule. It collects six valid
measured rounds, allows at most eight timer-resolution replacements per
workload attempt, and records each discarded round for diagnosis.
Raw per-round JSON and the aggregated wall, user CPU, system CPU, and RSS
samples remain in the output directory for diagnosis. The hosted performance
job uploads the top-level aggregate JSON even when the gate fails, so the exact
inputs to every completed attempt remain
available without uploading the much larger per-round sample tree.
Each aggregate records the exact rg and Scout command lines. The gate log begins
with a compact reproducibility manifest containing the host OS and architecture,
logical CPU count, binary versions and hashes, harness and Hyperfine hashes,
frozen corpus hashes, selected workload, and fixed thread counts. A focused
`--workload` run also prints its two commands in the log for direct local-to-CI
comparison.

Each attempt prints the wall and RSS components as either within or exceeding
their limits, followed by one overall result that names the workload and failed
component or components. The CPU line reports the ratio of median combined user
and system time as supporting diagnostic data; wall time and RSS remain the gate
dimensions. RSS is compared in exact bytes. Its report includes exact byte
counts, three-decimal MiB values, the measured floor and formula, and signed
headroom or excess so a close failure cannot look equal to its rounded limit.

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
All four Linux-tree workloads pass `--threads 3` to both rg and Scout. The fixed
worker count keeps local and hosted runs comparable when the machines expose
different logical CPU counts.

The generated `bounded_assignment_no_match` workload uses issue #30's exact
pattern and an 800-line input containing repeated `bitbucket` candidates but no
credential. `-U --count-matches --no-messages` reproduces the reported CLI path
while presenting the complete file to the regex engine as one haystack. A
no-match exit code of `1` is normalized for hyperfine; an unexpected match or
any search failure still fails the workload. Its median balanced-cycle ratio must
remain at or below 1.50x the pinned `rg` oracle.

The generated `large_bounded_unicode_class_no_match` workload uses issue #32's
exact `x[\w-]{50,1000}` pattern and 5,000 deterministic no-match candidates.
Scout runs with `SCOUT_REGEX_SPECIALIZATION_MODE=general`, so the comparison
measures the general automata implementation without domain or benchmark-family
recognizers. Its median balanced-cycle ratio must remain at or below 1.50x the
pinned `rg` oracle.

The issue #37, #36, and #44 gates share a deterministic CRLF corpus made from
200,000 Paladin-like four-line records, followed by the four delegate
declarations from the issue #36 reproduction. The issue #37 workloads measure
the prefilter-free `\b\w{5}\s+\w{5}\s+\w{5}\b` expression through both line
and match counting, the exact `\bGeneratedRecord\b` expression, the anchored
declaration expression, and both the CRLF-aware and exact
`^[A-Za-z_]{70,90}$` identifier-class expressions. The issue #36 workload
measures the original four-branch shared-prefix alternation across four
sequential scans of the corpus, keeping each timed command above the host timer
resolution. The issue #44 workloads search for the same 64 absent literals once
through repeated `-e` arguments and once through a pattern file.
Every command pins `--threads 1 --mmap` and uses
either `--count` or `--count-matches`. Scout runs with
`SCOUT_REGEX_SPECIALIZATION_MODE=general` so these gates
exercise the authoritative matcher and its conservative prefilters. Each median
balanced-cycle ratio must remain at or below 1.50x the pinned `rg` oracle.

Median peak RSS is capped at 1.5x rg plus the measured Native AOT fixed-image
floor recorded in `docs/PARITY.md`: the script first measures rg and
`scout-real` tiny `--mmap -n` literal RSS floors from alternating first-position
samples in fresh Hyperfine processes. It allows the measured Scout Native AOT
floor in addition to the 1.5x rg limit for every RSS gate. Fresh processes matter
because, on macOS, child peak RSS is cumulative within one Hyperfine process.
Only the leading command supplies a clean RSS sample. Alternating rounds put rg
and Scout first once per cycle, so neither measurement inherits the other
process's peak.

In gate mode, every workload uses six valid measured rounds and six warmup
rounds by default. That produces twelve measured timing samples and three clean RSS
samples per binary, plus twelve warmup executions per binary. An explicit gate
`--runs` value must be even so every ABBA round has its BAAB partner. An explicit
gate `--warmup` value must also be even; zero disables warmups.
