#!/usr/bin/env bash
set -Eeuo pipefail

if [ "$#" -ne 1 ]; then
    printf 'usage: %s <osx-arm64|osx-x64|linux-x64|linux-arm64>\n' "$0" >&2
    exit 2
fi

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
BIN="$ROOT/artifacts/bin/$RID"
PACKAGE_ROOT="$ROOT/artifacts/packages"
STAGE_PARENT="$PACKAGE_ROOT/stage"
STAGE="$STAGE_PARENT/scout-$RID"
ARCHIVE="$PACKAGE_ROOT/scout-$RID.tar.gz"
HASH_FILE="$ARCHIVE.sha256"

case "$RID" in
    osx-arm64|osx-x64|linux-x64|linux-arm64)
        ;;
    *)
        printf 'RID %s is not supported by this Unix package script.\n' "$RID" >&2
        exit 1
        ;;
esac

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r "$1" | awk '{ print $1 }'
    else
        printf 'No SHA-256 tool found.\n' >&2
        exit 1
    fi
}

require_file() {
    if [ ! -f "$1" ]; then
        printf 'Missing required package input: %s\n' "$1" >&2
        exit 1
    fi
}

require_file "$BIN/scout"
require_file "$ROOT/docs/PARITY.md"
require_file "$ROOT/docs/THIRD-PARTY-NOTICES.md"

rm -rf "$STAGE"
mkdir -p "$STAGE" "$PACKAGE_ROOT"
cp "$BIN/scout" "$STAGE/scout"
chmod 755 "$STAGE/scout"
cp "$ROOT/docs/PARITY.md" "$STAGE/PARITY.md"
cp "$ROOT/docs/THIRD-PARTY-NOTICES.md" "$STAGE/THIRD-PARTY-NOTICES.md"
cat > "$STAGE/SCOUT-PACKAGE.txt" <<EOF
name = "Scout"
binary = "scout"
rid = "$RID"
ripgrep_commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"
dotnet_runtime = "10.0.2"
pcre2 = "10.46"
parity = "behavioral parity; identity is Scout-specific (see PARITY.md)"
EOF

rm -f "$ARCHIVE" "$HASH_FILE"
(
    cd "$STAGE_PARENT"
    tar -czf "$ARCHIVE" "scout-$RID"
)

printf '%s  %s\n' "$(sha256_file "$ARCHIVE")" "$(basename "$ARCHIVE")" > "$HASH_FILE"
printf 'OK %s: release package written to %s\n' "$RID" "$ARCHIVE"
