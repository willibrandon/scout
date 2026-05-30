#!/usr/bin/env sh
set -eu

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/spike/out/$RID"

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
else
    printf 'RID %s is not implemented in this local spike script yet.\n' "$RID" >&2
    exit 1
fi

printf '\377\n' > "$OUT/expected"
"$OUT/scout-spike" "$(printf '\377')" > "$OUT/got"
cmp "$OUT/expected" "$OUT/got"
printf 'OK %s: non-UTF-8 argv round-trip\n' "$RID"
