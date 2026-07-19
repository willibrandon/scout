# Upstream

This project replaces platform crates from the pinned ripgrep lockfile with
Scout-owned raw OS boundaries.

```text
name = "libc"
version = "0.2.177"
checksum = "2874a2af47a2325c2001a6e6fad9b16a53b802102b528163885171cf92b15976"

name = "windows-sys"
version = "0.61.2"
checksum = "ae137229bcbd6cdf0f7b80a31df61766145077ddf49416a728b02cb3921ff3fc"

name = "winapi-util"
version = "0.1.11"
checksum = "c2a7b1c03c876122aa43f3020e6c3c3ee5c05081c9a00739faf7503aeba10d22"

commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
```

The implementation owns Unix raw byte paths, raw `getcwd`, directory iteration,
raw standard streams, Windows UTF-16 boundaries, and direct `LibraryImport`
declarations for the exact OS APIs Scout uses.

Windows raw standard-input reads follow the .NET runtime console-stream boundary:
`ERROR_BROKEN_PIPE` and `ERROR_NO_DATA` report pipe EOF after the writer closes.
Broken downstream output pipes retain their independent graceful-exit handling.
