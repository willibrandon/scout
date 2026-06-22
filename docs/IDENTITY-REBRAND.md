# Scout Identity Rebrand — Implementation Contract (release prep)

**Status:** Contract — decisions D1–D5 **LOCKED** (no code changed). **Date:** 2026-06-04. **Rev:** 3 (review round 2: decisions locked + debug-category naming).
**Goal:** Change Scout's user-facing *identity* from ripgrep → scout for release, **without** weakening behavioral parity verification and **without** severing the machinery that ports future ripgrep upstream updates.

File:line references were checked against the working tree; the reproducible audit commands are in **Appendix A** (run them to re-confirm). Where a claim is "verified live," Appendix A has the exact command + observed output.

---

## 0. The core problem (read first)

Scout currently runs in a **strict byte-identical-to-ripgrep regime** (`docs/PARITY.md:3`: *"no accepted runtime deviations"*), enforced by **six** gates:

1. **Differential suite** — `DifferentialRunner.cs` runs Scout + spawns pinned `rg`, byte-compares exit code + normalized stdout/stderr. `DifferentialOutputNormalizer.cs` only masks elapsed-time / sorts; it does **not** strip identity.
2. **Pinned-output unit tests** — `ScoutApplicationTests.cs` / `ScoutApplicationRuntimeTests.cs` assert Scout output `==` pinned `rg` (version, help, errors, `--debug`/`--trace`).
3. **Managed generated-artifact verify** — `eng/verify-generated-artifacts.sh` (regen-and-diff the `.base64` blobs).
4. **Native generated-artifact differential** — `native/test-generated-artifacts-unix.sh` (`:123` diffs `rg` stdout vs scout stdout directly; `:145+` `compare_case help_long --help …`), called from the native build. **(Separate gate from #3 — both must move.)**
5. **Policy tests pinning the wiring** — `PinnedConfigurationTests.cs:1440` `NativeGeneratedArtifactDifferentialsAreWired` and `:1596` (`compare_artifact help_short help-short.base64 -h`, etc.).
6. **Native build smoke** + **release packaging stamp** — `native/build-app-unix.sh:151`, `build-app-windows.ps1:127` assert the version banner; `eng/package-release.sh:64` / `package-release.ps1:44` stamp `parity = "no accepted runtime deviations"`.

**The rebrand is a deliberate, surgical carve-out from this regime for *identity* surfaces only.** Behavior stays byte-identical to `rg`; identity becomes Scout's. Every gate above must be taught the difference. A naive `s/ripgrep/scout/` is wrong — it corrupts porting machinery, the parity contract, and legitimate behavioral data simultaneously.

### The three tiers

- **TIER-1 BEHAVIORAL PARITY** — must stay byte-identical to `rg`: **all stdout search output**, exit codes, error *message text*, debug *message bodies*, and behavioral data that merely *contains* tokens like `.rs` (file-type globs, `--type-list`, fixtures). **Do not change.**
- **TIER-2 IDENTITY** — surfaces that present Scout *as a program*: the version banner, the `rg:` stderr program prefix, `--debug`/`--trace` structural fields (program prefix + module/category + **debug source-location** `crates/.../*.rs:<line>`), help/man/completion *program identity*, homepage URLs, author credit placement. → become `scout`.
- **TIER-3 PROVENANCE / LEGAL** — vendored `upstream/`, `UPSTREAM.md`, source provenance comments, license attribution. **Must stay** ripgrep.

> **`.rs` is NOT identity in general (review fix C2).** Only **debug source-location strings** `crates/.../*.rs:<line>` emitted by the logger are an identity leak. `.rs` as a *file type* (`DefaultFileTypes.cs:427` `("rust","*.rs")`), in `--type-list` output (verified live: `rust: *.rs`), in globs, fixtures, or vendored upstream paths is **TIER-1 behavior / TIER-3 provenance — do not rebrand.**

---

## 1. IDENTITY SURFACES TO CHANGE (TIER-2)

### 1.1 Version — must be release-grade .NET version identity (review fix H3)

Today the version is a hardcoded literal `"ripgrep 15.1.0 (rev 4857d6fa67)"` in **two** places — `src/Scout.App/VersionOutput.cs:9,14` (managed) and `native/entry/scout_main.c:27` (native launcher) — and `Directory.Build.props` sets **only** `AssemblyName` (`:49`); there is **no** `<Version>`, `<FileVersion>`, `<AssemblyInformationalVersion>`, `<Product>`, `<Company>`, `<RepositoryUrl>`, or package manifest.

**A single `SCOUT_VERSION` constant is insufficient.** .NET release identity requires all of these to **agree**, centralized in `Directory.Build.props` and flowed to the native launcher via a `-D` compile define:

| Property | Value (example) |
|---|---|
| `Version` / `VersionPrefix` | `0.2.1` (Scout's own SemVer, decoupled from ripgrep 15.1.0) |
| `AssemblyVersion` / `FileVersion` | derived from `Version` |
| `AssemblyInformationalVersion` | `0.2.1+ripgrep.15.1.0.4857d6fa67` (SemVer build metadata carries the upstream coordinate) |
| `Product` | `Scout` |
| `Company` / `Authors` | project owner |
| `Description` | `Scout — a feature-complete port of ripgrep to .NET Native AOT` |
| `RepositoryUrl` / `PackageProjectUrl` | Scout's repo |
| `PackageId` (if packaged) | `Scout.*` per §1 of DESIGN |

**`--version` banner** (managed `VersionOutput.cs:9,14` + native `scout_main.c:27`, kept in lockstep) — derive from the informational version:
```
scout 0.2.1 (ripgrep 15.1.0 compatible, rev 4857d6fa67)
```
The `simd(compile):…` / `features:…` body (`VersionOutput.cs:21-52`) is format scaffolding `rg` also emits — keep the **format**; only the leading identity token changes. The upstream `rev` is sourced from the pin table (provenance), not Scout's own version.

### 1.2 Help / man / completions — command-aware transform, NOT string replacement (review fix H2)

**Architecture:** `--help`/`-h`, `--generate man`, `--generate complete-{bash,zsh,fish,powershell}` are served verbatim from 7 gzip+Base64 blobs in `src/Scout.App/GeneratedArtifacts/*.base64` (decoded `HelpOutput.cs:14`, embedded via `Scout.SourceGen/GeneratedArtifactSourceGenerator.cs`). They are captured from pinned `rg` and gate-checked equal to it (gates #3, #4, #5).

**Strategy — a deterministic, *command-/token-aware* transform `rg-artifact → scout-artifact` (not naive `s/rg/scout/`).** Per-artifact rules:

- **help-short / help-long (prose):** token-aware replacement only. Replace the tool name `ripgrep`→`scout` and the **program token** `rg`→`scout` *as a command* (USAGE lines, example invocations). **Do not** touch: regex examples, the `.rgignore`/`RIPGREP_CONFIG_PATH` identifiers unless D1/D2 say so, flag names, or substrings (`rg` inside other words). Replace homepage URL; relocate the author line (§3).
- **man.base64:** same prose rules + structural: `.TH RG 1 …`→`.TH SCOUT 1 …`, `\fBrg\fP`→`\fBscout\fP`, HOMEPAGE/issue URLs → Scout's. **Preserve** the OSC-8 gist URL (third-party reference) and any license block verbatim.
- **complete-*.base64 (command-aware):** rename function defs (`_rg`→`_scout`, `__rg_*`→`__scout_*`), registrations (`complete -F _rg … rg`, `#compdef rg`, `compdef … rg`), descriptive comments, and any aliases — *consistently*, so the completion **registers only `scout`, not `rg`** (D5 LOCKED) and calls its own functions. **Preserve** the embedded **zsh-users copyright block** verbatim (TIER-3).

**Additive content for D1/D2 (not pure substitution):** the transform must also **inject new prose** that ripgrep's help has no source for — document `SCOUT_CONFIG_PATH` first with `RIPGREP_CONFIG_PATH` as compatibility (D1), and `.scoutignore` alongside `.rgignore` (D2). Treat these as deterministic, reviewable transform *additions* keyed to the relevant sections, so they re-apply cleanly on upstream sync.

**Gate rewiring (all four, not one — review fix H1):** `eng/verify-generated-artifacts.sh` **and** `native/test-generated-artifacts-unix.sh` change from "scout-artifact == rg-output" to "scout-artifact == `transform`(rg-output)"; update the policy asserts at `PinnedConfigurationTests.cs:1440` and `:1596`. A future upstream sync re-runs `transform` on the new `rg` → Scout's artifacts regenerate automatically (**porting preserved**).

### 1.3 Error / diagnostic prefix — two helper APIs, not one constant (review fix H5)

The `rg:` stderr prefix (verified live) comes from `ScoutError.WithContext(...)` (`src/Scout.Errors/ScoutError.cs:66-69`, chain joined by `": "`) in **two** forms that need **two** distinct helpers to avoid `scout: scout: path` / colon drift:

- **Form A** `WithContext("rg")` → `scout: <msg>`. Provide `Diagnostics.ProgramContext()` → contributes the program token once. Sites: `ScoutApplication.cs` (85,111,186,200,246,252,277), `ConfigArgumentExpander.cs` (123,129,135,141,147), `PatternPreparation.cs` (752,773), `Pcre2SearchOperations.cs` (27,33,45,88,140), `SearchWalkPlanning.cs` (15,63,187), `SearchPathArgument.cs` (51,72,81), `Scout.Cli/PatternFileLoader.cs` (22,27,32,37,105,197).
- **Form B** `WithContext($"rg: {path}")` → `scout: <path>: <msg>`. Provide `Diagnostics.ProgramPathContext(path)` that formats program + path consistently (one colon-join policy, raw-byte path safe). Sites: `SearchApplicationDiagnostics.cs:21`, `LargeFileSearchOperations.cs:93,98`, `Pcre2SearchOperations.cs:3617`, `SearchFileContentReader.cs:41,47,70,76,82,115,133`.

Back both helpers with a single `ProgramName = "scout"` constant. Net: program identity is defined once; the two emission shapes are explicit, not ad-hoc string interpolation.

### 1.4 `--debug` / `--trace` — deterministic, stable Scout source locations (review fix H4)

Verified live, format `rg: {LEVEL}|{target}|{file}:{line}: {message}` (`src/Scout.Diagnostics/DiagnosticLogger.cs`):
```
rg: DEBUG|rg::flags::config|crates/core/flags/config.rs:19: RIPGREP_CONFIG_PATH … not set …
rg: DEBUG|grep_regex::config|/Users/brandon/src/ripgrep/crates/regex/src/config.rs:175: assembling HIR …
```
Identity fields → Scout, **message bodies stay** (TIER-1):
- **Prefix** `rg:`→`scout:` (via §1.3 helper).
- **Category** (`target`): Rust module paths → **stable .NET-style Scout category names** (LOCKED — not lowercase examples). Explicit, fixed vocabulary (do **not** derive from runtime type names that can drift):

  | Upstream `target` | Scout category |
  |---|---|
  | `rg::flags::hiargs`, `rg::flags::parse`, `rg::flags::config` | `Scout.App.Flags` |
  | `rg::search` | `Scout.Searching` |
  | `grep_regex::config`, `grep_regex::matcher`, `grep_regex::literal` | `Scout.Regex` |
  | `grep_searcher::searcher`, `grep_searcher::searcher::core` | `Scout.Searching` |
  | `ignore::gitignore` | `Scout.Ignore` |
  | `globset` | `Scout.Globbing` |
- **Source location** `crates/.../*.rs:<rust-line>` (21 sites: `SearchDiagnosticLogging.cs` ×17, `ConfigArgumentExpander.cs` ×4) → **stable repo-relative Scout paths** like `src/Scout.App/SearchDiagnosticLogging.cs:NN`.
  - **Determinism requirement:** `[CallerFilePath]` embeds the **absolute build-machine path** unless normalized. `Directory.Build.props` already sets `ContinuousIntegrationBuild=true` (`:13`) and `Deterministic=true` (`:14`) but **not** `DeterministicSourcePaths`/`PathMap`. **Recommended design:** keep the *explicit (category, repo-relative-file, line) triple* at each call site (mirroring the current hardcoded style) populated with Scout values, so output is stable and golden-testable; keep the upstream `crates/.../*.rs:line` as a `// provenance:` comment. (If `[CallerFilePath]` is preferred, it MUST be paired with `PathMap`/`DeterministicSourcePaths` mapping the repo root to a stable prefix — otherwise builds leak absolute paths and golden tests differ per machine.)
- **Remove** the leaked personal path: `SearchDiagnosticLogging.cs:9` `DefaultRipgrepSourceRoot = "/Users/brandon/src/ripgrep"` and the `RipgrepSourcePath(...)`/`SCOUT_RIPGREP_SOURCE_ROOT` **debug-path** plumbing (`:175-187`). *(The `SCOUT_RIPGREP_SOURCE_ROOT`/`SCOUT_RIPGREP_REFERENCE` **oracle/reference** use elsewhere stays — see §4 tripwire 7.)*

### 1.5 URLs, author, and `--pcre2-version` split (review fix M1)
- Homepage `github.com/BurntSushi/ripgrep` (help/man) → **Scout's repo** in Scout's own help; the OSC-8 gist URL stays (third-party reference).
- Author `Andrew Gallant <jamslam@gmail.com>` → relocate to a credit line (see §3 wording fix).
- **`--pcre2-version` is split, not pure identity:** the *available* output `PCRE2 10.46 is available (JIT is available)` (`Pcre2Library.cs:53-67`; verified live) is **build-capability behavior — keep**. Only the *unavailable* text `"…not available in this build of ripgrep"` (`Pcre2Library.cs:14,29,35`) is product identity → decision D4. (The native side has **no** PCRE2 "ripgrep" string; `scout_main.c:92` is `"PCRE2 unknown is available (JIT is unavailable)"` — review fix M6.)

### 1.6 Scout-owned flag descriptor text (review fix M3)
Even though current `--help` is served from blobs (so these aren't rendered today), these descriptor strings are **Scout-owned source** and must not speak in ripgrep's product voice: `Flags/Definitions/NoConfigFlag.cs:13` ("Disable **ripgrep** configuration expansion."), `PrettyFlag.cs:13` ("Apply **ripgrep's** pretty output alias."), `IgnoreDotFlag.cs:13` (".ignore and .rgignore" — follows D2). Rewrite to Scout voice.

### 1.7 Internal generated-doc wording (review fix M4)
`src/Scout.SourceGen/GeneratedArtifactSourceGenerator.cs:108` emits `/// Provides a source-generated compressed ripgrep artifact payload.` Decide: "upstream artifact" (provenance-accurate) vs "Scout artifact" (product), then **gate it** so it can't drift.

---

## 2. BEHAVIORAL/COMPAT DECISIONS — **LOCKED**

| # | Surface | File:line | **Locked decision + spec** |
|---|---|---|---|
| **D1** | Config env var | `ConfigArgumentExpander.cs:31` (sole read) + strings `:34,123,129,135,141,147` | **`SCOUT_CONFIG_PATH` is PRIMARY; `RIPGREP_CONFIG_PATH` is a compatibility FALLBACK.** Semantics: (a) if `SCOUT_CONFIG_PATH` is set (non-empty), use it **exclusively** — and on a read error, **fail; do NOT fall back** to `RIPGREP_CONFIG_PATH`; (b) only when `SCOUT_CONFIG_PATH` is unset/empty, consult `RIPGREP_CONFIG_PATH`; (c) `--no-config` disables **both**; (d) the unset-debug line + read-error text name the **actual var consulted**; (e) help/man document `SCOUT_CONFIG_PATH` **first**, mention `RIPGREP_CONFIG_PATH` as compatibility; (f) Unix **raw-byte** `OsString` (no UTF-8 round-trip); (g) test `configPathOverride` path preserved; (h) differential harness (`DifferentialRunner.cs:123-126`) controls **both** vars for isolation; (i) update analyzer `PinnedConfigurationTests.cs:763` (currently forbids `SCOUT_CONFIG_PATH`) + `DESIGN.md:266`. |
| **D2** | Ignore filename | `WalkBuilder.cs:345` (sole site) | **Honor both, `.rgignore` then `.scoutignore` so Scout-native rules WIN on conflict.** Append to `ignoreFileNames` in order `… , .rgignore, .scoutignore` (later name = higher precedence in gitignore semantics → `.scoutignore` overrides `.rgignore`). Both gated by the existing dot-ignore control: **`--no-ignore-dot` disables `.ignore`, `.rgignore`, AND `.scoutignore`**. Also covered by `--no-ignore`, parent traversal, and explicit `--ignore-file`; differential temp-dir isolation so fixtures don't carry the other name. `.gitignore`/`.ignore` unchanged. |
| **D3** | Missing-pattern error text | `ScoutApplication.cs:200` | → **`"scout requires at least one pattern to execute a search"`**. Update assert `ScoutApplicationTests.cs:157`. |
| **D4** | PCRE2 unavailable text | `Pcre2Library.cs:14,29,35` | → **`"…not available in this build of scout"`**. Keep the **available** `--pcre2-version` output unchanged (§1.5). Update `Pcre2LibraryTests.cs:17-20` + native smoke. |
| **D5** | Completions | `complete-*.base64` | **Register only `scout`, not `rg`** — part of the §1.2 command-aware transform. |

---

## 3. MUST NOT CHANGE — legal / attribution (TIER-3)

- **`docs/THIRD-PARTY-NOTICES.md` entirely.** Highest priority: **PCRE2 BSD notice (lines 617-655)** — the **sole** copy of PCRE2's required notice (no `LICENCE` vendored under `native/pcre2/`). Removing it = license violation in the shipped binary. Plus MIT (Andrew Gallant ×4, Rust Project, Mozilla, Crossbeam, lexopt, textwrap, anyhow), Apache-2.0, Unlicense, WHATWG.
- **zsh-users copyright** embedded in `complete-zsh.base64` — preserve through the transform.
- **Author credit** — relocate, never delete. **Wording (review fix M5):** avoid "ported from ripgrep by Andrew Gallant" (reads as if Andrew ported Scout). Use: **"Scout ports ripgrep, originally authored by Andrew Gallant. See THIRD-PARTY-NOTICES."**
- `upstream/.../LICENSE-UNICODE`, `native/pcre2/UPSTREAM`.

---

## 4. PRESERVE — porting machinery (TIER-3) + tripwires

Anchor: pinned commit `4857d6fa67db69a95cd4b6f2adda5d807d4d0119`. Keep: `upstream/` (Cargo.lock, `ripgrep-4857d6fa/tests/**`, `regex-syntax-0.8.8/unicode_tables/**`, `regex-1.12.2/testdata/**`, `encoding_rs-0.8.35/**`, `ucd/UCD-16.0.0.zip`, `UNICODE-VERSION`); 20 `src/*/UPSTREAM.md`; the pin (`PinnedConfigurationTests.cs:16`, `UPSTREAM-SYNC.md`, `PREREQS.lock`); oracle harness (`PinnedRipgrepOracle.cs`, `DifferentialRunner.cs`, `eng/setup|capture|restore-ripgrep-oracle.*`, `oracle-capture.yml`); ported tests (`PortedRgTests.catalog`, `PortedRgTestSourceGenerator.cs`, `RegexCorpusLoader.cs:8`); preflight HEAD-equals-pin (`eng/preflight.sh:493-505`).

**Tripwires (naive find/replace breaks porting):**
1. Oracle binary name is `rg` (`capture-ripgrep-oracle.sh` builds `--bin rg`; restore and tests keep `*/target/*/rg` lookups).
2. `crates/.../*.rs` in `--debug` are a **live parity contract** today (byte-compared), not comments — they move only via the §1.4 rewrite that also moves gate #2.
3. Log targets `rg::`/`grep_regex::`/`grep_searcher::` — same as #2.
4. `RIPGREP_CONFIG_PATH` is the parity contract; `SCOUT_CONFIG_PATH` is currently **forbidden** by `PinnedConfigurationTests.cs:763` + `DESIGN.md:266` (D1 updates both).
5. `upstream/regex-1.12.2/testdata` path string (`regex-1.12.2` = upstream coordinate).
6. Vendored dir names `ripgrep-4857d6fa/`, `regex-syntax-0.8.8/`, etc. (version/commit coordinates).
7. `SCOUT_RIPGREP_SOURCE_ROOT`/`SCOUT_RIPGREP_REFERENCE` keep the `RIPGREP` token (they locate the reference checkout/oracle). The **debug-path** use goes away (§1.4); the **oracle/reference** use stays.
8. `UPSTREAM.md` crate names + checksums asserted verbatim (`name = "ripgrep"` stays).
9. `THIRD-PARTY-NOTICES.md` / `UPSTREAM-SYNC.md` inventory rows assert "ripgrep" as provenance.

---

## 5. Differential strategy — narrow, structured normalization (review fix C1)

**Stdout search bytes are NEVER normalized — they stay byte-exact.** If a searched file contains `rg`, `ripgrep`, `.rs`, or a Scout URL, those bytes must compare exactly, or real result diffs get hidden. Normalization is restricted to **(a) structured stderr identity fields** and **(b) dedicated identity surfaces**, never free-form stdout/stderr text.

**(A) Structured-field normalization in the differential.** Extend `DifferentialOutputNormalizer` to canonicalize *only* known-position identity fields on both outputs before compare:
- the **leading program prefix** of an stderr error line (`scout:`/`rg:` at line start only — not occurrences mid-message);
- in `--debug`/`--trace` lines, the **prefix + category + source-location fields** (positions 1–3 of the `|`-delimited structure) → placeholders, comparing **message bodies** only.
This proves behavioral error/debug equivalence while being blind to identity. It must be field-structured (parse the line shape), **not** a global token replace.

**(B) Scout-golden tests for pure-identity surfaces** (decoupled from `rg`): `--version`, `--help`/`-h`, `--pcre2-version` (unavailable case), man, completions, and `--debug`/`--trace` *line shape* assert against checked-in Scout-expected output **and** positively assert the output says "scout", not "ripgrep".

Tests moving parity→golden: `ScoutApplicationTests.cs` (version 17-57, help 63-102, error prefixes 135,157,181,206,233), `PatternFileLoaderTests.cs:46,65`, `ScoutApplicationRuntimeTests.cs:368-398` (debug/trace; drop `SCOUT_RIPGREP_SOURCE_ROOT` wiring 1580-1590), `Pcre2LibraryTests.cs:17-20` (D4). `CliParserTests.cs` (message text, no prefix) is **unaffected**.

### 5.1 New gate — automated rebrand audit (review fix M7)
Add a CI gate that **decodes the generated artifacts** and scans **runtime + native + package** surfaces for forbidden `rg`/`ripgrep` identity tokens, with explicit **allowlists** for: TIER-3 provenance (`upstream/`, `UPSTREAM.md`, provenance comments), `THIRD-PARTY-NOTICES.md`, oracle tooling (`SCOUT_RIPGREP_*`, `--bin rg`), vendored fixtures, and behavioral strings (`.rs` file types, `RIPGREP_CONFIG_PATH`/`.rgignore` if kept). This is the durable guard that the carve-out stays correct as code evolves.

---

## 6. DESIGN.md / PARITY.md / packaging edits — contract-first (review fix L1, M2)

These define which deviations are legitimate, so they land **first / same-commit**, not last:
- **`DESIGN.md` §1.1 (line 56)** — replace the understated "Name appears only in namespace/binary/package/help" with the full surface (version banner, `scout:` prefix, debug format incl. source-locations, URLs, author relocation, D1/D2) + pointer here.
- **`DESIGN.md` §0 line 28 / §4.10 line 257** — version is Scout-owned (`scout <ver> (ripgrep … compatible)`); help/man/completions are the **deterministic transform** of upstream artifacts.
- **`DESIGN.md` §4.10.1 / line 266** — revise the "`SCOUT_CONFIG_PATH` removed from v1" paragraph per **D1** (this is the post-parity step it anticipated).
- **New `DESIGN.md` §4.10.2 "Identity vs Parity vs Provenance"** — codify the three-tier model + the rule: behavioral output stays byte-identical (identity-normalized differential); identity surfaces are Scout's (golden tests + audit gate); provenance/attribution preserved.
- **`docs/PARITY.md:3`** — change "no accepted runtime deviations" to "no accepted **behavioral** deviations; identity surfaces are intentionally Scout-specific, enumerated and gated here," and list them as documented deviations.
- **`eng/package-release.sh:64` / `package-release.ps1:44`** — the `parity = "no accepted runtime deviations"` stamp must be reconciled with the carve-out (e.g. `parity = "behavioral parity; identity is Scout-specific (see PARITY.md)"`).

---

## 7. Implementation sequence

1. **Contract docs first** (§6) — DESIGN.md/PARITY.md define legitimacy; land same-commit as code.
2. **Decisions D1–D5** with full specs (§2).
3. **Version identity** (§1.1) — full props in `Directory.Build.props` + native `-D`.
4. **Centralize program identity** (§1.3) — `ProgramName` + two context helpers; route all sites.
5. **Debug/trace rewrite** (§1.4) — stable Scout categories + repo-relative paths; drop leaked path; provenance comments.
6. **Command-aware artifact transform** (§1.2) — regenerate 7 blobs; rewire **both** artifact gates (#3,#4) + policy asserts.
7. **Differential narrowing + Scout-golden tests** (§5); flag-descriptor + source-gen-doc fixes (§1.6, §1.7).
8. **Rebrand audit gate** (§5.1).
9. **Full release gate** on all 6 RIDs — confirm behavior still byte-identical (narrow-normalized) and identity is Scout's.

## 7.1 Branch & completion process (LOCKED)

This change **intentionally breaks the old byte-parity assumptions** while tests and gates are being moved, so it is quarantined:

- **All implementation (steps 1–8) happens on a dedicated branch** (`identity-rebrand`), never on `main`. `main` must stay shippable while the carve-out is in flight.
- **Completion criterion:** the rebrand goal is considered complete only after **5 consecutive green full Release Gates runs on the same commit** (no code changes between them) on the branch HEAD — the same durability bar used for the main project, proving the moved gates are stable, not a lucky pass.
- **Merge to `main` only after** the branch has the full release gate green **and** the 5-consecutive-green-same-commit bar is met. No commit to `main` before then.
- Intermediate branch states will be red (expected — parity tests are being migrated to golden/identity-normalized form); that is fine on the branch and is exactly why it is isolated.

**Net:** porting machinery intact (transform re-derives on sync; provenance comments + `upstream/` + oracle untouched), behavior byte-identical to `rg`, identity is Scout's, no leaked personal path, an audit gate keeps it that way, and `main` only receives the change once it is durably green on the branch.

---

## Appendix A — reproducible audit commands

```sh
S=artifacts/bin/osx-arm64/scout            # HEAD-accurate build via native/build-app-unix.sh osx-arm64
# Version (identity banner; rev present because Scout matches the pinned post-tag commit):
"$S" -V                                     # → ripgrep 15.1.0 (rev 4857d6fa67)
# Error program prefix:
"$S" --bogus 2>&1                           # → rg: unrecognized flag --bogus
# Debug source-locations (the .rs leak) + leaked absolute path:
"$S" --debug foo /tmp/x 2>&1 | grep -E 'crates/|/Users/'
# .rs as BEHAVIOR (must NOT rebrand):
"$S" --type-list | grep '^rust:'            # → rust: *.rs
# Surface inventories (source of the file:line tables):
grep -rn 'WithContext("rg")\|WithContext($"rg: ' src --include=*.cs
grep -rn 'ripgrep' src/Scout.App/VersionOutput.cs native/entry/scout_main.c src/Scout.Pcre2/Pcre2Library.cs
sed -n '1,160p' native/test-generated-artifacts-unix.sh   # second artifact gate
grep -n 'no accepted runtime deviations' eng/package-release.*
```
