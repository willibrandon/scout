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

ensure_directory() {
    directory="$1"
    if mkdir -p "$directory" 2>/dev/null; then
        return
    fi

    command -v sudo >/dev/null 2>&1 || fail "Could not create $directory and sudo is unavailable."
    sudo mkdir -p "$directory"
    sudo chown -R "$(id -u):$(id -g)" "$(dirname -- "$directory")"
}

ensure_rustup() {
    if command -v rustup >/dev/null 2>&1; then
        return
    fi

    command -v curl >/dev/null 2>&1 || fail "curl is required to install rustup."
    rustup_installer="$(mktemp "${TMPDIR:-/tmp}/rustup.XXXXXX")"
    curl --proto '=https' --tlsv1.2 --fail --silent --show-error \
        --output "$rustup_installer" \
        https://sh.rustup.rs
    sh "$rustup_installer" -y --profile minimal --default-toolchain none
    rm -f "$rustup_installer"
    # shellcheck disable=SC1091
    . "$HOME/.cargo/env"
}

ensure_reference_checkout() {
    expected_commit="$1"

    if [ ! -d "$REFERENCE/.git" ]; then
        ensure_directory "$(dirname -- "$REFERENCE")"
        rm -rf "$REFERENCE"
        git init "$REFERENCE"
        git -C "$REFERENCE" remote add origin https://github.com/BurntSushi/ripgrep.git
    fi

    actual_commit="$(git -C "$REFERENCE" rev-parse HEAD 2>/dev/null || true)"
    if [ "$actual_commit" = "$expected_commit" ]; then
        return
    fi

    if [ -n "$actual_commit" ] && [ "${CI:-false}" != "true" ]; then
        fail "$REFERENCE is at $actual_commit, expected $expected_commit. Move or update the reference checkout explicitly before running this setup locally."
    fi

    if ! git -C "$REFERENCE" fetch --depth 1 origin "$expected_commit"; then
        git -C "$REFERENCE" fetch origin
    fi
    git -C "$REFERENCE" checkout --detach "$expected_commit"
}

hash_matches() {
    path="$1"
    expected_sha256="$2"
    [ -x "$path" ] || return 1
    actual_sha256="$(sha256_file "$path")"
    [ "$actual_sha256" = "$expected_sha256" ]
}

verify_binary_hash() {
    label="$1"
    path="$2"
    expected_sha256="$3"

    [ -x "$path" ] || fail "Missing executable $label: $path"
    actual_sha256="$(sha256_file "$path")"
    expect_equal "$label sha256" "$expected_sha256" "$actual_sha256"
}

EXPECTED_RIPGREP="$(read_lock_value "ripgrep_commit")" || fail "Missing ripgrep_commit in tests/PREREQS.lock."
RUST_TOOLCHAIN="$(read_lock_value "cargo")" || fail "Missing cargo in tests/PREREQS.lock."
RG_PROFILE="$(read_lock_value "ripgrep_rg_profile")" || fail "Missing ripgrep_rg_profile in tests/PREREQS.lock."
RG_PATH="$(read_lock_value "ripgrep_rg_path")" || fail "Missing ripgrep_rg_path in tests/PREREQS.lock."
RG_SHA256="$(read_lock_value "ripgrep_rg_sha256")" || fail "Missing ripgrep_rg_sha256 in tests/PREREQS.lock."
RG_PCRE2_PROFILE="$(read_lock_value "ripgrep_pcre2_rg_profile")" || fail "Missing ripgrep_pcre2_rg_profile in tests/PREREQS.lock."
RG_PCRE2_FEATURES="$(read_lock_value "ripgrep_pcre2_rg_features")" || fail "Missing ripgrep_pcre2_rg_features in tests/PREREQS.lock."
RG_PCRE2_PATH="$(read_lock_value "ripgrep_pcre2_rg_path")" || fail "Missing ripgrep_pcre2_rg_path in tests/PREREQS.lock."
RG_PCRE2_SHA256="$(read_lock_value "ripgrep_pcre2_rg_sha256")" || fail "Missing ripgrep_pcre2_rg_sha256 in tests/PREREQS.lock."

ensure_rustup
rustup toolchain install "$RUST_TOOLCHAIN" --profile minimal
ACTUAL_CARGO="$(cargo "+$RUST_TOOLCHAIN" --version | awk '{ print $2 }')"
expect_equal "cargo" "$RUST_TOOLCHAIN" "$ACTUAL_CARGO"

ensure_reference_checkout "$EXPECTED_RIPGREP"
ACTUAL_RIPGREP="$(git -C "$REFERENCE" rev-parse HEAD)"
expect_equal "ripgrep commit" "$EXPECTED_RIPGREP" "$ACTUAL_RIPGREP"

if ! hash_matches "$RG_PATH" "$RG_SHA256"; then
    (
        cd "$REFERENCE"
        cargo "+$RUST_TOOLCHAIN" build --profile "$RG_PROFILE" --bin rg
    )
fi
verify_binary_hash "reference rg" "$RG_PATH" "$RG_SHA256"

if ! hash_matches "$RG_PCRE2_PATH" "$RG_PCRE2_SHA256"; then
    (
        cd "$REFERENCE"
        CARGO_TARGET_DIR="$REFERENCE/target/pcre2" \
            PCRE2_SYS_STATIC=1 \
            cargo "+$RUST_TOOLCHAIN" build --profile "$RG_PCRE2_PROFILE" --features "$RG_PCRE2_FEATURES" --bin rg
    )
fi
verify_binary_hash "PCRE2 reference rg" "$RG_PCRE2_PATH" "$RG_PCRE2_SHA256"

printf 'OK pinned ripgrep oracle is ready at %s\n' "$REFERENCE"
