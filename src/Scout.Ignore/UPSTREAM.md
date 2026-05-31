# Upstream

Ports the traversal surface of ripgrep's `ignore` workspace crate.

```text
name = "ignore"
version = "0.4.25"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
path = "crates/ignore"
```

- Initial covered surface: `walk::WalkBuilder`, `walk::Walk`, `walk::WalkParallel`, `walk::WalkState`, `walk::DirEntry`, hidden filtering, depth limits, max file size, symlink traversal, same-file loop protection, same-file-system boundaries, explicit overrides, explicit ignore files, standard ignore filter toggles, case-insensitive ignore matching, default file type selections, parent ignore files, global gitignore discovery and `core.excludesFile` parsing, BOM-stripped gitignore files, direct and path-or-parent upstream gitignore matching matrices, and per-directory `.gitignore`/`.git/info/exclude`/`.ignore`/`.rgignore`/custom ignore stacks with negation, repository gating, linked-worktree common-dir exclude resolution, source precedence, parallel visitor construction, skip/quit state surface, per-worker stack stealing, app-level explicit `--threads` directory file discovery, default multi-threaded `--files` directory listing with a single output stage, default multi-threaded non-JSON directory search with per-file output buffers and stats merging, and default multi-threaded JSON directory search with per-file output buffers and summary merging.
- Remaining surface: complete upstream walker/ignore test parity and any remaining upstream ordered-emission edge cases.
