#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"

if [ "$#" -ne 1 ]; then
    printf 'usage: %s <linux-rid>\n' "$0" >&2
    exit 2
fi

RID="$1"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

strip_toml_value='
function value_of(line) {
    value = line
    sub(/^[^=]*=[[:space:]]*/, "", value)
    sub(/^"/, "", value)
    sub(/"$/, "", value)
    return value
}
'

read_section_value() {
    awk -v header="[$1]" -v key="$2" "$strip_toml_value"'
        $0 == header {
            in_section = 1
            next
        }
        in_section && $0 ~ /^\[/ {
            in_section = 0
        }
        in_section && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            print value_of($0)
            found = 1
            exit 0
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_linux_tools() {
    awk -v rid="$RID" "$strip_toml_value"'
        function reset_tool() {
            tool_rid = ""
            name = ""
            package = ""
            binary = ""
            path = ""
            version = ""
            sha256 = ""
        }
        function flush() {
            if (in_tool && tool_rid == rid) {
                if (name == "" || package == "" || binary == "" || path == "" || version == "" || sha256 == "") {
                    exit 2
                }
                print name "|" package "|" binary "|" path "|" version "|" sha256
            }
        }
        $0 == "[[tool.linux]]" {
            flush()
            in_tool = 1
            reset_tool()
            next
        }
        in_tool && $0 ~ /^\[/ {
            flush()
            in_tool = 0
            reset_tool()
            next
        }
        in_tool && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            tool_rid = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            name = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*package[[:space:]]*=/ {
            package = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*binary[[:space:]]*=/ {
            binary = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*path[[:space:]]*=/ {
            path = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*version[[:space:]]*=/ {
            version = value_of($0)
            next
        }
        in_tool && $0 ~ /^[[:space:]]*sha256[[:space:]]*=/ {
            sha256 = value_of($0)
            next
        }
        END {
            flush()
        }
    ' "$LOCK"
}

expect_equal() {
    label="$1"
    expected="$2"
    actual="$3"
    if [ "$actual" != "$expected" ]; then
        printf 'Expected %s %s, got %s\n' "$label" "$expected" "$actual" >&2
        exit 1
    fi
}

sha256_file() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r "$1" | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

SNAPSHOT_URL="$(read_section_value "linux_container" "snapshot_url")" || fail "Missing linux_container.snapshot_url in tests/PREREQS.lock."
if [ -n "${LINUX_SNAPSHOT_URL:-}" ]; then
    expect_equal "LINUX_SNAPSHOT_URL" "$SNAPSHOT_URL" "$LINUX_SNAPSHOT_URL"
fi

LIBC6_VERSION="$(read_section_value "linux_container" "libc6_version")" || fail "Missing linux_container.libc6_version in tests/PREREQS.lock."
LIBC_BIN_VERSION="$(read_section_value "linux_container" "libc_bin_version")" || fail "Missing linux_container.libc_bin_version in tests/PREREQS.lock."
if [ -n "${LINUX_LIBC_VERSION:-}" ]; then
    expect_equal "LINUX_LIBC_VERSION" "$LIBC6_VERSION" "$LINUX_LIBC_VERSION"
fi

if [ -f /etc/apt/sources.list ] && ! grep -F "$SNAPSHOT_URL" /etc/apt/sources.list >/dev/null; then
    fail "/etc/apt/sources.list does not use the pinned snapshot URL."
fi

for source_file in /etc/apt/sources.list /etc/apt/sources.list.d/*.list /etc/apt/sources.list.d/*.sources; do
    [ -e "$source_file" ] || continue
    if grep -E 'deb\.debian\.org|security\.debian\.org' "$source_file" >/dev/null; then
        fail "Unpinned Debian source remains in $source_file."
    fi
done

expect_equal "libc6 package version" "$LIBC6_VERSION" "$(dpkg-query -W -f='${Version}' libc6)"
expect_equal "libc-bin package version" "$LIBC_BIN_VERSION" "$(dpkg-query -W -f='${Version}' libc-bin)"

rows="${TMPDIR:-/tmp}/scout-linux-prereqs.$$"
trap 'rm -f "$rows"' EXIT HUP INT TERM
read_linux_tools > "$rows" || fail "Invalid linux tool entries for $RID in tests/PREREQS.lock."
[ -s "$rows" ] || fail "Missing linux tool entries for $RID in tests/PREREQS.lock."

while IFS='|' read -r name package binary path expected_version expected_sha256; do
    actual_path="$(command -v "$binary" || true)"
    expect_equal "$name path" "$path" "$actual_path"

    actual_version="$(dpkg-query -W -f='${Version}' "$package")"
    expect_equal "$name package version" "$expected_version" "$actual_version"

    actual_sha256="$(sha256_file "$path")"
    expect_equal "$name sha256" "$expected_sha256" "$actual_sha256"
done < "$rows"

printf 'OK %s: Linux prerequisites match tests/PREREQS.lock\n' "$RID"
