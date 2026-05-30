#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
REFERENCE="/Users/brandon/src/ripgrep"

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

read_lock_value() {
    awk -v key="$1" "$strip_toml_value"'
        $0 ~ /^\[/ {
            exit 1
        }
        $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

read_lock_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v key="$3" "$strip_toml_value"'
        $0 == header {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            matched = value_of($0) == name
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

require_literal() {
    value="$1"
    label="$2"
    case "$value" in
        resolved@*)
            fail "$label is not frozen in tests/PREREQS.lock: $value"
            ;;
        "")
            fail "$label is empty in tests/PREREQS.lock"
            ;;
    esac
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

check_file_hash() {
    label="$1"
    path="$2"
    expected_sha256="$3"

    require_literal "$expected_sha256" "$label sha256"
    [ -f "$path" ] || fail "Missing $label: $path"

    actual_sha256="$(sha256_file "$path")"
    expect_equal "$label sha256" "$expected_sha256" "$actual_sha256"
}

check_macos_tool_hash() {
    name="$1"
    path="$(read_lock_table_value "tool.macos" "$name" "path")" || fail "Missing macOS tool path for $name in tests/PREREQS.lock."
    sha256="$(read_lock_table_value "tool.macos" "$name" "sha256")" || fail "Missing macOS tool hash for $name in tests/PREREQS.lock."

    check_file_hash "macOS tool $name" "$path" "$sha256"
}

check_pinned_path_corpora() {
    awk "$strip_toml_value"'
        function flush() {
            if (in_corpus && path != "" && sha256 != "") {
                print name "|" path "|" sha256
            }
        }
        $0 == "[[corpus]]" {
            flush()
            in_corpus = 1
            name = ""
            path = ""
            sha256 = ""
            next
        }
        in_corpus && $0 ~ /^\[\[/ {
            flush()
            in_corpus = 0
            next
        }
        in_corpus && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            name = value_of($0)
            next
        }
        in_corpus && $0 ~ /^[[:space:]]*path[[:space:]]*=/ {
            path = value_of($0)
            next
        }
        in_corpus && $0 ~ /^[[:space:]]*sha256[[:space:]]*=/ {
            sha256 = value_of($0)
            next
        }
        END {
            flush()
        }
    ' "$LOCK" | while IFS='|' read -r name path sha256; do
        check_file_hash "corpus $name" "$path" "$sha256"
    done
}

EXPECTED_SDK="$(read_lock_value "dotnet_sdk")" || fail "Missing dotnet_sdk in tests/PREREQS.lock."
require_literal "$EXPECTED_SDK" "dotnet_sdk"
ACTUAL_SDK="$(dotnet --version)"
expect_equal ".NET SDK" "$EXPECTED_SDK" "$ACTUAL_SDK"

EXPECTED_RIPGREP="$(read_lock_value "ripgrep_commit")" || fail "Missing ripgrep_commit in tests/PREREQS.lock."
require_literal "$EXPECTED_RIPGREP" "ripgrep_commit"
ACTUAL_RIPGREP="$(git -C "$REFERENCE" rev-parse HEAD)"
expect_equal "ripgrep commit" "$EXPECTED_RIPGREP" "$ACTUAL_RIPGREP"

RG_PROFILE="$(read_lock_value "ripgrep_rg_profile")" || fail "Missing ripgrep_rg_profile in tests/PREREQS.lock."
expect_equal "ripgrep build profile" "release-lto" "$RG_PROFILE"

RG_PATH="$(read_lock_value "ripgrep_rg_path")" || fail "Missing ripgrep_rg_path in tests/PREREQS.lock."
RG_SHA256="$(read_lock_value "ripgrep_rg_sha256")" || fail "Missing ripgrep_rg_sha256 in tests/PREREQS.lock."
check_file_hash "reference rg" "$RG_PATH" "$RG_SHA256"

RG_REV="$(printf '%s' "$EXPECTED_RIPGREP" | cut -c 1-10)"
RG_VERSION="$( ( "$RG_PATH" --version || true ) | sed -n '1p' )"
expect_equal "reference rg version" "ripgrep 15.1.0 (rev $RG_REV)" "$RG_VERSION"

if [ "$(uname -s)" = "Darwin" ]; then
    check_macos_tool_hash "gzip"
    check_macos_tool_hash "bzip2"
    check_macos_tool_hash "xz"
    check_macos_tool_hash "zstd"
    check_macos_tool_hash "lz4"
    check_macos_tool_hash "brotli"

    HYPERFINE_PATH="$(read_lock_table_value "tool.macos" "hyperfine" "path")" || fail "Missing macOS hyperfine path in tests/PREREQS.lock."
    HYPERFINE_VERSION="$(read_lock_table_value "tool.macos" "hyperfine" "version")" || fail "Missing macOS hyperfine version in tests/PREREQS.lock."
    HYPERFINE_SHA256="$(read_lock_table_value "tool.macos" "hyperfine" "sha256")" || fail "Missing macOS hyperfine hash in tests/PREREQS.lock."
    check_file_hash "macOS tool hyperfine" "$HYPERFINE_PATH" "$HYPERFINE_SHA256"
    ACTUAL_HYPERFINE_VERSION="$("$HYPERFINE_PATH" --version | sed -n '1p')"
    expect_equal "hyperfine version" "hyperfine $HYPERFINE_VERSION" "$ACTUAL_HYPERFINE_VERSION"
fi

check_pinned_path_corpora

cmp "$REFERENCE/Cargo.lock" "$ROOT/upstream/Cargo.lock"
printf 'Scout preflight passed.\n'
