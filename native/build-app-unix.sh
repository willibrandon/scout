#!/usr/bin/env sh
set -eu

RID="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/artifacts/app/$RID"
BIN="$ROOT/artifacts/bin/$RID"
PCRE2_LIB="$ROOT/artifacts/native/pcre2/$RID/lib/libpcre2-8.a"

case "$RID" in
    osx-arm64|osx-x64)
        ;;
    *)
        printf 'RID %s is not implemented in this local app linker yet.\n' "$RID" >&2
        exit 1
        ;;
esac

"$ROOT/native/pcre2/build-unix.sh" "$RID"
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
        -Wl,-force_load,"$PCRE2_LIB" \
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
        -Wl,-force_load,"$PCRE2_LIB" \
        -lc++ -lobjc -lz \
        -framework Foundation -framework Security -framework GSS -framework CryptoKit -framework Network \
        -o "$BIN/scout"
fi

"$BIN/scout" -V > "$BIN/version.out"
printf 'ripgrep 15.1.0 (rev 4857d6fa67)\n' > "$BIN/version.expected"
cmp "$BIN/version.expected" "$BIN/version.out"
"$BIN/scout" --pcre2-version > "$BIN/pcre2-version.out"
printf 'PCRE2 10.46 is available (JIT is available)\n' > "$BIN/pcre2-version.expected"
cmp "$BIN/pcre2-version.expected" "$BIN/pcre2-version.out"
cat > "$BIN/pcre2-smoke.txt" <<'EOF'
foobar
foo
foobarfoo
EOF
"$BIN/scout" -P 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-smoke.out"
printf 'foobar\nfoobarfoo\n' > "$BIN/pcre2-smoke.expected"
cmp "$BIN/pcre2-smoke.expected" "$BIN/pcre2-smoke.out"
"$BIN/scout" -P --json 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-json.out"
grep '"type":"begin"' "$BIN/pcre2-json.out" >/dev/null
grep '"match":{"text":"foo"}' "$BIN/pcre2-json.out" >/dev/null
grep '"type":"summary"' "$BIN/pcre2-json.out" >/dev/null
printf 'foo 42\nxoyz\ncat\tdog\n' > "$BIN/pcre2-only-matching.txt"
"$BIN/scout" -P -o '.*o(?!.*\s)' "$BIN/pcre2-only-matching.txt" > "$BIN/pcre2-only-matching.out"
printf 'xo\ncat\tdo\n' > "$BIN/pcre2-only-matching.expected"
cmp "$BIN/pcre2-only-matching.expected" "$BIN/pcre2-only-matching.out"
"$BIN/scout" -P --count 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-count.out"
printf '3\n' > "$BIN/pcre2-count.expected"
cmp "$BIN/pcre2-count.expected" "$BIN/pcre2-count.out"
"$BIN/scout" -P --count-matches 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-count-matches.out"
printf '4\n' > "$BIN/pcre2-count-matches.expected"
cmp "$BIN/pcre2-count-matches.expected" "$BIN/pcre2-count-matches.out"
"$BIN/scout" -P --files-with-matches 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-with-matches.out"
printf '%s\n' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-with-matches.expected"
cmp "$BIN/pcre2-files-with-matches.expected" "$BIN/pcre2-files-with-matches.out"
"$BIN/scout" -P --files-without-match 'nomatch(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-without-match.out"
printf '%s\n' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-without-match.expected"
cmp "$BIN/pcre2-files-without-match.expected" "$BIN/pcre2-files-without-match.out"
"$BIN/scout" -P -n 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-line-number.out"
printf '1:foobar\n3:foobarfoo\n' > "$BIN/pcre2-line-number.expected"
cmp "$BIN/pcre2-line-number.expected" "$BIN/pcre2-line-number.out"
"$BIN/scout" -P --column 'bar' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-column.out"
printf '1:4:foobar\n3:4:foobarfoo\n' > "$BIN/pcre2-column.expected"
cmp "$BIN/pcre2-column.expected" "$BIN/pcre2-column.out"
"$BIN/scout" -P -H -n --column -b -o 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-fields-only-matching.out"
printf '%s:1:2:1:o\n%s:2:2:8:o\n%s:3:2:12:o\n%s:3:8:18:o\n' "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-fields-only-matching.expected"
cmp "$BIN/pcre2-fields-only-matching.expected" "$BIN/pcre2-fields-only-matching.out"
cat > "$BIN/pcre2-multiline.txt" <<'EOF'
Start
middle
thing2
EOF
"$BIN/scout" -P --multiline '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline.out"
printf 'Start\n' > "$BIN/pcre2-multiline.expected"
cmp "$BIN/pcre2-multiline.expected" "$BIN/pcre2-multiline.out"
"$BIN/scout" -P --json --multiline '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-json-multiline.out"
grep '"lines":{"text":"Start\\n"}' "$BIN/pcre2-json-multiline.out" >/dev/null
grep '"match":{"text":"Start"}' "$BIN/pcre2-json-multiline.out" >/dev/null
grep '"type":"summary"' "$BIN/pcre2-json-multiline.out" >/dev/null
"$BIN/scout" -P --multiline --files-with-matches '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline-files.out"
printf '%s\n' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline-files.expected"
cmp "$BIN/pcre2-multiline-files.expected" "$BIN/pcre2-multiline-files.out"
cat > "$BIN/pcre2-multiline-count.txt" <<'EOF'
def A;
def B;
use A;
use B;
EOF
"$BIN/scout" -P --multiline --count '(?s)def (\w+);(?=.*use \w+)' "$BIN/pcre2-multiline-count.txt" > "$BIN/pcre2-multiline-count.out"
printf '2\n' > "$BIN/pcre2-multiline-count.expected"
cmp "$BIN/pcre2-multiline-count.expected" "$BIN/pcre2-multiline-count.out"
"$BIN/scout" -P --multiline --count-matches '(?s)def (\w+);(?=.*use \w+)' "$BIN/pcre2-multiline-count.txt" > "$BIN/pcre2-multiline-count-matches.out"
printf '2\n' > "$BIN/pcre2-multiline-count-matches.expected"
cmp "$BIN/pcre2-multiline-count-matches.expected" "$BIN/pcre2-multiline-count-matches.out"
"$ROOT/native/test-pcre2-differential-unix.sh" "$RID" "$BIN/scout"
"$ROOT/native/test-invalid-utf8-differential-unix.sh" "$RID" "$BIN/scout"
for symbol in \
    _pcre2_code_free_8 \
    _pcre2_compile_8 \
    _pcre2_config_8 \
    _pcre2_get_error_message_8 \
    _pcre2_get_ovector_pointer_8 \
    _pcre2_jit_compile_8 \
    _pcre2_match_8 \
    _pcre2_match_data_create_from_pattern_8 \
    _pcre2_match_data_free_8; do
    nm -g "$BIN/scout" | grep " $symbol$" >/dev/null
done
printf 'OK %s: Scout.App native export linked with PCRE2 and smoke checks passed\n' "$RID"
