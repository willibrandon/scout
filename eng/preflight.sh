#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
REFERENCE="${SCOUT_RIPGREP_REFERENCE:-/Users/brandon/src/ripgrep}"
. "$ROOT/eng/sha256-set.sh"

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

read_lock_macos_tool_value() {
    awk -v name="$1" -v rid="$HOST_RID" -v environment="$HOST_ORACLE_ENVIRONMENT" -v key="$2" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            table_has_value = 0
        }
        function maybe_select(    score) {
            if (!in_table || table_name != name) {
                return
            }

            score = 0
            if (table_rid == rid && table_environment == environment) {
                score = 4
            } else if (table_rid == rid && table_environment == "") {
                score = 3
            } else if (table_rid == "" && table_environment == environment) {
                score = 2
            } else if (table_rid == "" && table_environment == "") {
                score = 1
            }

            if (score > selected_score) {
                selected_score = score
                selected_value = table_value
                selected_has_value = table_has_value
                duplicate = 0
            } else if (score != 0 && score == selected_score) {
                duplicate = 1
            }
        }
        $0 == "[[tool.macos]]" {
            maybe_select()
            in_table = 1
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            table_has_value = 0
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_select()
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
            table_has_value = 1
            next
        }
        END {
            maybe_select()
            if (selected_score == 0 || !selected_has_value || duplicate) {
                exit 1
            }

            print selected_value
        }
    ' "$LOCK"
}

host_rid() {
    if [ -n "${SCOUT_HOST_RID:-}" ]; then
        case "$SCOUT_HOST_RID" in
            osx-arm64|osx-x64|linux-x64|linux-arm64|win-x64|win-arm64)
                printf '%s\n' "$SCOUT_HOST_RID"
                return
                ;;
            *)
                fail "Unsupported SCOUT_HOST_RID for pinned ripgrep oracle: $SCOUT_HOST_RID"
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
        MINGW*:x86_64|MSYS*:x86_64|CYGWIN*:x86_64)
            printf 'win-x64\n'
            ;;
        MINGW*:aarch64|MINGW*:arm64|MSYS*:aarch64|MSYS*:arm64|CYGWIN*:aarch64|CYGWIN*:arm64)
            printf 'win-arm64\n'
            ;;
        *)
            fail "Unsupported host for pinned ripgrep oracle: $os $arch"
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
                fail "Unsupported SCOUT_ORACLE_ENVIRONMENT for pinned ripgrep oracle: $SCOUT_ORACLE_ENVIRONMENT"
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

lock_has_table() {
    grep -qx "\[\[$1\]\]" "$LOCK"
}

