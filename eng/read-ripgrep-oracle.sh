#!/usr/bin/env sh
set -eu

if [ "$#" -ne 2 ]; then
    printf 'usage: %s <oracle-key> <root-key>\n' "$0" >&2
    exit 2
fi

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
TABLE_KEY="$1"
ROOT_KEY="$2"

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
        Linux:x86_64)
            printf 'linux-x64\n'
            ;;
        Linux:aarch64|Linux:arm64)
            printf 'linux-arm64\n'
            ;;
        *)
            fail "Unsupported host for pinned ripgrep oracle: $os $arch"
            ;;
    esac
}

oracle_environment() {
    if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        printf 'github-actions\n'
    else
        printf 'local\n'
    fi
}

read_lock_rid_table_value() {
    awk -v header="[[${1}]]" -v rid="$2" -v environment="$3" -v key="$4" "$strip_toml_value"'
        $0 == header {
            in_table = 1
            matched = 0
            matched_environment = environment == ""
            next
        }
        in_table && $0 ~ /^\[/ {
            in_table = 0
            matched = 0
            matched_environment = environment == ""
        }
        in_table && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            matched = value_of($0) == rid
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            matched_environment = environment != "" && value_of($0) == environment
            next
        }
        in_table && matched && matched_environment && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

lock_has_table() {
    grep -qx "\[\[$1\]\]" "$LOCK"
}

read_oracle_value() {
    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "$HOST_ORACLE_ENVIRONMENT" "$TABLE_KEY")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "" "$TABLE_KEY")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if lock_has_table "ripgrep_oracle"; then
        return 1
    fi

    read_lock_value "$ROOT_KEY"
}

HOST_RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"
read_oracle_value
