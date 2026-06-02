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
        if command -v shasum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 shasum -a 256
        elif command -v sha256sum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum
        else
            find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | while IFS= read -r relative_path; do
                file_sha256="$(sha256_file "$relative_path")"
                printf '%s  %s\n' "$file_sha256" "$relative_path"
            done
        fi | sed 's#  \./#  #'
    ) | sha256_stream
}

is_windows_shell() {
    case "$(uname -s 2>/dev/null || printf 'unknown')" in
        MINGW*|MSYS*|CYGWIN*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

only_windows_tar_symlink_errors() {
    awk '
        BEGIN {
            ok = 1
            saw_symlink_error = 0
        }
        /Cannot create symlink to/ {
            saw_symlink_error = 1
            next
        }
        /Exiting with failure status due to previous errors/ {
            next
        }
        /^[[:space:]]*$/ {
            next
        }
        {
            ok = 0
        }
        END {
            exit !(ok && saw_symlink_error)
        }
    ' "$1"
}

extract_tar_gz() {
    archive="$1"
    destination="$2"
    tar_errors="$(mktemp "$destination/tar-errors.XXXXXX")"

    if tar -xzf "$archive" -C "$destination" 2>"$tar_errors"; then
        rm -f "$tar_errors"
        return 0
    fi

    status="$?"
    cat "$tar_errors" >&2
    if is_windows_shell && only_windows_tar_symlink_errors "$tar_errors"; then
        printf 'Ignoring Windows tar symlink creation errors; the pinned tree hash covers regular files only.\n' >&2
        rm -f "$tar_errors"
        return 0
    fi

    rm -f "$tar_errors"
    return "$status"
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
    download_url="$1"
    download_path="$2"
    download_label="$3"

    if [ -f "$download_path" ]; then
        printf 'Using cached %s: %s\n' "$download_label" "$download_path" >&2
        return
    fi

    mkdir -p "$(dirname -- "$download_path")"
    download_tmp="$download_path.tmp"
    rm -f "$download_tmp"
    printf 'Downloading %s from %s\n' "$download_label" "$download_url" >&2
    curl -L --fail --retry 3 --output "$download_tmp" "$download_url"
    mv "$download_tmp" "$download_path"
}

prepare_opensubtitles() {
    opensub_archive="$OUT_DIR/opensubtitles/en.txt.gz"
    opensub_path="$OUT_DIR/opensubtitles/en.txt"

    download_file "$OPENSUBTITLES_URL" "$opensub_archive" "OpenSubtitles en.txt.gz"

    if [ ! -f "$opensub_path" ]; then
        opensub_tmp="$opensub_path.tmp"
        rm -f "$opensub_tmp"
        printf 'Decompressing OpenSubtitles corpus to %s\n' "$opensub_path" >&2
        gzip -dc "$opensub_archive" > "$opensub_tmp"
        mv "$opensub_tmp" "$opensub_path"
    else
        printf 'Using cached OpenSubtitles text: %s\n' "$opensub_path" >&2
    fi

    archive_path="$(display_path "$opensub_archive")"
    corpus_path="$(display_path "$opensub_path")"
    archive_sha256="$(sha256_file "$opensub_archive")"
    corpus_sha256="$(sha256_file "$opensub_path")"
    corpus_bytes="$(file_bytes "$opensub_path")"

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
    linux_archive="$OUT_DIR/linux/linux-$LINUX_COMMIT.tar.gz"
    linux_tree="$OUT_DIR/linux/linux-$LINUX_COMMIT"

    download_file "$LINUX_ARCHIVE_URL" "$linux_archive" "Linux kernel archive"

    if [ ! -d "$linux_tree" ]; then
        mkdir -p "$(dirname -- "$linux_tree")"
        extract_dir="$(mktemp -d "$(dirname -- "$linux_tree")/extract.XXXXXX")"
        top_level="$(tar -tzf "$linux_archive" | sed -n '1s#/.*##p')"
        [ -n "$top_level" ] || fail "Could not determine Linux archive root."
        printf 'Extracting Linux corpus to %s\n' "$linux_tree" >&2
        extract_tar_gz "$linux_archive" "$extract_dir"
        [ -d "$extract_dir/$top_level" ] || fail "Missing extracted Linux root: $top_level"
        mv "$extract_dir/$top_level" "$linux_tree"
        rmdir "$extract_dir"
    else
        printf 'Using cached Linux tree: %s\n' "$linux_tree" >&2
    fi

    archive_path="$(display_path "$linux_archive")"
    tree_path="$(display_path "$linux_tree")"
    archive_sha256="$(sha256_file "$linux_archive")"
    tree_sha256="$(sha256_tree "$linux_tree")"

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
