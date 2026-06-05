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
`--mmap -n` literal RSS floors in the same hyperfine run. Every peak-RSS gate
then requires `scout <= (rg * 1.5) + measured_scout_native_aot_floor`, so the
fixed runtime/image floor is measured per run instead of hidden behind a magic
constant.
