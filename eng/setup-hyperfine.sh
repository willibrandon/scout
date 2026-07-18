#!/bin/sh
set -eu

ROOT="$(CDPATH='' cd -- "$(/usr/bin/dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

require_sha256() {
    value="$1"
    label="$2"

    if [ "${#value}" -ne 64 ]; then
        fail "$label must be a 64-character lowercase SHA-256 digest."
    fi

    case "$value" in
        *[!0-9a-f]*)
            fail "$label must be a 64-character lowercase SHA-256 digest."
            ;;
    esac
}

read_pinned_hyperfine_value() {
    key="$1"

    /usr/bin/awk -v key="$key" -v host_rid="$HOST_RID" '
        function value_of(line, value) {
            value = line
            sub(/^[^=]*=[[:space:]]*/, "", value)
            sub(/^"/, "", value)
            sub(/"$/, "", value)
            return value
        }
        function finish_table() {
            if (!in_table || table_name != "hyperfine") {
                return
            }

            if (table_rid == "" && table_environment == "") {
                baseline_count++
                baseline_value = table_value
            } else if (table_rid == "" || table_rid == host_rid) {
                specific_values[++specific_count] = table_value
            }
        }
        $0 == "[[tool.macos]]" {
            finish_table()
            in_table = 1
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            finish_table()
            in_table = 0
            next
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
            finish_table()
            if (baseline_count != 1 || baseline_value == "") {
                exit 1
            }
            for (i = 1; i <= specific_count; i++) {
                if (specific_values[i] != baseline_value) {
                    exit 2
                }
            }
            print baseline_value
        }
    ' "$LOCK"
}

read_required_pin() {
    key="$1"
    label="$2"

    if value="$(read_pinned_hyperfine_value "$key")"; then
        :
    else
        status=$?
        if [ "$status" -eq 2 ]; then
            fail "Environment-specific macOS hyperfine $label does not match the environment-neutral pin."
        fi
        fail "Missing unique environment-neutral macOS hyperfine $label in tests/PREREQS.lock."
    fi

    case "$value" in
        ""|resolved@*)
            fail "macOS hyperfine $label is not frozen in tests/PREREQS.lock: $value"
            ;;
    esac

    printf '%s\n' "$value"
}

sha256_file() {
    digest_line="$(/usr/bin/shasum -a 256 "$1")"
    printf '%s\n' "${digest_line%% *}"
}

verify_hash() {
    label="$1"
    path="$2"
    expected="$3"
    actual="$(sha256_file "$path")"

    if [ "$actual" != "$expected" ]; then
        printf '%s SHA-256 mismatch:\n  expected: %s\n  actual:   %s\n' \
            "$label" "$expected" "$actual" >&2
        exit 1
    fi
}

if [ "$#" -ne 1 ]; then
    fail "Usage: eng/setup-hyperfine.sh INSTALL_ROOT"
fi

INSTALL_ROOT="$1"
case "$INSTALL_ROOT" in
    /*)
        ;;
    *)
        fail "INSTALL_ROOT must be an absolute path."
        ;;
esac

if [ "$INSTALL_ROOT" = "/" ]; then
    fail "INSTALL_ROOT must not be the filesystem root."
fi
if [ -e "$INSTALL_ROOT" ] || [ -L "$INSTALL_ROOT" ]; then
    fail "INSTALL_ROOT must not already exist: $INSTALL_ROOT"
fi

[ "$(/usr/bin/uname -s)" = "Darwin" ] || \
    fail "Pinned hyperfine provisioning is supported only on macOS."
[ "$(/usr/bin/uname -m)" = "arm64" ] || \
    fail "Pinned hyperfine provisioning is supported only on macOS arm64."
[ -f "$LOCK" ] || fail "Missing prerequisite lock: $LOCK"
HOST_RID="osx-arm64"

VERSION="$(read_required_pin "version" "version")"
BOTTLE_URL="$(read_required_pin "bottle_url" "bottle URL")"
BOTTLE_SHA256="$(read_required_pin "bottle_sha256" "bottle SHA-256")"
BINARY_SHA256="$(read_required_pin "sha256" "binary SHA-256")"

require_sha256 "$BOTTLE_SHA256" "macOS hyperfine bottle SHA-256"
require_sha256 "$BINARY_SHA256" "macOS hyperfine binary SHA-256"

case "$VERSION" in
    *[!0-9A-Za-z._+-]*|"")
        fail "macOS hyperfine version contains unsupported characters: $VERSION"
        ;;
esac

EXPECTED_BLOB_SUFFIX="/blobs/sha256:$BOTTLE_SHA256"
case "$BOTTLE_URL" in
    https://ghcr.io/v2/*"$EXPECTED_BLOB_SUFFIX")
        ;;
    *)
        fail "macOS hyperfine bottle URL must be the GHCR blob identified by its pinned SHA-256."
        ;;
esac

REPOSITORY="${BOTTLE_URL#https://ghcr.io/v2/}"
REPOSITORY="${REPOSITORY%"$EXPECTED_BLOB_SUFFIX"}"
case "$REPOSITORY" in
    ""|/*|*/|*..*|*[!0-9A-Za-z._/-]*)
        fail "macOS hyperfine bottle URL contains an unsupported GHCR repository path."
        ;;
