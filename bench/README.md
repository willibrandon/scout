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
bench/run-hyperfine.sh --gate
```

Run one release-gate workload with the same sampling and limits:

```sh
bench/run-hyperfine.sh --gate --workload linux_heldout_capture_general
```

The focused form validates the selected workload's corpus, measures the RSS
floor required by its memory limit, and writes the same aggregate JSON as the
full gate. It is a diagnostic run; the full `--gate` command executes the
release-gate workload sequence. `bench/run-hyperfine.sh --list` prints the
accepted names. Gate mode selects the hosted pinned rg binary by default.
The workflow and local release gate both invoke `bench/run-hyperfine.sh --gate`.
Every gate form delegates to a shared driver that restores the oracle,
provisions the SHA-512-pinned macOS arm64 .NET SDK in disposable state,
validates the exact SDK and runtime inventory, restores the checksum-pinned
Hyperfine bottle into disposable state, validates the corpora, runs preflight,
builds Native AOT, and invokes the gate. A complete
gate requires committed and clean performance inputs; a focused workload uses
the same release-equivalent preparation while developing a change.
Both complete and focused shared-driver runs use the hosted pinned rg oracle and
the lockfile corpus paths.
The build runs from a detached worktree at the selected commit. Pinned corpus
archives and the oracle cache are shared. Corpus files, the Linux tree, build
outputs, and benchmark outputs are created inside that worktree; NuGet packages
use disposable state outside it. The aggregate JSON and raw per-round sample
directories are copied back when the run ends.
Oracle restoration, corpus materialization, and build orchestration use fresh
home, temporary, XDG, .NET CLI, and Python user state for every run. The driver
selects the pinned host-tool set, then clears inherited CI, runner, Homebrew,
Git, native-toolchain, .NET, NuGet, allocator, and Python configuration.
The rg oracle is the hosted release-LTO binary in both environments. Host tools
use the frozen local or GitHub Actions hash set for the machine that executes
the driver; decompressed corpus hashes and the Hyperfine binary remain fixed.

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
archive for the hosted RID. The performance driver restores the exact locked
Hyperfine bottle into its private state, fetches the pinned corpora into
`artifacts/corpora`, and verifies every frozen hash from `tests/PREREQS.lock`
before measuring. Cancelled, failed, or stale CI
completions do not queue release work. It does not require any personal machine,
privately managed runner, or repository secret.

`eng/fetch-corpora.sh` prints replacement `[[corpus]]` blocks for
`tests/PREREQS.lock` after it downloads OpenSubtitles and the pinned Linux
archive, hashes the decompressed `en.txt`, and hashes the extracted Linux tree
manifest. The committed lockfile now contains frozen hashes, so
the shared driver verifies the archives, materializes fresh corpus contents,
and uses the lockfile paths.

The script enforces the wall-time gates from `docs/DESIGN.md` with paired
ABBA/BAAB cycles. One fresh Hyperfine process runs `rg`, Scout, Scout, `rg`; the
next runs Scout, `rg`, `rg`, Scout. A cycle ratio is the geometric mean of those
two round ratios, and the gate uses the median cycle ratio. Ten valid measured
rounds therefore provide twenty timing samples for each binary and five cycle
ratios while balancing every command position as filesystem-cache and
hosted-runner conditions change. Two warmup rounds alternate in the same way,
preserving the previous total of twelve warmup and measured rounds while moving
more observations into the prespecified result. A zero-valued measured wall time is a
timer-resolution artifact, so the harness discards the entire balanced round
and repeats the same ABBA or BAAB position schedule. It collects ten valid
measured rounds, allows at most eight timer-resolution replacements per
workload attempt, and records each discarded round for diagnosis.
Raw per-round JSON and the aggregated wall, user CPU, system CPU, and RSS
samples remain in the output directory for diagnosis. The hosted performance
job uploads the aggregate, output-verification, and raw per-round JSON even when
the gate fails, so the exact inputs and samples from every completed attempt
remain available.
Each aggregate records the exact rg and Scout argument vectors, working
directory, allowlisted environment, and expected exit code. Hyperfine invokes
the timed commands directly instead of starting a shell. Generated benchmark
inputs are required to match the complete byte counts and SHA-256 hashes in
`tests/PREREQS.lock`; their deterministic manifest is embedded in every output
verification and timing aggregate. The same artifacts embed a deterministic
reproducibility manifest with the macOS build, hardware model, runner image,
process state, binary hashes, build provenance, and pinned toolchain. The gate
verifies that the Native AOT
payload was built from the current source content. Its
reproducibility manifest records the OS build, hardware model, logical CPU
count, source fingerprint, build toolchain, launcher and payload hashes,
performance-input commit and fingerprint, Hyperfine hash, frozen corpus hashes,
selected workload, and fixed thread counts. A focused `--workload` run also
prints its two commands.
Before timing, the gate executes both commands once and compares a C-locale
sorted-line SHA-256 digest. The digest, byte count, and line count are written
to the uploaded workload output JSON. Search workloads require equivalent
output. The cold-version workload records each program's version output
independently because the product names and versions differ.
Both verification and timing run with a fresh home and temporary directory and
an allowlisted locale, timezone, and system path. Developer Git ignores, rg
configuration, .NET runtime knobs, allocator diagnostics, and runner-specific
environment variables therefore cannot change the searched files or measured
processes.

Each attempt prints the wall and RSS components as either within or exceeding
their limits, followed by one overall result that names the workload and failed
component or components. The CPU line reports the ratio of median combined user
and system time as supporting diagnostic data; wall time and RSS remain the gate
dimensions. RSS is compared in exact bytes. Its report includes exact byte
counts, three-decimal MiB values, the measured floor and formula, and signed
headroom or excess so a close failure cannot look equal to its rounded limit.

The shared release-equivalent driver requires the source, native build inputs,
and complete performance harness to be committed and clean. Each workload has
one prespecified warmup and measured sample set. The full gate evaluates every
workload after a performance-limit failure and prints all failed workload names
in its final summary. An output, sampling, build, or other infrastructure error
stops the gate immediately.

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
worker count makes search concurrency identical. The manifest records the host
hardware and OS build; the hosted `macos-26` result is the release decision.
Tree commands run from the corpus root with `.` as the argument, giving local
and hosted runs the same output paths.

The generated `bounded_assignment_no_match` workload uses issue #30's exact
pattern and an 800-line input containing repeated `bitbucket` candidates but no
credential. `-U --count-matches --no-messages` reproduces the reported CLI path
while presenting the complete file to the regex engine as one haystack. A
no-match exit code of `1` is normalized for hyperfine; an unexpected match or
any search failure still fails the workload. Both commands pin `--threads 1`.
Its median balanced-cycle ratio must
remain at or below 1.50x the pinned `rg` oracle.

The generated `large_bounded_unicode_class_no_match` workload uses issue #32's
exact `x[\w-]{50,1000}` pattern and 5,000 deterministic no-match candidates.
Scout runs with `SCOUT_REGEX_SPECIALIZATION_MODE=general`, so the comparison
measures the general automata implementation without domain or benchmark-family
recognizers. Both commands pin `--threads 1`. Its median balanced-cycle ratio
must remain at or below 1.50x the
pinned `rg` oracle.

The issue #37, #36, #44, and #46 gates share a deterministic CRLF corpus made
from 200,000 Paladin-like four-line records, followed by `PaladinRecord` and
`PaladinValue` sentinels and the four delegate declarations from the issue #36
reproduction. The issue #37 workloads measure
the prefilter-free `\b\w{5}\s+\w{5}\s+\w{5}\b` expression through both line
and match counting, the exact `\bGeneratedRecord\b` expression, the anchored
declaration expression, and both the CRLF-aware and exact
`^[A-Za-z_]{70,90}$` identifier-class expressions. The issue #36 workload
measures the original four-branch shared-prefix alternation across four
sequential scans of the corpus, keeping each timed command above the host timer
resolution. The issue #44 workloads search for the same 64 absent literals
through repeated `-e` arguments and a pattern file. Each timed command scans
sixteen copies of the input argument, keeping the absent-pattern comparison
above Hyperfine's short-command warning range. The issue #46 workloads use the
exact nested finite-language match and no-match expressions. Their commands
scan two and four copies respectively so both paths stay above host timer
resolution.
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

In gate mode, every workload uses ten valid measured rounds and two warmup
rounds by default. That produces twenty measured timing samples and five clean
RSS samples per binary, plus four warmup executions per binary. An explicit gate
`--runs` value must be even so every ABBA round has its BAAB partner. An explicit
gate `--warmup` value must also be even; zero disables warmups.
