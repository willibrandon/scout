# Upstream

Ports the traversal surface of ripgrep's `ignore` workspace crate.

```text
name = "ignore"
version = "0.4.29"
commit = "e89fff89ac9af12e8d4ce9d5fd07beb408ca730f"
path = "crates/ignore"

name = "walkdir"
version = "2.5.0"
checksum = "29790946404f91d9c5d06f9874efddea1dc06c5efe94541a7d6863108e3a5e4b"

name = "same-file"
version = "1.0.6"
checksum = "93fc1dc3aaa9bfed95e02e6eadabb4baf7e3078b0bd1b4d7b6b0b68378900502"

name = "crossbeam-deque"
version = "0.8.7"
checksum = "5181e0de7b61eb03a81e347d6dd8797bae9da5146707b51077e2d71a54ec0ceb"
```

- Covered surface: `walk::WalkBuilder`, `walk::Walk`, `walk::WalkParallel`, `walk::WalkState`, `walk::DirEntry`, hidden filtering, depth limits, max file size, symlink traversal, same-file loop protection, same-file-system boundaries, explicit overrides, explicit ignore files, standard ignore filter toggles, case-insensitive ignore matching, default file type selections, parent ignore files, global gitignore discovery and `core.excludesFile` parsing, BOM-stripped gitignore files, direct and path-or-parent upstream gitignore matching matrices, path-or-parent root validation, rooted patterns matching parent-like names such as `..foo`, and per-directory `.gitignore`/`.git/info/exclude`/`.ignore`/`.rgignore`/custom ignore stacks with negation, repository gating, linked-worktree common-dir exclude resolution, source precedence, parallel visitor construction, skip/quit state surface, per-worker stack stealing, app-level explicit `--threads` directory file discovery, default multi-threaded `--files` directory listing with a single output stage, default multi-threaded non-JSON directory search with per-file output buffers and stats merging, and default multi-threaded JSON directory search with per-file output buffers and summary merging.
- Directory-entry-based ignore-file discovery also ports ripgrep commit `68b3c9e0d95950cfc835fb07924457919a4ec07d`, which landed after the pinned baseline and avoids probing absent ignore paths.
- Linux and macOS traversal use one raw directory snapshot that retains native entry types, projects valid UTF-8 names to text paths, and preserves malformed names as byte paths.
- This file records source provenance and covered surfaces only. Scope is not parked here; any uncovered upstream case must be represented by an implementation change, a conformance test, or a zero-entry release ledger in `docs/PARITY.md`.
