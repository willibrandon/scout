#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
    printf 'usage: %s <osx-arm64|osx-x64|linux-x64|linux-arm64>\n' "$0" >&2
    exit 2
fi

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/spike/out/$RID"

case "$RID" in
    osx-arm64|osx-x64|linux-x64|linux-arm64)
        ;;
    *)
        printf 'RID %s is not supported by this Unix spike linker.\n' "$RID" >&2
        exit 1
        ;;
esac

dotnet publish "$ROOT/spike/Scout.Entry" -r "$RID" -c Release -p:NativeLib=Static -o "$OUT"

RT="$HOME/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.$RID/10.0.2/runtimes/$RID/native"

if [ "$RID" = "osx-arm64" ]; then
    clang "$ROOT/spike/native/scout_main.c" \
        "$OUT/Scout.Entry.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.Apple.a" \
        -lc++ -lobjc -lz \
        -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
        -o "$OUT/scout-spike"
elif [ "$RID" = "osx-x64" ]; then
    clang -arch x86_64 "$ROOT/spike/native/scout_main.c" \
        "$OUT/Scout.Entry.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" "$RT/libRuntime.VxsortEnabled.a" \
        "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.Apple.a" \
        -lc++ -lobjc -lz \
        -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
        -o "$OUT/scout-spike"
elif [ "$RID" = "linux-x64" ] || [ "$RID" = "linux-arm64" ]; then
    CC="${CC:-cc}"
    VXSORT_ARCHIVE=
    if [ -f "$RT/libRuntime.VxsortEnabled.a" ]; then
        VXSORT_ARCHIVE="$RT/libRuntime.VxsortEnabled.a"
    fi

    "$CC" "$ROOT/spike/native/scout_main.c" \
        -Wl,--start-group \
        "$OUT/Scout.Entry.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" ${VXSORT_ARCHIVE:+"$VXSORT_ARCHIVE"} \
        "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.OpenSsl.a" \
        -Wl,--end-group \
        -lstdc++ -lz -lpthread -ldl -lm -lrt \
        -o "$OUT/scout-spike"
else
    printf 'internal error: unhandled RID %s.\n' "$RID" >&2
    exit 2
fi

printf '\377\n' > "$OUT/expected"
"$OUT/scout-spike" "$(printf '\377')" > "$OUT/got"
cmp "$OUT/expected" "$OUT/got"
printf 'OK %s: non-UTF-8 argv round-trip\n' "$RID"
