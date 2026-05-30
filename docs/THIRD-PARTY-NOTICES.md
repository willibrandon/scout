# Third-Party Notices

Scout ports behavior from ripgrep and its pinned dependency graph. The
authoritative dependency inventory is `upstream/Cargo.lock`; every dependency
whose code or algorithm is ported must have its license reproduced here before
release.

Initial required notices:

- ripgrep: MIT OR Unlicense.
- regex-syntax: MIT OR Apache-2.0.
- regex-automata: MIT OR Apache-2.0.
- aho-corasick: MIT OR Unlicense.
- memchr: MIT OR Unlicense.
- bstr: MIT OR Apache-2.0.
- encoding_rs and encoding_rs_io: per upstream license text.
- walkdir, same-file, termcolor: MIT OR Unlicense.
- crossbeam components: MIT OR Apache-2.0.
- pcre2 and pcre2-sys Rust bindings: MIT OR Apache-2.0.
- PCRE2 C library: BSD license.

Full verbatim license texts are a release blocker and are added as each source
tree is vendored.
