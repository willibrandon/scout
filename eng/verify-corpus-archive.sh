#!/usr/bin/env sh
set -eu

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' \
        'usage: eng/verify-corpus-archive.sh LOCK CORPUS_NAME ARCHIVE_PATH' \
        '' \
        'Verifies a corpus archive against its archive_sha256 in PREREQS.lock.'
}

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 < "$1" | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum < "$1" | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r < "$1" | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

if [ "$#" -ne 3 ]; then
    usage >&2
    exit 2
fi

LOCK="$1"
CORPUS_NAME="$2"
ARCHIVE_PATH="$3"

[ -f "$LOCK" ] || fail "Missing prerequisite lock: $LOCK"
[ -f "$ARCHIVE_PATH" ] || fail "Missing corpus archive: $ARCHIVE_PATH"

corpus_row="$(awk -v target="$CORPUS_NAME" '
    BEGIN {
        match_count = 0
        invalid_count = 0
    }
    function value_of(line, value) {
        value = substr(line, index(line, "=") + 1)
        sub(/^[[:space:]]*/, "", value)
        sub(/[[:space:]]*$/, "", value)
        if (value !~ /^"[^"]*"$/) {
            return ""
        }
        return substr(value, 2, length(value) - 2)
    }
    function flush() {
        if (!in_corpus || name != target) {
            return
        }
        match_count++
        if (name_count != 1 || archive_sha256_count != 1) {
            invalid_count++
        }
        matched_sha256 = archive_sha256
    }
    $0 == "[[corpus]]" {
        flush()
        in_corpus = 1
        name = ""
        name_count = 0
        archive_sha256 = ""
        archive_sha256_count = 0
        next
    }
    in_corpus && $0 ~ /^\[/ {
        flush()
        in_corpus = 0
        next
    }
    in_corpus && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
        name = value_of($0)
        name_count++
        next
    }
    in_corpus && $0 ~ /^[[:space:]]*archive_sha256[[:space:]]*=/ {
        archive_sha256 = value_of($0)
        archive_sha256_count++
        next
    }
    END {
        flush()
        print match_count "|" invalid_count "|" matched_sha256
    }
' "$LOCK")"

match_count="${corpus_row%%|*}"
corpus_row="${corpus_row#*|}"
invalid_count="${corpus_row%%|*}"
expected_sha256="${corpus_row#*|}"

if [ "$match_count" -ne 1 ]; then
    fail "Expected exactly one [[corpus]] named $CORPUS_NAME in $LOCK; found $match_count."
fi
if [ "$invalid_count" -ne 0 ]; then
    fail "Corpus $CORPUS_NAME must have exactly one literal name and archive_sha256 in $LOCK."
fi
case "$expected_sha256" in
    *[!0-9a-f]*)
        fail "Corpus $CORPUS_NAME archive_sha256 must be a literal lowercase SHA-256 in $LOCK: $expected_sha256"
        ;;
esac
if [ "${#expected_sha256}" -ne 64 ]; then
    fail "Corpus $CORPUS_NAME archive_sha256 must be a literal lowercase SHA-256 in $LOCK: $expected_sha256"
fi

actual_sha256="$(sha256_file "$ARCHIVE_PATH")"
if [ "$actual_sha256" != "$expected_sha256" ]; then
    printf 'Corpus archive SHA-256 mismatch:\n' >&2
    printf '  name: %s\n' "$CORPUS_NAME" >&2
    printf '  path: %s\n' "$ARCHIVE_PATH" >&2
    printf '  expected: %s\n' "$expected_sha256" >&2
    printf '  actual:   %s\n' "$actual_sha256" >&2
    exit 1
fi

printf 'Verified corpus archive: %s (%s)\n' "$CORPUS_NAME" "$actual_sha256" >&2
