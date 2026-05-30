#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT_DIR="${SCOUT_CORPORA_DIR:-$ROOT/artifacts/corpora}"

OPENSUBTITLES_URL="https://object.pouta.csc.fi/OPUS-OpenSubtitles/v2016/mono/en.txt.gz"
LINUX_COMMIT="84e57d292203a45c96dbcb2e6be9dd80961d981a"
LINUX_ARCHIVE_URL="https://codeload.github.com/BurntSushi/linux/tar.gz/$LINUX_COMMIT"

FETCH_OPENSUBTITLES=0
FETCH_LINUX=0
SELECTED=0

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' \
        'usage: eng/fetch-corpora.sh [--all|--opensubtitles|--linux] [--output-dir DIR]' \
        '' \
        'Fetches the external corpora required by docs/DESIGN.md and prints' \
        'replacement [[corpus]] TOML blocks for tests/PREREQS.lock.' \
        '' \
        'Default output directory: artifacts/corpora'
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r "$1" | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

sha256_stream() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

sha256_tree() {
    tree="$1"
    [ -d "$tree" ] || fail "Missing tree corpus: $tree"
    (
        cd "$tree"
        find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | while IFS= read -r relative_path; do
            file_sha256="$(sha256_file "$relative_path")"
            printf '%s  %s\n' "$file_sha256" "$relative_path"
        done
    ) | sha256_stream
}

file_bytes() {
    wc -c < "$1" | tr -d ' '
}

display_path() {
    case "$1" in
        "$ROOT"/*)
            printf '%s\n' "${1#$ROOT/}"
            ;;
        *)
            printf '%s\n' "$1"
            ;;
    esac
}

download_file() {
    url="$1"
    path="$2"
    label="$3"

    if [ -f "$path" ]; then
        printf 'Using cached %s: %s\n' "$label" "$path" >&2
        return
    fi

    mkdir -p "$(dirname -- "$path")"
    tmp="$path.tmp"
    rm -f "$tmp"
    printf 'Downloading %s from %s\n' "$label" "$url" >&2
    curl -L --fail --retry 3 --output "$tmp" "$url"
    mv "$tmp" "$path"
}

prepare_opensubtitles() {
    archive="$OUT_DIR/opensubtitles/en.txt.gz"
    path="$OUT_DIR/opensubtitles/en.txt"

    download_file "$OPENSUBTITLES_URL" "$archive" "OpenSubtitles en.txt.gz"

    if [ ! -f "$path" ]; then
        tmp="$path.tmp"
        rm -f "$tmp"
        printf 'Decompressing OpenSubtitles corpus to %s\n' "$path" >&2
        gzip -dc "$archive" > "$tmp"
        mv "$tmp" "$path"
    else
        printf 'Using cached OpenSubtitles text: %s\n' "$path" >&2
    fi

    archive_path="$(display_path "$archive")"
    corpus_path="$(display_path "$path")"
    archive_sha256="$(sha256_file "$archive")"
    corpus_sha256="$(sha256_file "$path")"
    corpus_bytes="$(file_bytes "$path")"

    cat <<EOF
[[corpus]]
name = "opensubtitles-en"
kind = "file"
archive_url = "$OPENSUBTITLES_URL"
archive_path = "$archive_path"
archive_sha256 = "$archive_sha256"
path = "$corpus_path"
sha256 = "$corpus_sha256"
bytes = "$corpus_bytes"
EOF
}

prepare_linux() {
    archive="$OUT_DIR/linux/linux-$LINUX_COMMIT.tar.gz"
    tree="$OUT_DIR/linux/linux-$LINUX_COMMIT"

    download_file "$LINUX_ARCHIVE_URL" "$archive" "Linux kernel archive"

    if [ ! -d "$tree" ]; then
        mkdir -p "$(dirname -- "$tree")"
        extract_dir="$(mktemp -d "$(dirname -- "$tree")/extract.XXXXXX")"
        top_level="$(tar -tzf "$archive" | sed -n '1s#/.*##p')"
        [ -n "$top_level" ] || fail "Could not determine Linux archive root."
        printf 'Extracting Linux corpus to %s\n' "$tree" >&2
        tar -xzf "$archive" -C "$extract_dir"
        [ -d "$extract_dir/$top_level" ] || fail "Missing extracted Linux root: $top_level"
        mv "$extract_dir/$top_level" "$tree"
        rmdir "$extract_dir"
    else
        printf 'Using cached Linux tree: %s\n' "$tree" >&2
    fi

    [ -f "$tree/vmlinux" ] || fail "Linux corpus is missing vmlinux: $tree/vmlinux"

    archive_path="$(display_path "$archive")"
    tree_path="$(display_path "$tree")"
    archive_sha256="$(sha256_file "$archive")"
    tree_sha256="$(sha256_tree "$tree")"

    cat <<EOF
[[corpus]]
name = "linux-kernel"
kind = "tree"
commit = "$LINUX_COMMIT"
archive_url = "$LINUX_ARCHIVE_URL"
archive_path = "$archive_path"
archive_sha256 = "$archive_sha256"
tree_path = "$tree_path"
tree_sha256 = "$tree_sha256"
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --all)
            FETCH_OPENSUBTITLES=1
            FETCH_LINUX=1
            SELECTED=1
            shift
            ;;
        --opensubtitles)
            FETCH_OPENSUBTITLES=1
            SELECTED=1
            shift
            ;;
        --linux)
            FETCH_LINUX=1
            SELECTED=1
            shift
            ;;
        --output-dir)
            [ "$#" -ge 2 ] || fail "Missing value for --output-dir."
            OUT_DIR="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: $1"
            ;;
    esac
done

if [ "$SELECTED" -eq 0 ]; then
    FETCH_OPENSUBTITLES=1
    FETCH_LINUX=1
fi

require_command curl
require_command gzip
require_command tar
require_command find
require_command sort

printed=0
if [ "$FETCH_OPENSUBTITLES" -eq 1 ]; then
    prepare_opensubtitles
    printed=1
fi

if [ "$FETCH_LINUX" -eq 1 ]; then
    if [ "$printed" -eq 1 ]; then
        printf '\n'
    fi
    prepare_linux
fi
