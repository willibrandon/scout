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

host_rid() {
    os="$(uname -s)"
    arch="$(uname -m)"
    case "$os:$arch" in
        Darwin:arm64)
            printf 'osx-arm64\n'
            ;;
        Darwin:x86_64)
            printf 'osx-x64\n'
            ;;
        *)
            fail "Unsupported host for pinned hyperfine: $os $arch"
            ;;
    esac
}

oracle_environment() {
    if [ -n "${SCOUT_ORACLE_ENVIRONMENT:-}" ]; then
        case "$SCOUT_ORACLE_ENVIRONMENT" in
            github-actions|local)
                printf '%s\n' "$SCOUT_ORACLE_ENVIRONMENT"
                return
                ;;
            *)
                fail "Unsupported SCOUT_ORACLE_ENVIRONMENT: $SCOUT_ORACLE_ENVIRONMENT"
                ;;
        esac
    fi

    printf 'github-actions\n'
}

read_lock_rid_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v rid="$3" -v environment="$4" -v key="$5" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_rid == rid && table_value != "" &&
                ((environment == "" && table_environment == "") || (environment != "" && table_environment == environment))) {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            table_rid = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_lock_environment_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v environment="$3" -v key="$4" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_environment == environment && table_value != "") {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_macos_tool_value() {
    name="$1"
    key="$2"

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$HOST_RID" "$HOST_ORACLE_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$HOST_RID" "" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_environment_table_value "tool.macos" "$name" "$HOST_ORACLE_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    read_lock_table_value "tool.macos" "$name" "$key"
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
    expected_bottle_tag="$5"
    expected_bottle_url="$6"
    expected_bottle_sha256="$7"

    brew ruby -rjson -e '
        document = JSON.parse(File.read(ARGV[0]))
        formula = document.fetch("formulae").find { |item| item.fetch("name") == "hyperfine" }
        abort("Homebrew formula metadata for hyperfine was not found.") if formula.nil?

        expected_version = ARGV[1]
        expected_source_url = ARGV[2]
        expected_source_sha256 = ARGV[3]
        expected_bottle_tag = ARGV[4]
        expected_bottle_url = ARGV[5]
        expected_bottle_sha256 = ARGV[6]

        actual_version = formula.fetch("versions").fetch("stable")
        abort("Expected hyperfine version #{expected_version}, got #{actual_version}") unless actual_version == expected_version

        stable_source = formula.fetch("urls").fetch("stable")
        actual_source_url = stable_source.fetch("url")
        actual_source_sha256 = stable_source.fetch("checksum")
        abort("Expected hyperfine source URL #{expected_source_url}, got #{actual_source_url}") unless actual_source_url == expected_source_url
        abort("Expected hyperfine source SHA-256 #{expected_source_sha256}, got #{actual_source_sha256}") unless actual_source_sha256 == expected_source_sha256

        bottle = formula.fetch("bottle").fetch("stable").fetch("files")[expected_bottle_tag]
        abort("Expected hyperfine bottle tag #{expected_bottle_tag}; Homebrew metadata did not contain it.") if bottle.nil?
        actual_bottle_url = bottle.fetch("url")
        actual_bottle_sha256 = bottle.fetch("sha256")
        abort("Expected hyperfine bottle URL #{expected_bottle_url}, got #{actual_bottle_url}") unless actual_bottle_url == expected_bottle_url
        abort("Expected hyperfine bottle SHA-256 #{expected_bottle_sha256}, got #{actual_bottle_sha256}") unless actual_bottle_sha256 == expected_bottle_sha256
    ' "$json_path" "$expected_version" "$expected_source_url" "$expected_source_sha256" "$expected_bottle_tag" "$expected_bottle_url" "$expected_bottle_sha256"
}

retry_command() {
    attempt=1
    max_attempts=3
    delay_seconds=10

    while [ "$attempt" -le "$max_attempts" ]; do
        if "$@"; then
            return 0
        fi

        status=$?
        if [ "$attempt" -eq "$max_attempts" ]; then
            return "$status"
        fi

        printf 'Command failed with exit %s; retrying in %s seconds (%s/%s): %s\n' "$status" "$delay_seconds" "$attempt" "$max_attempts" "$*" >&2
        sleep "$delay_seconds"
        attempt=$((attempt + 1))
        delay_seconds=$((delay_seconds * 2))
    done
}

[ "$(uname -s)" = "Darwin" ] || fail "Pinned hyperfine provisioning is only defined for macOS release gates."
require_command brew
require_command sed

HOST_RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"
NAME="hyperfine"
VERSION="$(read_macos_tool_value "$NAME" "version")" || fail "Missing macOS hyperfine version in tests/PREREQS.lock."
PATH_VALUE="$(read_macos_tool_value "$NAME" "path")" || fail "Missing macOS hyperfine path in tests/PREREQS.lock."
SOURCE_URL="$(read_macos_tool_value "$NAME" "source_url")" || fail "Missing macOS hyperfine source URL in tests/PREREQS.lock."
SOURCE_SHA256="$(read_macos_tool_value "$NAME" "source_sha256")" || fail "Missing macOS hyperfine source hash in tests/PREREQS.lock."
BOTTLE_TAG="$(read_macos_tool_value "$NAME" "bottle_tag")" || fail "Missing macOS hyperfine bottle tag in tests/PREREQS.lock."
BOTTLE_URL="$(read_macos_tool_value "$NAME" "bottle_url")" || fail "Missing macOS hyperfine bottle URL in tests/PREREQS.lock."
BOTTLE_SHA256="$(read_macos_tool_value "$NAME" "bottle_sha256")" || fail "Missing macOS hyperfine bottle hash in tests/PREREQS.lock."
BINARY_SHA256="$(read_macos_tool_value "$NAME" "sha256")" || fail "Missing macOS hyperfine binary hash in tests/PREREQS.lock."

require_literal "$VERSION" "macOS hyperfine version"
require_literal "$SOURCE_URL" "macOS hyperfine source URL"
require_literal "$SOURCE_SHA256" "macOS hyperfine source SHA-256"
require_literal "$BOTTLE_TAG" "macOS hyperfine bottle tag"
require_literal "$BOTTLE_URL" "macOS hyperfine bottle URL"
require_literal "$BOTTLE_SHA256" "macOS hyperfine bottle SHA-256"
require_literal "$BINARY_SHA256" "macOS hyperfine binary SHA-256"

BREW_INFO="$(mktemp "${TMPDIR:-/tmp}/hyperfine-brew-info.XXXXXX.json")"
trap 'rm -f "$BREW_INFO"' EXIT
brew info --json=v2 "$NAME" > "$BREW_INFO"
verify_homebrew_metadata "$BREW_INFO" "$VERSION" "$SOURCE_URL" "$SOURCE_SHA256" "$BOTTLE_TAG" "$BOTTLE_URL" "$BOTTLE_SHA256"

retry_command brew fetch --formula --build-from-source "$NAME"
SOURCE_ARCHIVE="$(brew --cache --build-from-source "$NAME")"
check_file_hash "hyperfine source archive" "$SOURCE_ARCHIVE" "$SOURCE_SHA256"

retry_command brew fetch --formula --bottle-tag="$BOTTLE_TAG" "$NAME"
BOTTLE_ARCHIVE="$(brew --cache --formula --bottle-tag="$BOTTLE_TAG" "$NAME")"
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