esac

BOTTLE_ARCHIVE="$INSTALL_ROOT/hyperfine.bottle.tar.gz"
BINARY_TEMP="$INSTALL_ROOT/bin/.hyperfine.tmp"
BINARY_PATH="$INSTALL_ROOT/bin/hyperfine"
INSTALL_COMPLETE=0

cleanup() {
    /bin/rm -f "$BOTTLE_ARCHIVE" "$BINARY_TEMP"
    if [ "$INSTALL_COMPLETE" -ne 1 ]; then
        /bin/rm -f "$BINARY_PATH"
        /bin/rmdir "$INSTALL_ROOT/bin" >/dev/null 2>&1 || true
        /bin/rmdir "$INSTALL_ROOT" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM

/bin/mkdir "$INSTALL_ROOT"
/bin/mkdir "$INSTALL_ROOT/bin"

TOKEN_URL="https://ghcr.io/token?service=ghcr.io&scope=repository:$REPOSITORY:pull"
TOKEN_DOCUMENT="$(
    /usr/bin/curl \
        --proto '=https' \
        --tlsv1.2 \
        --fail \
        --silent \
        --show-error \
        --retry 3 \
        "$TOKEN_URL"
)"
TOKEN="$(
    printf '%s\n' "$TOKEN_DOCUMENT" |
        /usr/bin/sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
)"
case "$TOKEN" in
    ""|*[!0-9A-Za-z._~+/=-]*)
        fail "GHCR returned an invalid anonymous pull token for the pinned hyperfine bottle."
        ;;
esac

/usr/bin/curl \
    --proto '=https' \
    --tlsv1.2 \
    --location \
    --fail \
    --silent \
    --show-error \
    --retry 3 \
    --header "Authorization: Bearer $TOKEN" \
    --output "$BOTTLE_ARCHIVE" \
    "$BOTTLE_URL"

verify_hash "macOS hyperfine bottle" "$BOTTLE_ARCHIVE" "$BOTTLE_SHA256"

BOTTLE_MEMBER="hyperfine/$VERSION/bin/hyperfine"
/usr/bin/tar -xOf "$BOTTLE_ARCHIVE" "$BOTTLE_MEMBER" > "$BINARY_TEMP" || \
    fail "Pinned hyperfine bottle does not contain $BOTTLE_MEMBER."
/bin/chmod 0755 "$BINARY_TEMP"
verify_hash "macOS hyperfine binary" "$BINARY_TEMP" "$BINARY_SHA256"

ACTUAL_VERSION="$("$BINARY_TEMP" --version)"
if [ "$ACTUAL_VERSION" != "hyperfine $VERSION" ]; then
    fail "Expected hyperfine version hyperfine $VERSION, got $ACTUAL_VERSION"
fi

/bin/mv "$BINARY_TEMP" "$BINARY_PATH"
INSTALL_COMPLETE=1
printf '%s\n' "$BINARY_PATH"