read_oracle_value() {
    table_key="$1"
    root_key="$2"
    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "$HOST_ORACLE_ENVIRONMENT" "$table_key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "ripgrep_oracle" "$HOST_RID" "" "$table_key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if lock_has_table "ripgrep_oracle"; then
        return 1
    fi

    read_lock_value "$root_key"
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

is_windows_shell() {
    case "$(uname -s 2>/dev/null || printf unknown)" in
        MINGW*|MSYS*|CYGWIN*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

normalize_windows_text_file() {
    path="$1"

    if ! is_windows_shell; then
        return 0
    fi

    normalized="$path.lf"
    sed 's/\r$//' "$path" > "$normalized"
    mv "$normalized" "$path"
}

compare_pinned_text_file() {
    left="$1"
    right="$2"
    label="$3"

    if ! is_windows_shell; then
        cmp "$left" "$right" || fail "$label differs."
        return 0
    fi

    text_compare_tmp="$ROOT/artifacts/preflight/text-compare"
    rm -rf "$text_compare_tmp"
    mkdir -p "$text_compare_tmp"

    left_normalized="$(mktemp "$text_compare_tmp/left.XXXXXX")"
    right_normalized="$(mktemp "$text_compare_tmp/right.XXXXXX")"
    cp "$left" "$left_normalized"
    cp "$right" "$right_normalized"
    normalize_windows_text_file "$left_normalized"
    normalize_windows_text_file "$right_normalized"

    if ! cmp "$left_normalized" "$right_normalized"; then
        rm -f "$left_normalized" "$right_normalized"
        fail "$label differs."
    fi

    rm -f "$left_normalized" "$right_normalized"
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

derive_reference_from_oracle_path() {
    case "$1" in
        */target/*/rg)
            printf '%s\n' "${1%%/target/*/rg}"
            ;;
        */target/*/rg.exe)
            printf '%s\n' "${1%%/target/*/rg.exe}"
            ;;
        *)
            printf '%s\n' "$REFERENCE"
            ;;
    esac
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

mark_root_safe_for_git() {
    if command -v git >/dev/null 2>&1; then
        git config --global --add safe.directory "$ROOT" 2>/dev/null || true
    fi
}

check_macos_tool_hash() {
    name="$1"
    path="$(read_lock_macos_tool_value "$name" "path")" || fail "Missing macOS tool path for $name in tests/PREREQS.lock."
    version="$(read_lock_macos_tool_value "$name" "version")" || fail "Missing macOS tool version for $name in tests/PREREQS.lock."
    sha256_set="$(read_lock_macos_tool_value "$name" "sha256")" || fail "Missing macOS tool hash for $name in tests/PREREQS.lock."

    approved_sha256_values="$(decode_sha256_set "$sha256_set")" || fail "macOS tool $name sha256 must be a literal lowercase SHA-256 or nonempty array of unique literal lowercase SHA-256 values in tests/PREREQS.lock."
    [ -f "$path" ] || fail "Missing macOS tool $name: $path"

    actual_sha256="$(sha256_file "$path")"
    if sha256_set_contains "$sha256_set" "$actual_sha256"; then
        return
    else
        membership_status=$?
    fi

    [ "$membership_status" -eq 1 ] || fail "Calculated macOS tool $name sha256 is not a literal lowercase SHA-256: $actual_sha256"

    printf 'macOS tool hash mismatch: name=%s rid=%s environment=%s\n' "$name" "$HOST_RID" "$HOST_ORACLE_ENVIRONMENT" >&2
    printf '  version: %s\n  path: %s\n  expected sha256 (one of):\n' "$version" "$path" >&2
    printf '%s\n' "$approved_sha256_values" | while IFS= read -r approved_sha256; do
        printf '    %s\n' "$approved_sha256" >&2
    done
    printf '  actual sha256:\n    %s\n' "$actual_sha256" >&2
    MACOS_TOOL_FAILURES=1
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
        require_literal "$sha256" "corpus $name sha256"
        path="$(resolve_repo_path "$path")"
        check_file_hash "corpus $name" "$path" "$sha256"
    done
}

EXPECTED_SDK="$(read_lock_value "dotnet_sdk")" || fail "Missing dotnet_sdk in tests/PREREQS.lock."
require_literal "$EXPECTED_SDK" "dotnet_sdk"
ACTUAL_SDK="$(dotnet --version)"
expect_equal ".NET SDK" "$EXPECTED_SDK" "$ACTUAL_SDK"

mark_root_safe_for_git
"$ROOT/eng/check-msbuild-warning-gates.sh" "$ROOT/artifacts/preflight/msbuild-warning-gates"
dotnet format "$ROOT/Scout.slnx" --no-restore --verify-no-changes
if command -v python3 >/dev/null 2>&1; then
    BENCH_PYTHON="python3"
elif command -v python >/dev/null 2>&1; then
    BENCH_PYTHON="python"
else
    fail "python3 or python is required to verify the benchmark sampling harness."
fi
"$BENCH_PYTHON" -m unittest discover -s "$ROOT/bench/tests" -p 'test_*.py'

EXPECTED_RIPGREP="$(read_lock_value "ripgrep_commit")" || fail "Missing ripgrep_commit in tests/PREREQS.lock."
require_literal "$EXPECTED_RIPGREP" "ripgrep_commit"
HOST_RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"

RG_PROFILE="$(read_oracle_value "profile" "ripgrep_rg_profile")" || fail "Missing ripgrep_oracle.profile for $HOST_RID in tests/PREREQS.lock."
expect_equal "ripgrep build profile" "release-lto" "$RG_PROFILE"

RG_PATH="$(resolve_repo_path "$(read_oracle_value "path" "ripgrep_rg_path")")" || fail "Missing ripgrep_oracle.path for $HOST_RID in tests/PREREQS.lock."
RG_SHA256="$(read_oracle_value "sha256" "ripgrep_rg_sha256")" || fail "Missing ripgrep_oracle.sha256 for $HOST_RID in tests/PREREQS.lock."
if ARCHIVE_PATH_VALUE="$(read_oracle_value "archive_path" "ripgrep_oracle_archive_path" 2>/dev/null)"; then
    ARCHIVE_PATH="$(resolve_repo_path "$ARCHIVE_PATH_VALUE")"
    ARCHIVE_SHA256="$(read_oracle_value "archive_sha256" "ripgrep_oracle_archive_sha256")" || fail "Missing ripgrep_oracle.archive_sha256 for $HOST_RID in tests/PREREQS.lock."
    check_file_hash "pinned ripgrep oracle archive" "$ARCHIVE_PATH" "$ARCHIVE_SHA256"
    HAS_RIPGREP_SOURCE_CHECKOUT=0
else
    REFERENCE="$(derive_reference_from_oracle_path "$RG_PATH")"
    ACTUAL_RIPGREP="$(git -C "$REFERENCE" rev-parse HEAD)"
    expect_equal "ripgrep commit" "$EXPECTED_RIPGREP" "$ACTUAL_RIPGREP"
    HAS_RIPGREP_SOURCE_CHECKOUT=1
fi
check_file_hash "reference rg" "$RG_PATH" "$RG_SHA256"

RG_REV="$(printf '%s' "$EXPECTED_RIPGREP" | cut -c 1-10)"
RG_VERSION="$( ( "$RG_PATH" --version || true ) | sed -n '1p' )"
expect_equal "reference rg version" "ripgrep 15.1.0 (rev $RG_REV)" "$RG_VERSION"

"$ROOT/eng/verify-generated-artifacts.sh" "$RG_PATH"
"$ROOT/eng/verify-identity-rebrand.sh"
"$ROOT/eng/verify-unicode-data.sh"

RG_PCRE2_PROFILE="$(read_oracle_value "pcre2_profile" "ripgrep_pcre2_rg_profile")" || fail "Missing ripgrep_oracle.pcre2_profile for $HOST_RID in tests/PREREQS.lock."
expect_equal "PCRE2 reference rg build profile" "release-lto" "$RG_PCRE2_PROFILE"

RG_PCRE2_FEATURES="$(read_oracle_value "pcre2_features" "ripgrep_pcre2_rg_features")" || fail "Missing ripgrep_oracle.pcre2_features for $HOST_RID in tests/PREREQS.lock."
expect_equal "PCRE2 reference rg features" "pcre2" "$RG_PCRE2_FEATURES"

RG_PCRE2_PATH="$(resolve_repo_path "$(read_oracle_value "pcre2_path" "ripgrep_pcre2_rg_path")")" || fail "Missing ripgrep_oracle.pcre2_path for $HOST_RID in tests/PREREQS.lock."
RG_PCRE2_SHA256="$(read_oracle_value "pcre2_sha256" "ripgrep_pcre2_rg_sha256")" || fail "Missing ripgrep_oracle.pcre2_sha256 for $HOST_RID in tests/PREREQS.lock."
check_file_hash "PCRE2 reference rg" "$RG_PCRE2_PATH" "$RG_PCRE2_SHA256"

RG_PCRE2_VERSION="$( ( "$RG_PCRE2_PATH" --version || true ) | sed -n '1p' )"
expect_equal "PCRE2 reference rg version" "ripgrep 15.1.0 (rev $RG_REV)" "$RG_PCRE2_VERSION"
RG_PCRE2_FEATURE_LINE="$( ( "$RG_PCRE2_PATH" --version || true ) | sed -n '3p' )"
expect_equal "PCRE2 reference rg feature line" "features:+pcre2" "$RG_PCRE2_FEATURE_LINE"
EXPECTED_PCRE2_VERSION="$(read_lock_value "ripgrep_pcre2_reported_version")" || fail "Missing ripgrep_pcre2_reported_version in tests/PREREQS.lock."
ACTUAL_PCRE2_VERSION="$( ( "$RG_PCRE2_PATH" --pcre2-version || true ) | sed -n '1p' )"
expect_equal "PCRE2 reference rg PCRE2 version" "$EXPECTED_PCRE2_VERSION" "$ACTUAL_PCRE2_VERSION"

if [ "$(uname -s)" = "Darwin" ]; then
    MACOS_TOOL_FAILURES=0
    check_macos_tool_hash "gzip"
    check_macos_tool_hash "bzip2"
    check_macos_tool_hash "xz"
    check_macos_tool_hash "zstd"
    check_macos_tool_hash "lz4"
    check_macos_tool_hash "brotli"
    check_macos_tool_hash "uncompress"

    HYPERFINE_PATH="$(read_lock_macos_tool_value "hyperfine" "path")" || fail "Missing macOS hyperfine path in tests/PREREQS.lock."
    HYPERFINE_VERSION="$(read_lock_macos_tool_value "hyperfine" "version")" || fail "Missing macOS hyperfine version in tests/PREREQS.lock."
    HYPERFINE_SHA256="$(read_lock_macos_tool_value "hyperfine" "sha256")" || fail "Missing macOS hyperfine hash in tests/PREREQS.lock."
    check_file_hash "macOS tool hyperfine" "$HYPERFINE_PATH" "$HYPERFINE_SHA256"
    ACTUAL_HYPERFINE_VERSION="$("$HYPERFINE_PATH" --version | sed -n '1p')"
    expect_equal "hyperfine version" "hyperfine $HYPERFINE_VERSION" "$ACTUAL_HYPERFINE_VERSION"

    if [ "$MACOS_TOOL_FAILURES" -ne 0 ]; then
        fail "One or more macOS tool hashes do not match tests/PREREQS.lock."
    fi
fi

check_pinned_path_corpora

if [ "$HAS_RIPGREP_SOURCE_CHECKOUT" -eq 1 ]; then
    compare_pinned_text_file "$REFERENCE/Cargo.lock" "$ROOT/upstream/Cargo.lock" "Pinned Cargo.lock"
fi
printf 'Scout preflight passed.\n'
