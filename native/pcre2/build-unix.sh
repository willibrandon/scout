#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
    printf 'usage: %s <rid>\n' "$0" >&2
    exit 2
fi

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
SRC="$ROOT/native/pcre2/pcre2-10.46"
OUT="$ROOT/artifacts/native/pcre2/$RID"
OBJ="$OUT/obj"
LIB="$OUT/lib"
INCLUDE="$OUT/include"

case "$RID" in
    osx-arm64)
        : "${CC:?The macOS PCRE2 build requires the selected compiler.}"
        : "${AR:?The macOS PCRE2 build requires the selected archiver.}"
        : "${RANLIB:?The macOS PCRE2 build requires the selected archive indexer.}"
        : "${SDKROOT:?The macOS PCRE2 build requires the selected SDK.}"
        : "${MACOSX_DEPLOYMENT_TARGET:?The macOS PCRE2 build requires a deployment target.}"
        set -- \
            -arch arm64 \
            -isysroot "$SDKROOT" \
            "-mmacosx-version-min=$MACOSX_DEPLOYMENT_TARGET"
        ;;
    osx-x64)
        : "${CC:?The macOS PCRE2 build requires the selected compiler.}"
        : "${AR:?The macOS PCRE2 build requires the selected archiver.}"
        : "${RANLIB:?The macOS PCRE2 build requires the selected archive indexer.}"
        : "${SDKROOT:?The macOS PCRE2 build requires the selected SDK.}"
        : "${MACOSX_DEPLOYMENT_TARGET:?The macOS PCRE2 build requires a deployment target.}"
        set -- \
            -arch x86_64 \
            -isysroot "$SDKROOT" \
            "-mmacosx-version-min=$MACOSX_DEPLOYMENT_TARGET"
        ;;
    linux-x64|linux-arm64)
        CC="${CC:-cc}"
        AR="${AR:-ar}"
        RANLIB="${RANLIB:-ranlib}"
        set --
        ;;
    *)
        printf 'RID %s is not supported by this PCRE2 build script.\n' "$RID" >&2
        exit 1
        ;;
esac

rm -rf "$OUT"
mkdir -p "$OBJ" "$LIB" "$INCLUDE"

for source in "$SRC"/src/*.c; do
    name="$(basename "$source")"
    case "$name" in
        pcre2_jit_match.c|pcre2_jit_misc.c|pcre2_ucptables.c)
            continue
            ;;
    esac

    "$CC" "$@" \
        -O2 \
        -fPIC \
        -DPCRE2_CODE_UNIT_WIDTH=8 \
        -DHAVE_STDLIB_H=1 \
        -DHAVE_MEMMOVE=1 \
        -DHAVE_CONFIG_H=1 \
        -DPCRE2_STATIC=1 \
        -DSTDC_HEADERS=1 \
        -DSUPPORT_PCRE2_8=1 \
        -DSUPPORT_UNICODE=1 \
        -DSUPPORT_JIT=1 \
        -I "$SRC/src" \
        -I "$SRC/deps" \
        -I "$SRC/include" \
        ${CFLAGS:-} \
        -c "$source" \
        -o "$OBJ/${name%.c}.o"
done

ZERO_AR_DATE=1 "$AR" crs "$LIB/libpcre2-8.a" "$OBJ"/*.o
"$RANLIB" "$LIB/libpcre2-8.a"
cp "$SRC/include/pcre2.h" "$INCLUDE/pcre2.h"

printf 'built %s\n' "$LIB/libpcre2-8.a"
