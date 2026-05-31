# Upstream

This project ports ripgrep's `grep-pcre2` workspace crate and the Rust `pcre2`
binding surface from the pinned lockfile.

```text
name = "grep-pcre2"
version = "0.1.9"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/pcre2"

name = "pcre2"
version = "0.2.11"
checksum = "9e970b0fcce0c7ee6ef662744ff711f21ccd6f11b7cf03cd187a80e89797fc67"

name = "pcre2-sys"
version = "0.2.10"
checksum = "18b9073c1a2549bd409bf4a32c94d903bb1a09bf845bc306ae148897fa0760a4"
```

The vendored C library provenance is recorded in `native/pcre2/UPSTREAM`.
Managed interop uses source-generated `LibraryImport` declarations and links
the per-RID static `libpcre2-8`/`pcre2-8.lib` build into the AOT entry image.
Directory searches use the same sorted-serial/default-parallel walker planning
as the default engine; parallel PCRE2 workers each own a compiled regex and
match-data block so native match state is never shared across worker threads.
PCRE2 search statistics are aggregated through the shared `SearchStats` output
format and covered by native differentials against the pinned `rg -P` binary.
JSON output covers line-oriented PCRE2 context and passthru searches, including
native differential coverage for look-around patterns. Line-oriented vimgrep
output, including context, replacement, and multiline records, is also covered
by pinned native differentials.
