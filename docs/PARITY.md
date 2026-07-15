# Parity Ledger

Scout has no accepted behavioral deviations from the pinned ripgrep behavior.
Identity surfaces are intentionally Scout-specific, enumerated below, and
guarded by Scout golden tests plus the rebrand audit.

Pre-release implementation work may temporarily add tracked gaps here, but the
release gate requires this file to contain zero waived, skipped, or accepted
behavioral mismatches.

## Accepted Identity Surfaces

These are runtime surfaces where Scout intentionally differs from the pinned
`rg` binary because they identify the product rather than search behavior:

- Version banners: `-V` and `--version` print Scout's version while retaining
  the pinned ripgrep compatibility coordinate.
- Top-level stderr program prefixes use `scout:` instead of `rg:`.
- `--debug`/`--trace` structural fields use Scout's program prefix, stable
  Scout categories, and repo-relative Scout source locations. Debug message
  bodies remain behavior and are compared against the pinned `rg`.
- Help, man page, and shell completions are the deterministic Scout identity
  transform of the pinned `rg` artifacts. Completions register only `scout`.
- Help/man configuration and ignore-file documentation names
  `SCOUT_CONFIG_PATH` and `.scoutignore`, while documenting
  `RIPGREP_CONFIG_PATH` and `.rgignore` as compatibility inputs.
- Product-voice diagnostics say Scout where the message identifies the tool,
  for example the missing-pattern diagnostic and PCRE2 unavailable text.
- Homepage, issue, and visible credit text identify Scout while preserving
  ripgrep attribution in `THIRD-PARTY-NOTICES.md`.

## Tracked Gaps

None.

## Performance Gate Escalations

### Regex specialization ablation modes

Status: active release-gate control.

Scope: Scout's default regex engine dispatch.

Scout has three internal regex specialization modes, selected for the CLI by
`SCOUT_REGEX_SPECIALIZATION_MODE`: `default`, `general`, and `fallback`.
`default` is normal release behavior. `general` keeps structural fast paths,
including literal sets, bounded digit/delimiter families, fixed-width and
delimited-sequence engines, and structural capture extractors. It disables
domain, benchmark-family, and corpus-specific recognizers, including
IP/email/URI, LH3 email/URI, noqa, keyword/operator spacing, path-semver,
Bible-reference, and Rust predicate capture recognizers. `fallback` disables
recognizer fast paths, prefilters, start predicates, and required-search
guards, leaving only the core NFA/DFA engine selection.

The OpenSubtitles regex workload remains a public benchmark workload. The
release gate also includes `bounded_assignment_no_match`, the generated issue
#30 regression; `large_bounded_unicode_class_no_match`, the generated issue #32
regression running in general mode; generated general-mode line-search gates for
issues #37, #36, and #44; `linux_heldout_regex_general`, a Linux-tree regex
workload that runs Scout with `SCOUT_REGEX_SPECIALIZATION_MODE=general`; and
`linux_heldout_capture_general`, a replacement workload over the same held-out
pattern family that exercises capture output. These gates compare Scout against
the pinned `rg` and are independent of benchmark-specific fast paths.

Current ablation collection:

| Mode | Workload | Recorded ratio | Gate | Coverage |
| --- | --- | --- | --- | --- |
| `default` | `subtitles_en_regex` | `0.797x` release gate | `<= 1.20x` | Public benchmark workload under normal release behavior. |
| `default` | `bounded_assignment_no_match` | Pending first release-gate sample | `<= 1.50x` | Generated issue #30 no-match scan over 800 repeated required-literal candidates. |
| `general` | `large_bounded_unicode_class_no_match` | Pending first release-gate sample | `<= 1.50x` | Generated issue #32 no-match scan over 5,000 candidates using the general automata implementation. |
| `general` | `line_regex_word_boundary_general` | Pending first release-gate sample | `<= 1.50x` | Issue #37 word-boundary scan over the generated Paladin-like corpus. |
| `general` | `line_regex_anchored_general` | Pending first release-gate sample | `<= 1.50x` | Issue #37 anchored CRLF declaration scan over the generated Paladin-like corpus. |
| `general` | `line_regex_bounded_class_general` | Pending first release-gate sample | `<= 1.50x` | Issue #37 bounded ASCII identifier-class scan over the generated Paladin-like corpus. |
| `general` | `shared_delegate_prefix_general` | Pending first release-gate sample | `<= 1.50x` | Issue #36 shared-prefix alternation over four sparse delegate declarations. |
| `general` | `many_absent_regexp_general` | Pending first release-gate sample | `<= 1.50x` | Issue #44 no-match scan with 64 repeated `-e` expressions. |
| `general` | `many_absent_pattern_file_general` | Pending first release-gate sample | `<= 1.50x` | Issue #44 no-match scan with the same 64 expressions supplied through `-f`. |
| `general` | `linux_heldout_regex_general` | `1.458x` release-gate retry | `<= 1.50x` | Held-out Linux-tree regex with domain, benchmark-family, and corpus-specific recognizers disabled. |
| `general` | `linux_heldout_capture_general` | `1.520x` release gate | `<= 1.75x` | Held-out Linux-tree replacement workload through capture output with the same recognizers disabled. |
| `fallback` | Local diagnostic control. | Not release-gated. | N/A | Spot-checks core automata behavior without recognizer or guard acceleration; it is not used for a release-speed claim. |

### Native AOT fixed RSS floor for release RSS gates

Status: accepted explicit gate change under `docs/DESIGN.md` §9.

Scope: peak RSS for every release-gate workload.

Rationale: after stripping local symbols from `scout-real`, direct Native AOT
tiny `--mmap -n` literal search measured a fixed process floor of about `10.0 MB`.
The 9.3 GB OpenSubtitles literal scan stays near that floor, and the Linux-tree
literal scan's remaining delta is likewise approximately the measured floor
rather than corpus-sized retained input. The OpenSubtitles regex scan uses
ordered internal segment workers to recover throughput without changing output
ordering; its reducible segment memory remains bounded by the same gate. The
pinned `rg` literal RSS floor on the same `--mmap -n` probe is about `4.3 MB`.

Attribution: the stripped osx-arm64 `scout-real` Mach-O is close to the pinned
`rg` file size (`5.16 MB` vs `3.15 MB`), but it carries more resident startup
image/runtime state: `size -m` reports `scout-real` at about `4.80 MB` `__TEXT`
plus `1.41 MB` `__DATA` VM, including the Native AOT `hydrated` section, versus
`rg` at about `2.87 MB` `__TEXT` plus `16 KB` `__DATA` VM. On macOS the Native
AOT image also links the platform runtime support libraries (`Foundation`,
`CoreFoundation`, `libobjc`, `libc++`, `libz`) that the pinned `rg` binary does
not. This is why the literal workload's RSS is higher even though its search
buffer is only `128 KiB`.

Guard: `bench/run-hyperfine.sh --gate` measures the rg and `scout-real` tiny
`--mmap -n` literal RSS floors from alternating first-position samples in fresh
Hyperfine processes so macOS does not carry one child's cumulative peak into the
other measurement. The fixed runtime/image floor is measured per run instead of
hidden behind a magic constant. Every peak-RSS gate then requires
`scout <= (rg * 1.5) + measured_scout_native_aot_floor`.
