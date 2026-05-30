#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
EXPECTED_SDK="10.0.102"
EXPECTED_RIPGREP="4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
REFERENCE="/Users/brandon/src/ripgrep"

ACTUAL_SDK="$(dotnet --version)"
if [ "$ACTUAL_SDK" != "$EXPECTED_SDK" ]; then
    printf 'Expected .NET SDK %s, got %s\n' "$EXPECTED_SDK" "$ACTUAL_SDK" >&2
    exit 1
fi

ACTUAL_RIPGREP="$(git -C "$REFERENCE" rev-parse HEAD)"
if [ "$ACTUAL_RIPGREP" != "$EXPECTED_RIPGREP" ]; then
    printf 'Expected ripgrep %s, got %s\n' "$EXPECTED_RIPGREP" "$ACTUAL_RIPGREP" >&2
    exit 1
fi

cmp "$REFERENCE/Cargo.lock" "$ROOT/upstream/Cargo.lock"
printf 'Scout preflight passed.\n'
