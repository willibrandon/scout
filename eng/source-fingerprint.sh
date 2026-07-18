#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"

temporary_index="$(mktemp "${TMPDIR:-/tmp}/scout-source-index.XXXXXX")"
rm -f "$temporary_index"
cleanup() {
    rm -f "$temporary_index"
}
trap cleanup EXIT HUP INT TERM

GIT_INDEX_FILE="$temporary_index" git -c safe.directory="$ROOT" -C "$ROOT" read-tree --empty
GIT_INDEX_FILE="$temporary_index" git -c safe.directory="$ROOT" -c core.safecrlf=false -C "$ROOT" add -A -- \
    Directory.Build.props \
    Directory.Build.rsp \
    Directory.Build.targets \
    Directory.Packages.props \
    Scout.slnx \
    global.json \
    native \
    src \
    tests/PREREQS.lock
GIT_INDEX_FILE="$temporary_index" git -c safe.directory="$ROOT" -C "$ROOT" write-tree
