# Parity Ledger

Scout has no accepted runtime deviations from the pinned ripgrep behavior.

Pre-release implementation work may temporarily add tracked gaps here, but the
release gate requires this file to contain zero waived, skipped, or accepted
behavioral mismatches.

## Tracked Gaps

None.

## Performance Gate Escalations

### Native AOT fixed RSS floor for `subtitles_en_literal`

Status: accepted explicit gate change under `docs/DESIGN.md` §9.

Scope: peak RSS for the large single-file literal OpenSubtitles workload only.
Regex and all other release-gate workloads keep the strict `1.5x rg` peak RSS
ratio.

Rationale: after stripping local symbols from `scout-real`, direct Native AOT
tiny `--mmap -n` literal search measured a fixed process floor of about `10.0 MB`,
while the 9.3 GB OpenSubtitles literal scan measured about `10.2 MB`. The large-file
literal scan adds less than `1 MB` over the Native AOT process floor, so the
remaining delta is the managed AOT runtime/image cost rather than a corpus-sized
buffer or leak. The pinned `rg` literal RSS floor on the same `--mmap -n` probe
is about `4.3 MB`.

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
`--mmap -n` literal RSS floors in the same hyperfine run and applies only
`max(0, scout_floor - rg_floor)` as a fixed Native AOT allowance for
`subtitles_en_literal`; every other workload still fails above `1.5x rg`.
