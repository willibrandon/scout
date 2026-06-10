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
    if [ -n "${SCOUT_HOST_RID:-}" ]; then
        case "$SCOUT_HOST_RID" in
            linux-x64|linux-arm64|osx-x64|osx-arm64)
                printf '%s\n' "$SCOUT_HOST_RID"
                return
                ;;
            *)
                fail "Unsupported SCOUT_HOST_RID for pinned ripgrep oracle archive: $SCOUT_HOST_RID"
                ;;
        esac
    fi

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
            fail "Unsupported host for pinned ripgrep oracle archive: $os $arch"
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
                fail "Unsupported SCOUT_ORACLE_ENVIRONMENT for pinned ripgrep oracle archive: $SCOUT_ORACLE_ENVIRONMENT"
                ;;
        esac
    fi

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

read_oracle_value() {
    table_key="$1"
    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "$HOST_ORACLE_ENVIRONMENT" "$table_key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "" "$table_key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    return 1
}

resolve_repo_path() {
    case "$1" in
        /*)
            printf '%s\n' "$1"
            ;;
        *)
            printf '%s/%s\n' "$ROOT" "$1"
            ;;
    esac
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

verify_lowercase_sha256() {
    label="$1"
    value="$2"
    case "$value" in
        *[!0123456789abcdef]*)
            fail "$label must be a literal lowercase SHA-256 in tests/PREREQS.lock: $value"
            ;;
    esac
    [ "${#value}" -eq 64 ] || fail "$label must be a literal lowercase SHA-256 in tests/PREREQS.lock: $value"
}

verify_file_hash() {
    label="$1"
    path="$2"
    expected_sha256="$3"

    [ -f "$path" ] || fail "Missing $label: $path"
    actual_sha256="$(sha256_file "$path")"
    expect_equal "$label sha256" "$expected_sha256" "$actual_sha256"
}

verify_binary_hash() {
    label="$1"
    path="$2"
    expected_sha256="$3"

    [ -x "$path" ] || fail "Missing executable $label: $path"
    actual_sha256="$(sha256_file "$path")"
    expect_equal "$label sha256" "$expected_sha256" "$actual_sha256"
}

HOST_RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"
EXPECTED_RIPGREP="$(read_lock_value "ripgrep_commit")" || fail "Missing ripgrep_commit in tests/PREREQS.lock."
EXPECTED_PCRE2_VERSION="$(read_lock_value "ripgrep_pcre2_reported_version")" || fail "Missing ripgrep_pcre2_reported_version in tests/PREREQS.lock."
ARCHIVE_PATH_VALUE="$(read_oracle_value "archive_path")" || fail "Missing ripgrep_oracle.archive_path for $HOST_RID in tests/PREREQS.lock."
ARCHIVE_SHA256="$(read_oracle_value "archive_sha256")" || fail "Missing ripgrep_oracle.archive_sha256 for $HOST_RID in tests/PREREQS.lock."
RG_PATH_VALUE="$(read_oracle_value "path")" || fail "Missing ripgrep_oracle.path for $HOST_RID in tests/PREREQS.lock."
RG_SHA256="$(read_oracle_value "sha256")" || fail "Missing ripgrep_oracle.sha256 for $HOST_RID in tests/PREREQS.lock."
RG_PCRE2_PATH_VALUE="$(read_oracle_value "pcre2_path")" || fail "Missing ripgrep_oracle.pcre2_path for $HOST_RID in tests/PREREQS.lock."
RG_PCRE2_SHA256="$(read_oracle_value "pcre2_sha256")" || fail "Missing ripgrep_oracle.pcre2_sha256 for $HOST_RID in tests/PREREQS.lock."
ARCHIVE_PATH="$(resolve_repo_path "$ARCHIVE_PATH_VALUE")"
RG_PATH="$(resolve_repo_path "$RG_PATH_VALUE")"
RG_PCRE2_PATH="$(resolve_repo_path "$RG_PCRE2_PATH_VALUE")"

verify_lowercase_sha256 "ripgrep_oracle.archive_sha256" "$ARCHIVE_SHA256"
verify_lowercase_sha256 "ripgrep_oracle.sha256" "$RG_SHA256"
verify_lowercase_sha256 "ripgrep_oracle.pcre2_sha256" "$RG_PCRE2_SHA256"
verify_file_hash "pinned ripgrep oracle archive" "$ARCHIVE_PATH" "$ARCHIVE_SHA256"
command -v unzip >/dev/null 2>&1 || fail "unzip is required to restore the pinned ripgrep oracle archive."

rm -f "$RG_PATH" "$RG_PCRE2_PATH"
unzip -q -o "$ARCHIVE_PATH" -d "$ROOT"
chmod +x "$RG_PATH" "$RG_PCRE2_PATH"

verify_binary_hash "reference rg" "$RG_PATH" "$RG_SHA256"
verify_binary_hash "PCRE2 reference rg" "$RG_PCRE2_PATH" "$RG_PCRE2_SHA256"

expected_revision="$(printf '%s\n' "$EXPECTED_RIPGREP" | cut -c 1-10)"
actual_version="$("$RG_PATH" --version | sed -n '1p')"
case "$actual_version" in
    *"rev $expected_revision"*)
        ;;
    *)
        fail "Expected reference rg revision $expected_revision, got: $actual_version"
        ;;
esac

actual_pcre2_version="$("$RG_PCRE2_PATH" --pcre2-version | tr -d '\r')"
expect_equal "PCRE2 reported version" "$EXPECTED_PCRE2_VERSION" "$actual_pcre2_version"

printf 'OK pinned ripgrep oracle archive restored for %s from %s\n' "$HOST_RID" "$ARCHIVE_PATH_VALUE"
