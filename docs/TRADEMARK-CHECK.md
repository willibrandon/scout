# Trademark Check

Date: 2026-06-01

Scope: the project name `Scout`, the executable name `scout`, package IDs
`Scout.*`, and public positioning as `Scout (ripgrep port)`.

Gate status: done for the design-required engineering release gate. This is a
search-and-collision record, not a legal opinion, legal clearance, or a
trademark registration decision.

## Search Inputs

- USPTO Trademark Search: https://tmsearch.uspto.gov/
- Docker Scout product and CLI documentation:
  https://docs.docker.com/scout/ and
  https://docs.docker.com/reference/cli/docker/scout/
- Docker trademark guidelines:
  https://www.docker.com/ja-jp/legal/trademark-guidelines/
- Scout Monitoring / Scout APM:
  https://www.scoutapm.com/ and https://scoutapm.com/docs
- openSUSE Scout:
  https://en.opensuse.org/Scout
- Rust crate `scout`:
  https://docs.rs/crate/scout/1.0.0
- npm package `scout`:
  https://www.skypack.dev/view/scout

## Findings

The name is not unique. Current public search found software and CLI uses that
overlap the developer tooling space:

- Docker Scout is a Docker product and `docker scout` CLI for container and
  software-supply-chain analysis. Scout must not use Docker branding, imply a
  Docker relationship, or position itself as a container security scanner.
- Scout Monitoring, formerly Scout APM, is application monitoring software and
  hosted observability. Scout must not position itself as APM, tracing, logging,
  alerting, or application monitoring software.
- openSUSE Scout is a command-line package search / command-not-found utility.
  This is a direct `scout` command collision on openSUSE-family systems and must
  be handled by distribution packaging policy if Scout is packaged there.
- The crates.io `scout` crate is a Rust command-line fuzzy finder. This is a
  developer CLI collision but not a recursive grep CLI.
- The npm `scout` package is a JavaScript package for searching npm modules.

No searched source showed an exact conflict for a recursive grep-compatible CLI
branded as `Scout (ripgrep port)`, but the name remains collision-prone.

## Release Rules

- Public copy must use `Scout (ripgrep port)` where context is not already
  obvious.
- Do not claim the name is unique.
- Do not ship an `sc` alias.
- Do not use Docker, Scout APM, or openSUSE branding or logos.
- Re-run this check before any public v1.0 announcement or package-manager
  submission.
- If a hard conflict appears, execute the mechanical rename path described in
  `docs/DESIGN.md`.
