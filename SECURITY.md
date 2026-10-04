# Security

Report a vulnerability privately through [GitHub Security Advisories](https://github.com/willibrandon/scout/security/advisories/new)
for the `willibrandon/scout` repository. Please do not open a public issue for a vulnerability.
Fixes go into the latest release.

Scout searches files with your permissions. When requested, it runs a preprocessor through
`--pre` or external decompression tools through `--search-zip`. Reports are welcome when
Scout accesses files or executes commands beyond what the invocation requests, or when
processing a file or pattern causes memory corruption or another security vulnerability.
