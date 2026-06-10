# Contributing

Scout is a behavior port first. The useful standard for a change is simple:
keep the diff focused, preserve byte-level parity with the pinned ripgrep
revision, and make the evidence easy to review.

## Prerequisites

- [.NET SDK 10.0.102](https://dotnet.microsoft.com/download/dotnet/10.0),
  pinned by `global.json` with roll-forward disabled.
- Git and a terminal with ANSI support.
- A C toolchain for native binaries: `clang` on Unix, or a Visual Studio
  Developer Command Prompt on Windows.
- Bash on Windows when running differential/native release checks.

The full differential and performance suites also need the pinned ripgrep
oracle, decompression tools, and corpora recorded in `tests/PREREQS.lock`. CI
restores the captured hosted oracle archives; local setup uses
`eng/setup-ripgrep-oracle.*`, `eng/fetch-corpora.sh --all`, and
`eng/preflight.sh`.

## Repository Map

- `src/Scout.App/` - the `scout` command surface and search dispatch.
- `src/Scout.*` - ports of ripgrep crates and important dependencies.
  Each project has an `UPSTREAM.md` with its provenance.
- `tests/Scout.Foundation.Tests/` - component, analyzer, and configuration
  tests.
- `tests/Scout.Regex.Tests/` - regex corpus coverage.
- `tests/Scout.Differential.Tests/` - parity tests against pinned `rg`.
- `native/` - Native AOT entrypoints, PCRE2 builds, native linking, and native
  differential scripts.
- `eng/` - reproducibility, preflight, packaging, oracle, and generated-artifact
  scripts.
- `bench/` and `fuzz/` - performance gates and SharpFuzz targets.
- `docs/PARITY.md` and `docs/UPSTREAM-SYNC.md` - the behavioral ledger and the
  upstream-pin process.

## Local Checks

Start with the normal managed loop:

```sh
dotnet restore Scout.slnx
eng/check-msbuild-warning-gates.sh
dotnet build Scout.slnx --no-restore
dotnet format Scout.slnx --no-restore --verify-no-changes
```

For tests, run the narrowest suite that covers your change, then broaden before
review:

```sh
dotnet test tests/Scout.Foundation.Tests/Scout.Foundation.Tests.csproj --no-restore
dotnet test tests/Scout.Regex.Tests/Scout.Regex.Tests.csproj --no-restore
dotnet test tests/Scout.Differential.Tests/Scout.Differential.Tests.csproj --no-restore
```

The full solution test run is expected before release-grade changes, but it
requires the pinned oracle setup:

```sh
eng/setup-ripgrep-oracle.sh
eng/fetch-corpora.sh --all
eng/preflight.sh
dotnet test Scout.slnx --no-restore
```

On Windows, use `eng/setup-ripgrep-oracle.ps1` and install host prerequisites
with `eng/install-windows-host-prereqs.ps1` before differential work.

## Native Checks

If a change touches raw arguments, OS I/O, PCRE2, Native AOT, packaging, or
runtime identity, build the native executable for the host RID:

```sh
native/build-app-unix.sh osx-arm64 --smoke-only
```

Use the matching RID for your host. On Windows:

```powershell
native\build-app-windows.ps1 win-x64 -DifferentialMode SmokeOnly
```

Native differential modes are slower and require the pinned oracle. They are the
right check for changes that can affect byte output, PCRE2 behavior, invalid
UTF-8 handling, or generated help/man/completion artifacts.

## Code Standards

The repository enforces its standards in build and CI:

- Warnings are errors. Do not add `NoWarn`, `WarningsNotAsErrors`,
  `#pragma warning disable`, `SuppressMessage`, `GlobalSuppressions.cs`, or
  analyzer severities of `none`/`silent`.
- Nullable stays enabled. Fix nullability issues at the source.
- One type per file, including private helpers and generated output.
- File names match type names, and namespaces match folders.
- Public APIs need XML documentation because documentation files are generated.
- Tests are never skipped, ignored, explicit-only, or quarantined.
- Flag definitions keep their pinned upstream order with `[FlagOrder(...)]`.

Hot paths are byte-oriented and allocation-conscious. Prefer spans, explicit
byte writers, source generation, and small structs where that matches the
surrounding code. Avoid culture-sensitive formatting or string APIs on behavior
paths unless the upstream surface is explicitly text.

## Parity Expectations

Scout currently accepts no behavioral deviations from the pinned ripgrep
revision. The intentional differences are identity surfaces only: version
banners, command name, help/man/completion branding, Scout config names, and
Scout-specific diagnostics. They are listed in `docs/PARITY.md`.

For observable behavior changes:

1. Add or update focused component tests.
2. Add differential coverage when output, exit codes, ordering, errors, paths,
   encodings, regex behavior, ignore handling, or PCRE2 behavior can change.
3. Update generated artifacts only through the existing artifact pipeline, then
   run `eng/verify-generated-artifacts.sh` and `eng/verify-identity-rebrand.sh`.
4. Record any intentional byte-level deviation in `docs/PARITY.md` with a
   guarding test. A release should have no waived behavioral gaps.

If you advance an upstream pin, follow `docs/UPSTREAM-SYNC.md`. Do not mix an
upstream pin change with unrelated feature or cleanup work.

## CI And Release Gates

`CI` runs on Linux, macOS, and Windows for the supported RIDs. It covers restore,
MSBuild warning gates, build, portable tests, fuzz smoke targets, format, Native
AOT static-library publish, and native executable smoke checks. A successful
push to `main` dispatches `Release Gates` for that exact commit.

`Release Gates` are the release contract: pinned ripgrep oracle builds, frozen
corpora, preflight, full tests, native differentials, native link checks on all
six release RIDs, and the `hyperfine` performance gate. Tag-based releases then
publish standalone archives, Windows MSIs, the RID-aware .NET tool packages,
Homebrew, Scoop, and winget metadata.

Most PRs do not need a full local release-gate run. They do need enough local
evidence to show which part of the contract they affect.

## Pull Requests

1. Branch from `main`.
2. Keep the PR to one concern.
3. Include the local checks you ran and call out any release-gate area touched.
4. Update docs, project `UPSTREAM.md` files, `docs/THIRD-PARTY-NOTICES.md`, or
   generated artifacts only when the change actually affects them.
5. Leave broad refactors for their own PR unless they are necessary for the fix.

Contributions are under the [MIT license](LICENSE), same as Scout.
