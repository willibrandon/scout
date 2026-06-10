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
            fail "Unsupported host for ripgrep oracle capture: $os $arch"
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

repo_relative_path() {
    case "$1" in
        "$ROOT"/*)
            printf '%s\n' "${1#"$ROOT"/}"
            ;;
        *)
            fail "Oracle archive members must be under the repository root: $1"
            ;;
    esac
}

create_oracle_archive() {
    command -v zip >/dev/null 2>&1 || fail "zip is required to capture the ripgrep oracle archive."

    ORACLE_ARCHIVE_VALUE="${SCOUT_RIPGREP_ARCHIVE_PATH:-tests/oracles/ripgrep/$HOST_RID.zip}"
    ORACLE_ARCHIVE="$(resolve_repo_path "$ORACLE_ARCHIVE_VALUE")"
    mkdir -p "$(dirname -- "$ORACLE_ARCHIVE")"

    rg_member="$(repo_relative_path "$RG_PATH")"
    pcre2_member="$(repo_relative_path "$RG_PCRE2_PATH")"
    TZ=UTC touch -t 198001010000 "$RG_PATH" "$RG_PCRE2_PATH"
    rm -f "$ORACLE_ARCHIVE"
    (
        cd "$ROOT"
        zip -X -q "$ORACLE_ARCHIVE" "$rg_member" "$pcre2_member"
    )
    ORACLE_ARCHIVE_SHA256="$(sha256_file "$ORACLE_ARCHIVE")"
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

ensure_rustup() {
    if ! command -v rustup >/dev/null 2>&1; then
        command -v curl >/dev/null 2>&1 || fail "curl is required to install rustup."
        rustup_installer="$(mktemp "${TMPDIR:-/tmp}/rustup.XXXXXX")"
        curl --proto '=https' --tlsv1.2 --fail --silent --show-error \
            --output "$rustup_installer" \
            https://sh.rustup.rs
        sh "$rustup_installer" -y --profile minimal --default-toolchain none
        rm -f "$rustup_installer"
    fi

    if [ -f "$HOME/.cargo/env" ]; then
        # shellcheck disable=SC1091
        . "$HOME/.cargo/env"
    fi
}

ensure_reference_checkout() {
    expected_commit="$1"

    if [ ! -d "$REFERENCE/.git" ]; then
        mkdir -p "$(dirname -- "$REFERENCE")"
        rm -rf "$REFERENCE"
        git init "$REFERENCE"
        git -C "$REFERENCE" remote add origin https://github.com/BurntSushi/ripgrep.git
    fi

    actual_commit="$(git -C "$REFERENCE" rev-parse HEAD 2>/dev/null || true)"
    if [ "$actual_commit" = "$expected_commit" ]; then
        return
    fi

    if ! git -C "$REFERENCE" fetch --depth 1 origin "$expected_commit"; then
        git -C "$REFERENCE" fetch origin
    fi
    git -C "$REFERENCE" checkout --detach "$expected_commit"
}

build_ripgrep() {
    (
        cd "$REFERENCE"
        cargo "+$RUST_TOOLCHAIN" build --profile "$RG_PROFILE" --bin rg
    )
}

build_pcre2_ripgrep() {
    (
        cd "$REFERENCE"
        CARGO_TARGET_DIR="$REFERENCE/target/pcre2" \
            PCRE2_SYS_STATIC=1 \
            cargo "+$RUST_TOOLCHAIN" build --profile "$RG_PCRE2_PROFILE" --features "$RG_PCRE2_FEATURES" --bin rg
    )
}

print_lock_row() {
    ROW_PATH_VALUE="${SCOUT_RIPGREP_ROW_PATH:-tests/oracles/ripgrep/$HOST_RID.lock}"
    ROW_PATH="$(resolve_repo_path "$ROW_PATH_VALUE")"
    mkdir -p "$(dirname -- "$ROW_PATH")"
    {
        printf '%s\n' '--- tests/PREREQS.lock ripgrep oracle row ---'
        printf '%s\n' '[[ripgrep_oracle]]'
        printf 'rid = "%s"\n' "$HOST_RID"
        printf 'environment = "%s"\n' "$HOST_ORACLE_ENVIRONMENT"
        printf 'archive_path = "%s"\n' "$ORACLE_ARCHIVE_VALUE"
        printf 'archive_sha256 = "%s"\n' "$ORACLE_ARCHIVE_SHA256"
        printf 'profile = "%s"\n' "$RG_PROFILE"
        printf 'path = "%s"\n' "$RG_PATH_VALUE"
        printf 'sha256 = "%s"\n' "$RG_SHA256"
        printf 'pcre2_profile = "%s"\n' "$RG_PCRE2_PROFILE"
        printf 'pcre2_features = "%s"\n' "$RG_PCRE2_FEATURES"
        printf 'pcre2_path = "%s"\n' "$RG_PCRE2_PATH_VALUE"
        printf 'pcre2_sha256 = "%s"\n' "$RG_PCRE2_SHA256"
        printf '%s\n' '--- end ripgrep oracle row ---'
    } | tee "$ROW_PATH"
}

EXPECTED_RIPGREP="$(read_lock_value "ripgrep_commit")" || fail "Missing ripgrep_commit in tests/PREREQS.lock."
RUST_TOOLCHAIN="$(read_lock_value "cargo")" || fail "Missing cargo in tests/PREREQS.lock."
RG_PROFILE="$(read_lock_value "ripgrep_rg_profile")" || fail "Missing ripgrep_rg_profile in tests/PREREQS.lock."
RG_PCRE2_PROFILE="$(read_lock_value "ripgrep_pcre2_rg_profile")" || fail "Missing ripgrep_pcre2_rg_profile in tests/PREREQS.lock."
RG_PCRE2_FEATURES="$(read_lock_value "ripgrep_pcre2_rg_features")" || fail "Missing ripgrep_pcre2_rg_features in tests/PREREQS.lock."
HOST_RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"

REFERENCE_VALUE="${SCOUT_RIPGREP_REFERENCE:-artifacts/ripgrep-oracle/$HOST_RID/ripgrep}"
REFERENCE="$(resolve_repo_path "$REFERENCE_VALUE")"
RG_PATH_VALUE="${SCOUT_RIPGREP_RG_PATH:-$REFERENCE_VALUE/target/$RG_PROFILE/rg}"
RG_PCRE2_PATH_VALUE="${SCOUT_RIPGREP_PCRE2_RG_PATH:-$REFERENCE_VALUE/target/pcre2/$RG_PCRE2_PROFILE/rg}"
RG_PATH="$(resolve_repo_path "$RG_PATH_VALUE")"
RG_PCRE2_PATH="$(resolve_repo_path "$RG_PCRE2_PATH_VALUE")"

ensure_rustup
rustup toolchain install "$RUST_TOOLCHAIN" --profile minimal
ACTUAL_CARGO="$(cargo "+$RUST_TOOLCHAIN" --version | awk '{ print $2 }')"
expect_equal "cargo" "$RUST_TOOLCHAIN" "$ACTUAL_CARGO"

ensure_reference_checkout "$EXPECTED_RIPGREP"
ACTUAL_RIPGREP="$(git -C "$REFERENCE" rev-parse HEAD)"
expect_equal "ripgrep commit" "$EXPECTED_RIPGREP" "$ACTUAL_RIPGREP"

build_ripgrep
[ -x "$RG_PATH" ] || fail "Missing built reference rg: $RG_PATH"
RG_SHA256="$(sha256_file "$RG_PATH")"

build_pcre2_ripgrep
[ -x "$RG_PCRE2_PATH" ] || fail "Missing built PCRE2 reference rg: $RG_PCRE2_PATH"
RG_PCRE2_SHA256="$(sha256_file "$RG_PCRE2_PATH")"

create_oracle_archive
print_lock_row
