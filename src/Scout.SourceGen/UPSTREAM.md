# Upstream

This project is Scout-owned infrastructure for the design-required source
generators and analyzers. It has no corresponding Rust crate, but it consumes
and pins generated behavior to the same ripgrep reference commit as the runtime
projects.

```text
name = "Scout.SourceGen"
commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
role = "source generators and repository policy analyzers"

name = "Microsoft.CodeAnalysis.CSharp"
version = "5.0.0"
```

The generated flag catalog is ordered from the pinned upstream flag definition
surface in `crates/core/flags/defs.rs`. The generated ported-test wrappers are
driven by the pinned upstream `tests/*.rs` `rgtest!` catalog. The generated
artifact payloads embed the checked-in help, man page, and shell completion
outputs captured from the pinned `rg --generate` and help commands.

The analyzers enforce Scout-specific policy from `docs/DESIGN.md`: one type per
file across hand-written and generated sources, namespace/folder structure, the
no-skip test rule, and pinned flag order metadata.
