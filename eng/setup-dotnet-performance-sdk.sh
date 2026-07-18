#!/bin/sh
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
            exit
        }
        $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            print value_of($0)
            found = 1
            exit
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_archive_value() {
    awk -v header="[[$1]]" -v rid="$2" -v key="$3" "$strip_toml_value"'
        $0 == header {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            matched = value_of($0) == rid
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            print value_of($0)
            found = 1
            exit
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
        ""|*[\$\{\}\(\)\`]*|*SCOUT_*|*GITHUB_*|*RUNNER_*|*HOME*)
            fail "$label is not frozen in tests/PREREQS.lock: $value"
            ;;
    esac
}

verify_sdk() {
    dotnet_root="$(CDPATH= cd -- "$1" && pwd -P)"
    dotnet="$dotnet_root/dotnet"

    [ -x "$dotnet" ] || fail "The pinned .NET SDK archive did not contain an executable dotnet host."

    actual_sdk="$($dotnet --version)"
    [ "$actual_sdk" = "$EXPECTED_SDK" ] ||
        fail "Pinned .NET SDK mismatch: expected $EXPECTED_SDK, found $actual_sdk."

    sdk_inventory="$($dotnet --list-sdks)"
    sdk_count="$(printf '%s\n' "$sdk_inventory" | awk 'NF { count++ } END { print count + 0 }')"
    sdk_version="$(printf '%s\n' "$sdk_inventory" | awk 'NF { print $1; exit }')"
    sdk_path="$(printf '%s\n' "$sdk_inventory" | awk 'NF {
        value = $0
        sub(/^.*\[/, "", value)
        sub(/\]$/, "", value)
        print value
        exit
    }')"
    [ "$sdk_count" = "1" ] && [ "$sdk_version" = "$EXPECTED_SDK" ] ||
        fail "The isolated .NET root must contain only SDK $EXPECTED_SDK."
    [ "$sdk_path" = "$dotnet_root/sdk" ] ||
        fail "The pinned .NET SDK inventory escaped the isolated root: ${sdk_path:-unknown}."

    dotnet_info="$($dotnet --info)"
    host_version="$(printf '%s\n' "$dotnet_info" | awk '
        $1 == "Host:" { in_host = 1; next }
        in_host && $1 == "Version:" { print $2; exit }
    ')"
    host_architecture="$(printf '%s\n' "$dotnet_info" | awk '
        $1 == "Host:" { in_host = 1; next }
        in_host && $1 == "Architecture:" { print $2; exit }
    ')"
    sdk_base_path="$(printf '%s\n' "$dotnet_info" | awk '
        $1 == "Base" && $2 == "Path:" { print $3; exit }
    ')"
    [ "$host_version" = "$EXPECTED_HOST_RUNTIME" ] ||
        fail "Pinned .NET host mismatch: expected $EXPECTED_HOST_RUNTIME, found ${host_version:-unknown}."
    [ "$host_architecture" = "arm64" ] ||
        fail "Pinned .NET host architecture mismatch: expected arm64, found ${host_architecture:-unknown}."
    [ -d "$sdk_base_path" ] ||
        fail "Pinned .NET SDK Base Path is missing: ${sdk_base_path:-unknown}."
    sdk_base_path="$(CDPATH= cd -- "$sdk_base_path" && pwd -P)"
    [ "$sdk_base_path" = "$dotnet_root/sdk/$EXPECTED_SDK" ] ||
        fail "Pinned .NET SDK Base Path escaped the isolated root: $sdk_base_path."

    runtime_inventory="$($dotnet --list-runtimes)"
    runtime_count="$(printf '%s\n' "$runtime_inventory" | awk 'NF { count++ } END { print count + 0 }')"
    runtime_name="$(printf '%s\n' "$runtime_inventory" | awk 'NF { print $1; exit }')"
    runtime_version="$(printf '%s\n' "$runtime_inventory" | awk 'NF { print $2; exit }')"
    runtime_path="$(printf '%s\n' "$runtime_inventory" | awk 'NF {
        value = $0
        sub(/^.*\[/, "", value)
        sub(/\]$/, "", value)
        print value
        exit
    }')"
    [ "$runtime_count" = "1" ] && \
        [ "$runtime_name" = "Microsoft.NETCore.App" ] && \
        [ "$runtime_version" = "$EXPECTED_HOST_RUNTIME" ] ||
        fail "The isolated .NET root must contain only Microsoft.NETCore.App $EXPECTED_HOST_RUNTIME."
    [ "$runtime_path" = "$dotnet_root/shared/Microsoft.NETCore.App" ] ||
        fail "The pinned .NET runtime inventory escaped the isolated root: ${runtime_path:-unknown}."

    hostfxr_inventory="$(/usr/bin/find "$dotnet_root/host/fxr" \
        -mindepth 1 -maxdepth 1 -type d -exec /usr/bin/basename {} \; | /usr/bin/sort)"
    [ "$hostfxr_inventory" = "$EXPECTED_HOST_RUNTIME" ] ||
        fail "The isolated .NET root must contain only hostfxr $EXPECTED_HOST_RUNTIME."
}

verify_archive_sha512() {
    archive="$1"
    expected_sha512="$2"
    label="$3"
    if [ -x /usr/bin/shasum ]; then
        actual_sha512="$(/usr/bin/shasum -a 512 "$archive" | awk '{ print $1 }')"
    elif [ -x /usr/bin/openssl ]; then
        actual_sha512="$(/usr/bin/openssl dgst -sha512 "$archive" | awk '{ print $NF }')"
    else
        fail "The pinned .NET SDK archive requires shasum or openssl for SHA-512 verification."
    fi
    [ "$actual_sha512" = "$expected_sha512" ] ||
        fail "Pinned $label archive SHA-512 mismatch: expected $expected_sha512, found $actual_sha512."
}

validate_archive_sha512() {
    value="$1"
    label="$2"

    [ "${#value}" -eq 128 ] ||
        fail "$label must be 128 lowercase hexadecimal characters."
    case "$value" in
        *[!0-9a-f]*)
            fail "$label must be 128 lowercase hexadecimal characters."
            ;;
    esac
}

[ "$#" -eq 1 ] || fail "Usage: eng/setup-dotnet-performance-sdk.sh INSTALL_ROOT"
[ "$(/usr/bin/uname -s)" = "Darwin" ] && [ "$(/usr/bin/uname -m)" = "arm64" ] ||
    fail "The pinned performance .NET SDK is defined only for macOS arm64."

INSTALL_ROOT="$1"
case "$INSTALL_ROOT" in
    /*)
        ;;
    *)
        fail "The pinned performance .NET SDK install root must be absolute."
        ;;
esac
[ ! -e "$INSTALL_ROOT" ] && [ ! -L "$INSTALL_ROOT" ] ||
    fail "The pinned performance .NET SDK install root must not already exist: $INSTALL_ROOT"

EXPECTED_SDK="$(read_lock_value dotnet_sdk)" || fail "Missing dotnet_sdk in tests/PREREQS.lock."
EXPECTED_HOST_RUNTIME="$(read_lock_value dotnet_host_runtime)" || fail "Missing dotnet_host_runtime in tests/PREREQS.lock."
SDK_ARCHIVE_URL="$(read_archive_value dotnet_sdk_archive osx-arm64 url)" || fail "Missing osx-arm64 .NET SDK archive URL in tests/PREREQS.lock."
SDK_ARCHIVE_SHA512="$(read_archive_value dotnet_sdk_archive osx-arm64 sha512)" || fail "Missing osx-arm64 .NET SDK archive SHA-512 in tests/PREREQS.lock."
RUNTIME_ARCHIVE_URL="$(read_archive_value dotnet_runtime_archive osx-arm64 url)" || fail "Missing osx-arm64 .NET runtime archive URL in tests/PREREQS.lock."
RUNTIME_ARCHIVE_SHA512="$(read_archive_value dotnet_runtime_archive osx-arm64 sha512)" || fail "Missing osx-arm64 .NET runtime archive SHA-512 in tests/PREREQS.lock."

require_literal "$EXPECTED_SDK" "dotnet_sdk"
require_literal "$EXPECTED_HOST_RUNTIME" "dotnet_host_runtime"
require_literal "$SDK_ARCHIVE_URL" "osx-arm64 .NET SDK archive URL"
require_literal "$SDK_ARCHIVE_SHA512" "osx-arm64 .NET SDK archive SHA-512"
require_literal "$RUNTIME_ARCHIVE_URL" "osx-arm64 .NET runtime archive URL"
require_literal "$RUNTIME_ARCHIVE_SHA512" "osx-arm64 .NET runtime archive SHA-512"
EXPECTED_ARCHIVE_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/$EXPECTED_SDK/dotnet-sdk-$EXPECTED_SDK-osx-arm64.tar.gz"
[ "$SDK_ARCHIVE_URL" = "$EXPECTED_ARCHIVE_URL" ] ||
    fail "The osx-arm64 .NET SDK archive URL does not match the pinned SDK version."
EXPECTED_RUNTIME_ARCHIVE_URL="https://builds.dotnet.microsoft.com/dotnet/Runtime/$EXPECTED_HOST_RUNTIME/dotnet-runtime-$EXPECTED_HOST_RUNTIME-osx-arm64.tar.gz"
[ "$RUNTIME_ARCHIVE_URL" = "$EXPECTED_RUNTIME_ARCHIVE_URL" ] ||
    fail "The osx-arm64 .NET runtime archive URL does not match the pinned host runtime version."
validate_archive_sha512 "$SDK_ARCHIVE_SHA512" "The osx-arm64 .NET SDK archive SHA-512"
validate_archive_sha512 "$RUNTIME_ARCHIVE_SHA512" "The osx-arm64 .NET runtime archive SHA-512"

INSTALL_PARENT="$(/usr/bin/dirname -- "$INSTALL_ROOT")"
mkdir -p "$INSTALL_PARENT"
INSTALL_PARENT="$(CDPATH= cd -- "$INSTALL_PARENT" && pwd)"
WORK_DIRECTORY="$(mktemp -d "${INSTALL_PARENT%/}/.dotnet-performance-sdk.XXXXXX")"
SDK_ARCHIVE="$WORK_DIRECTORY/dotnet-sdk.tar.gz"
RUNTIME_ARCHIVE="$WORK_DIRECTORY/dotnet-runtime.tar.gz"
EXTRACTED_ROOT="$WORK_DIRECTORY/root"
trap 'rm -rf -- "$WORK_DIRECTORY"' EXIT HUP INT TERM

for archive_specification in \
    "$SDK_ARCHIVE|$SDK_ARCHIVE_URL|$SDK_ARCHIVE_SHA512|.NET SDK" \
    "$RUNTIME_ARCHIVE|$RUNTIME_ARCHIVE_URL|$RUNTIME_ARCHIVE_SHA512|.NET runtime"; do
    IFS='|' read -r archive archive_url archive_sha512 archive_label <<EOF
$archive_specification
EOF
    /usr/bin/curl \
        --fail \
        --location \
        --retry 3 \
        --retry-all-errors \
        --silent \
        --show-error \
        --output "$archive" \
        "$archive_url"
    verify_archive_sha512 "$archive" "$archive_sha512" "$archive_label"
done

mkdir "$EXTRACTED_ROOT"
/usr/bin/tar -xzf "$SDK_ARCHIVE" -C "$EXTRACTED_ROOT"
rm -rf -- "$EXTRACTED_ROOT/host/fxr" "$EXTRACTED_ROOT/shared"
/usr/bin/tar -xzf "$RUNTIME_ARCHIVE" -C "$EXTRACTED_ROOT"
verify_sdk "$EXTRACTED_ROOT"
mv "$EXTRACTED_ROOT" "$INSTALL_ROOT"
verify_sdk "$INSTALL_ROOT"

printf 'OK pinned .NET SDK %s with host runtime %s is ready at %s\n' \
    "$EXPECTED_SDK" "$EXPECTED_HOST_RUNTIME" "$INSTALL_ROOT"
