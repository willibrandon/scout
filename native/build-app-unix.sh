#!/usr/bin/env sh
set -eu

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/artifacts/app/$RID"
BIN="$ROOT/artifacts/bin/$RID"

dotnet publish "$ROOT/src/Scout.App/Scout.App.csproj" -r "$RID" -c Release -p:NativeLib=Static -o "$OUT"
mkdir -p "$BIN"

RT="$HOME/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.$RID/10.0.2/runtimes/$RID/native"

if [ "$RID" = "osx-arm64" ]; then
    clang "$ROOT/native/entry/scout_main.c" \
        "$OUT/scout.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.Apple.a" \
        -lc++ -lobjc -lz \
        -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
        -o "$BIN/scout"
elif [ "$RID" = "osx-x64" ]; then
    clang -arch x86_64 "$ROOT/native/entry/scout_main.c" \
        "$OUT/scout.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" "$RT/libRuntime.VxsortEnabled.a" \
        "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.Apple.a" \
        -lc++ -lobjc -lz \
        -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
        -o "$BIN/scout"
else
    printf 'RID %s is not implemented in this local app linker yet.\n' "$RID" >&2
    exit 1
fi

"$BIN/scout" -V > "$BIN/version.out"
printf 'ripgrep 15.1.0 (rev 4857d6fa67)\n' > "$BIN/version.expected"
cmp "$BIN/version.expected" "$BIN/version.out"
printf 'OK %s: Scout.App native export linked and -V matched upstream short version\n' "$RID"
