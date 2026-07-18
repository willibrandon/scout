#!/bin/sh

read_native_prerequisite_value() {
    native_prerequisite_root="$1"
    native_prerequisite_key="$2"

    awk -v key="$native_prerequisite_key" '
        /^\[/ {
            exit 1
        }
        $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            value = substr($0, index($0, "=") + 1)
            sub(/^[[:space:]]*"/, "", value)
            sub(/"[[:space:]]*$/, "", value)
            print value
            found = 1
            exit 0
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$native_prerequisite_root/tests/PREREQS.lock"
}

native_sha256_file() {
    native_sha256_path="$1"

    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 < "$native_sha256_path" | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum < "$native_sha256_path" | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r < "$native_sha256_path" | awk '{ print $1 }'
    else
        printf 'No SHA-256 tool found.\n' >&2
        return 1
    fi
}

native_expect_equal() {
    native_equal_label="$1"
    native_equal_expected="$2"
    native_equal_actual="$3"

    if [ "$native_equal_actual" != "$native_equal_expected" ]; then
        printf 'Expected %s %s, got %s\n' \
            "$native_equal_label" "$native_equal_expected" "$native_equal_actual" >&2
        return 1
    fi
}

native_xcode_identity() {
    native_xcode_developer_dir="$1"

    DEVELOPER_DIR="$native_xcode_developer_dir" /usr/bin/xcodebuild -version
}

select_native_xcode_developer_dir() {
    native_xcode_version="$1"
    native_xcode_build="$2"
    native_xcode_applications="${3:-/Applications}"

    for native_xcode_candidate in \
        "$native_xcode_applications/Xcode_${native_xcode_version}.app/Contents/Developer" \
        "$native_xcode_applications/Xcode_${native_xcode_version}.0.app/Contents/Developer" \
        "$native_xcode_applications/Xcode.app/Contents/Developer"; do
        [ -d "$native_xcode_candidate" ] || continue
        native_xcode_identity_value="$(native_xcode_identity "$native_xcode_candidate" 2>/dev/null || true)"
        native_xcode_actual_version="$(printf '%s\n' "$native_xcode_identity_value" | sed -n 's/^Xcode //p')"
        native_xcode_actual_build="$(printf '%s\n' "$native_xcode_identity_value" | sed -n 's/^Build version //p')"
        if [ "$native_xcode_actual_version" = "$native_xcode_version" ] &&
            [ "$native_xcode_actual_build" = "$native_xcode_build" ]; then
            printf '%s\n' "$native_xcode_candidate"
            return 0
        fi
    done

    printf 'Xcode %s build %s is required.\n' \
        "$native_xcode_version" "$native_xcode_build" >&2
    return 1
}

configure_native_toolchain() {
    native_toolchain_root="$1"
    native_toolchain_rid="$2"

    case "$native_toolchain_rid" in
        osx-arm64|osx-x64)
            NATIVE_XCODE_VERSION="$(read_native_prerequisite_value "$native_toolchain_root" xcode_version)" || return 1
            NATIVE_XCODE_BUILD="$(read_native_prerequisite_value "$native_toolchain_root" xcode_build)" || return 1
            NATIVE_MACOS_SDK_VERSION="$(read_native_prerequisite_value "$native_toolchain_root" macos_sdk)" || return 1
            NATIVE_MACOS_DEPLOYMENT_TARGET="$(read_native_prerequisite_value "$native_toolchain_root" macos_deployment_target)" || return 1
            NATIVE_APPLE_CLANG_VERSION="$(read_native_prerequisite_value "$native_toolchain_root" apple_clang)" || return 1
            NATIVE_APPLE_LD_VERSION="$(read_native_prerequisite_value "$native_toolchain_root" apple_ld)" || return 1

            NATIVE_DEVELOPER_DIR="$(select_native_xcode_developer_dir "$NATIVE_XCODE_VERSION" "$NATIVE_XCODE_BUILD")" || return 1
            export DEVELOPER_DIR="$NATIVE_DEVELOPER_DIR"
            NATIVE_SDKROOT="$(/usr/bin/xcrun --sdk macosx --show-sdk-path)" || return 1
            native_actual_sdk_version="$(/usr/bin/xcrun --sdk macosx --show-sdk-version)" || return 1
            native_expect_equal "macOS SDK" "$NATIVE_MACOS_SDK_VERSION" "$native_actual_sdk_version" || return 1

            NATIVE_CC="$(/usr/bin/xcrun --find clang)" || return 1
            NATIVE_LD="$(/usr/bin/xcrun --find ld)" || return 1
            NATIVE_AR="$(/usr/bin/xcrun --find ar)" || return 1
            NATIVE_RANLIB="$(/usr/bin/xcrun --find ranlib)" || return 1
            NATIVE_STRIP="$(/usr/bin/xcrun --find strip)" || return 1
            NATIVE_NM="$(/usr/bin/xcrun --find nm)" || return 1
            NATIVE_TOOLCHAIN_BIN="$(CDPATH= cd -- "$(/usr/bin/dirname -- "$NATIVE_CC")" && pwd)"

            native_actual_clang_version="$(native_compiler_version "$NATIVE_CC" | sed -n 's/^Apple clang version //p')"
            native_actual_ld_version="$(native_linker_version "$NATIVE_LD")"
            native_expect_equal "Apple Clang" "$NATIVE_APPLE_CLANG_VERSION" "$native_actual_clang_version" || return 1
            native_expect_equal "Apple ld" "$NATIVE_APPLE_LD_VERSION" "$native_actual_ld_version" || return 1

            for native_tool_name in clang ld ar ranlib strip nm; do
                case "$native_tool_name" in
                    clang) native_tool_path="$NATIVE_CC" ;;
                    ld) native_tool_path="$NATIVE_LD" ;;
                    ar) native_tool_path="$NATIVE_AR" ;;
                    ranlib) native_tool_path="$NATIVE_RANLIB" ;;
                    strip) native_tool_path="$NATIVE_STRIP" ;;
                    nm) native_tool_path="$NATIVE_NM" ;;
                esac
                native_expected_tool_sha256="$(read_native_prerequisite_value \
                    "$native_toolchain_root" "apple_${native_tool_name}_sha256")" || return 1
                native_actual_tool_sha256="$(native_sha256_file "$native_tool_path")" || return 1
                native_expect_equal \
                    "Apple $native_tool_name SHA-256" \
                    "$native_expected_tool_sha256" \
                    "$native_actual_tool_sha256" || return 1
            done

            CC="$NATIVE_CC"
            LD="$NATIVE_LD"
            AR="$NATIVE_AR"
            RANLIB="$NATIVE_RANLIB"
            STRIP="$NATIVE_STRIP"
            NM="$NATIVE_NM"
            SDKROOT="$NATIVE_SDKROOT"
            MACOSX_DEPLOYMENT_TARGET="$NATIVE_MACOS_DEPLOYMENT_TARGET"
            PATH="$NATIVE_TOOLCHAIN_BIN:$PATH"
            export CC LD AR RANLIB STRIP NM SDKROOT MACOSX_DEPLOYMENT_TARGET PATH
            ;;
        linux-x64|linux-arm64)
            NATIVE_XCODE_VERSION=""
            NATIVE_XCODE_BUILD=""
            NATIVE_MACOS_SDK_VERSION=""
            NATIVE_MACOS_DEPLOYMENT_TARGET=""
            NATIVE_DEVELOPER_DIR=""
            NATIVE_SDKROOT=""
            NATIVE_APPLE_CLANG_VERSION=""
            NATIVE_APPLE_LD_VERSION=""
            NATIVE_TOOLCHAIN_BIN=""
            NATIVE_CC="${CC:-cc}"
            NATIVE_LD="${LD:-ld}"
            NATIVE_AR="${AR:-ar}"
            NATIVE_RANLIB="${RANLIB:-ranlib}"
            NATIVE_STRIP="${STRIP:-strip}"
            NATIVE_NM="${NM:-nm}"
            CC="$NATIVE_CC"
            LD="$NATIVE_LD"
            AR="$NATIVE_AR"
            RANLIB="$NATIVE_RANLIB"
            STRIP="$NATIVE_STRIP"
            NM="$NATIVE_NM"
            export CC LD AR RANLIB STRIP NM
            ;;
        *)
            printf 'RID %s is not supported by this Unix native toolchain.\n' \
                "$native_toolchain_rid" >&2
            return 1
            ;;
    esac
}

native_compiler_version() {
    compiler="$1"
    "$compiler" --version | sed -n '1p'
}

native_linker_version() {
    linker="$1"
    "$linker" -v 2>&1 | sed -n '1p'
}
