#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"

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

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "$1 is required to provision pinned hyperfine."
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

expect_equal() {
    label="$1"
    expected="$2"
    actual="$3"
    if [ "$actual" != "$expected" ]; then
        printf 'Expected %s %s, got %s\n' "$label" "$expected" "$actual" >&2
        exit 1
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

hash_matches() {
    path="$1"
    expected_sha256="$2"

    [ -f "$path" ] || return 1
    actual_sha256="$(sha256_file "$path")"
    [ "$actual_sha256" = "$expected_sha256" ]
}

version_matches() {
    path="$1"
    expected_version="$2"

    [ -x "$path" ] || return 1
    actual_version="$("$path" --version | sed -n '1p')"
    [ "$actual_version" = "hyperfine $expected_version" ]
}

verify_homebrew_metadata() {
    json_path="$1"
    expected_version="$2"
    expected_source_url="$3"
    expected_source_sha256="$4"
    expected_bottle_url="$5"
    expected_bottle_sha256="$6"

    brew ruby -rjson -e '
        document = JSON.parse(File.read(ARGV[0]))
        formula = document.fetch("formulae").find { |item| item.fetch("name") == "hyperfine" }
        abort("Homebrew formula metadata for hyperfine was not found.") if formula.nil?

        expected_version = ARGV[1]
        expected_source_url = ARGV[2]
        expected_source_sha256 = ARGV[3]
        expected_bottle_url = ARGV[4]
        expected_bottle_sha256 = ARGV[5]

        actual_version = formula.fetch("versions").fetch("stable")
        abort("Expected hyperfine version #{expected_version}, got #{actual_version}") unless actual_version == expected_version

        stable_source = formula.fetch("urls").fetch("stable")
        actual_source_url = stable_source.fetch("url")
        actual_source_sha256 = stable_source.fetch("checksum")
        abort("Expected hyperfine source URL #{expected_source_url}, got #{actual_source_url}") unless actual_source_url == expected_source_url
        abort("Expected hyperfine source SHA-256 #{expected_source_sha256}, got #{actual_source_sha256}") unless actual_source_sha256 == expected_source_sha256

        bottles = formula.fetch("bottle").fetch("stable").fetch("files").values
        match = bottles.find do |bottle|
            bottle.fetch("url") == expected_bottle_url && bottle.fetch("sha256") == expected_bottle_sha256
        end
        abort("Expected hyperfine bottle #{expected_bottle_url} with SHA-256 #{expected_bottle_sha256}; Homebrew metadata did not contain it.") if match.nil?
    ' "$json_path" "$expected_version" "$expected_source_url" "$expected_source_sha256" "$expected_bottle_url" "$expected_bottle_sha256"
}

[ "$(uname -s)" = "Darwin" ] || fail "Pinned hyperfine provisioning is only defined for macOS release gates."
require_command brew
require_command sed

NAME="hyperfine"
VERSION="$(read_lock_table_value "tool.macos" "$NAME" "version")" || fail "Missing macOS hyperfine version in tests/PREREQS.lock."
PATH_VALUE="$(read_lock_table_value "tool.macos" "$NAME" "path")" || fail "Missing macOS hyperfine path in tests/PREREQS.lock."
SOURCE_URL="$(read_lock_table_value "tool.macos" "$NAME" "source_url")" || fail "Missing macOS hyperfine source URL in tests/PREREQS.lock."
SOURCE_SHA256="$(read_lock_table_value "tool.macos" "$NAME" "source_sha256")" || fail "Missing macOS hyperfine source hash in tests/PREREQS.lock."
BOTTLE_URL="$(read_lock_table_value "tool.macos" "$NAME" "bottle_url")" || fail "Missing macOS hyperfine bottle URL in tests/PREREQS.lock."
BOTTLE_SHA256="$(read_lock_table_value "tool.macos" "$NAME" "bottle_sha256")" || fail "Missing macOS hyperfine bottle hash in tests/PREREQS.lock."
BINARY_SHA256="$(read_lock_table_value "tool.macos" "$NAME" "sha256")" || fail "Missing macOS hyperfine binary hash in tests/PREREQS.lock."

require_literal "$VERSION" "macOS hyperfine version"
require_literal "$SOURCE_URL" "macOS hyperfine source URL"
require_literal "$SOURCE_SHA256" "macOS hyperfine source SHA-256"
require_literal "$BOTTLE_URL" "macOS hyperfine bottle URL"
require_literal "$BOTTLE_SHA256" "macOS hyperfine bottle SHA-256"
require_literal "$BINARY_SHA256" "macOS hyperfine binary SHA-256"

BREW_INFO="$(mktemp "${TMPDIR:-/tmp}/hyperfine-brew-info.XXXXXX.json")"
trap 'rm -f "$BREW_INFO"' EXIT
brew info --json=v2 "$NAME" > "$BREW_INFO"
verify_homebrew_metadata "$BREW_INFO" "$VERSION" "$SOURCE_URL" "$SOURCE_SHA256" "$BOTTLE_URL" "$BOTTLE_SHA256"

brew fetch --formula --build-from-source "$NAME"
SOURCE_ARCHIVE="$(brew --cache --build-from-source "$NAME")"
check_file_hash "hyperfine source archive" "$SOURCE_ARCHIVE" "$SOURCE_SHA256"

brew fetch --formula "$NAME"
BOTTLE_ARCHIVE="$(brew --cache "$NAME")"
check_file_hash "hyperfine bottle archive" "$BOTTLE_ARCHIVE" "$BOTTLE_SHA256"

if ! hash_matches "$PATH_VALUE" "$BINARY_SHA256" || ! version_matches "$PATH_VALUE" "$VERSION"; then
    if brew list --formula "$NAME" >/dev/null 2>&1; then
        brew reinstall --formula "$NAME"
    else
        brew install --formula "$NAME"
    fi
fi

check_file_hash "macOS tool hyperfine" "$PATH_VALUE" "$BINARY_SHA256"
version_matches "$PATH_VALUE" "$VERSION" || fail "hyperfine version mismatch after install."
printf 'OK pinned hyperfine is ready at %s\n' "$PATH_VALUE"
