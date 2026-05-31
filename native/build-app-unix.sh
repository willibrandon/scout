#!/usr/bin/env bash
set -Eeuo pipefail

on_error() {
    status=$?
    line="${BASH_LINENO[0]}"
    command="$BASH_COMMAND"
    printf 'FAILED %s:%s: %s (exit %s)\n' "$0" "$line" "$command" "$status" >&2
}
trap on_error ERR

expect_equal_file() {
    description="$1"
    expected="$2"
    actual="$3"

    if cmp -s "$expected" "$actual"; then
        return 0
    fi

    printf 'Unexpected %s output.\nExpected:\n' "$description" >&2
    sed -n l "$expected" >&2
    printf 'Actual:\n' >&2
    sed -n l "$actual" >&2
    return 1
}

expect_contains() {
    description="$1"
    pattern="$2"
    path="$3"

    if grep "$pattern" "$path" >/dev/null; then
        return 0
    fi

    printf 'Missing %s in %s.\nPattern: %s\nActual:\n' "$description" "$path" "$pattern" >&2
    sed -n l "$path" >&2
    return 1
}

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
    printf 'usage: %s <rid> [--with-differentials|--smoke-only]\n' "$0" >&2
    exit 2
fi

RID="$1"
DIFFERENTIAL_MODE="${2:---with-differentials}"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/artifacts/app/$RID"
BIN="$ROOT/artifacts/bin/$RID"
PCRE2_LIB="$ROOT/artifacts/native/pcre2/$RID/lib/libpcre2-8.a"

cd "$ROOT"

case "$DIFFERENTIAL_MODE" in
    --with-differentials|--smoke-only)
        ;;
    *)
        printf 'Unknown differential mode %s.\n' "$DIFFERENTIAL_MODE" >&2
        printf 'usage: %s <rid> [--with-differentials|--smoke-only]\n' "$0" >&2
        exit 2
        ;;
esac

case "$RID" in
    osx-arm64|osx-x64|linux-x64|linux-arm64)
        ;;
    *)
        printf 'RID %s is not supported by this Unix app linker.\n' "$RID" >&2
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
elif [ "$RID" = "linux-x64" ] || [ "$RID" = "linux-arm64" ]; then
    CC="${CC:-cc}"
    VXSORT_ARCHIVE=
    if [ -f "$RT/libRuntime.VxsortEnabled.a" ]; then
        VXSORT_ARCHIVE="$RT/libRuntime.VxsortEnabled.a"
    fi

    "$CC" "$ROOT/native/entry/scout_main.c" \
        -Wl,--start-group \
        "$OUT/scout.a" \
        "$RT/libbootstrapperdll.o" "$RT/libaotminipal.a" \
        "$RT/libRuntime.WorkstationGC.a" ${VXSORT_ARCHIVE:+"$VXSORT_ARCHIVE"} \
        "$RT/libeventpipe-disabled.a" "$RT/libstandalonegc-disabled.a" \
        "$RT/libSystem.Native.a" "$RT/libSystem.Globalization.Native.a" \
        "$RT/libSystem.IO.Compression.Native.a" "$RT/libSystem.Net.Security.Native.a" \
        "$RT/libSystem.Security.Cryptography.Native.OpenSsl.a" \
        -Wl,--whole-archive "$PCRE2_LIB" -Wl,--no-whole-archive \
        -Wl,--end-group \
        -lstdc++ -lz -lpthread -ldl -lm -lrt \
        -o "$BIN/scout"
fi

"$BIN/scout" -V > "$BIN/version.out"
printf 'ripgrep 15.1.0 (rev 4857d6fa67)\n' > "$BIN/version.expected"
expect_equal_file "scout -V" "$BIN/version.expected" "$BIN/version.out"
"$BIN/scout" --pcre2-version > "$BIN/pcre2-version.out"
printf 'PCRE2 10.46 is available (JIT is available)\n' > "$BIN/pcre2-version.expected"
expect_equal_file "scout --pcre2-version" "$BIN/pcre2-version.expected" "$BIN/pcre2-version.out"
cat > "$BIN/pcre2-smoke.txt" <<'EOF'
foobar
foo
foobarfoo
EOF
"$BIN/scout" -P 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-smoke.out"
printf 'foobar\nfoobarfoo\n' > "$BIN/pcre2-smoke.expected"
expect_equal_file "PCRE2 lookahead smoke" "$BIN/pcre2-smoke.expected" "$BIN/pcre2-smoke.out"
"$BIN/scout" -P --json 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-json.out"
expect_contains "JSON begin event" '"type":"begin"' "$BIN/pcre2-json.out"
expect_contains "JSON PCRE2 match text" '"match":{"text":"foo"}' "$BIN/pcre2-json.out"
expect_contains "JSON summary event" '"type":"summary"' "$BIN/pcre2-json.out"
"$BIN/scout" -P --json -o 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-json-only-matching.out"
expect_contains "JSON only-matching begin event" '"type":"begin"' "$BIN/pcre2-json-only-matching.out"
expect_contains "JSON only-matching PCRE2 match text" '"match":{"text":"foo"}' "$BIN/pcre2-json-only-matching.out"
expect_contains "JSON only-matching summary event" '"type":"summary"' "$BIN/pcre2-json-only-matching.out"
printf 'foo 42\nxoyz\ncat\tdog\n' > "$BIN/pcre2-only-matching.txt"
"$BIN/scout" -P -o '.*o(?!.*\s)' "$BIN/pcre2-only-matching.txt" > "$BIN/pcre2-only-matching.out"
printf 'xo\ncat\tdo\n' > "$BIN/pcre2-only-matching.expected"
expect_equal_file "PCRE2 only-matching lookahead" "$BIN/pcre2-only-matching.expected" "$BIN/pcre2-only-matching.out"
"$BIN/scout" -P --count 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-count.out"
printf '3\n' > "$BIN/pcre2-count.expected"
expect_equal_file "PCRE2 count" "$BIN/pcre2-count.expected" "$BIN/pcre2-count.out"
"$BIN/scout" -P --count-matches 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-count-matches.out"
printf '4\n' > "$BIN/pcre2-count-matches.expected"
expect_equal_file "PCRE2 count-matches" "$BIN/pcre2-count-matches.expected" "$BIN/pcre2-count-matches.out"
mkdir -p "$BIN/explicit-cwd"
printf 'needle\n' > "$BIN/explicit-cwd/file"
(cd "$BIN/explicit-cwd" && "$BIN/scout" needle . > "$BIN/explicit-cwd.out")
printf './file:needle\n' > "$BIN/explicit-cwd.expected"
expect_equal_file "explicit current directory search" "$BIN/explicit-cwd.expected" "$BIN/explicit-cwd.out"
"$BIN/scout" -P --files-with-matches 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-with-matches.out"
printf '%s\n' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-with-matches.expected"
expect_equal_file "PCRE2 files-with-matches" "$BIN/pcre2-files-with-matches.expected" "$BIN/pcre2-files-with-matches.out"
"$BIN/scout" -P --files-without-match 'nomatch(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-without-match.out"
printf '%s\n' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-files-without-match.expected"
expect_equal_file "PCRE2 files-without-match" "$BIN/pcre2-files-without-match.expected" "$BIN/pcre2-files-without-match.out"
"$BIN/scout" -P -n 'foo(?=bar)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-line-number.out"
printf '1:foobar\n3:foobarfoo\n' > "$BIN/pcre2-line-number.expected"
expect_equal_file "PCRE2 line-number" "$BIN/pcre2-line-number.expected" "$BIN/pcre2-line-number.out"
cat > "$BIN/pcre2-context.txt" <<'EOF'
one
foobar
middle
foo
last
EOF
"$BIN/scout" -P -n -C1 'foo(?=bar)' "$BIN/pcre2-context.txt" > "$BIN/pcre2-context.out"
printf '1-one\n2:foobar\n3-middle\n' > "$BIN/pcre2-context.expected"
expect_equal_file "PCRE2 context" "$BIN/pcre2-context.expected" "$BIN/pcre2-context.out"
"$BIN/scout" -P -n --passthru 'foo(?=bar)' "$BIN/pcre2-context.txt" > "$BIN/pcre2-passthru.out"
printf '1-one\n2:foobar\n3-middle\n4-foo\n5-last\n' > "$BIN/pcre2-passthru.expected"
expect_equal_file "PCRE2 passthru" "$BIN/pcre2-passthru.expected" "$BIN/pcre2-passthru.out"
"$BIN/scout" -P -n -o -C1 'foo(?=bar)' "$BIN/pcre2-context.txt" > "$BIN/pcre2-context-only-matching.out"
printf '1-one\n2:foo\n3-middle\n' > "$BIN/pcre2-context-only-matching.expected"
expect_equal_file "PCRE2 context only-matching" "$BIN/pcre2-context-only-matching.expected" "$BIN/pcre2-context-only-matching.out"
"$BIN/scout" -P -n -r X -C1 'foo(?=bar)' "$BIN/pcre2-context.txt" > "$BIN/pcre2-context-replacement.out"
printf '1-one\n2:Xbar\n3-middle\n' > "$BIN/pcre2-context-replacement.expected"
expect_equal_file "PCRE2 context replacement" "$BIN/pcre2-context-replacement.expected" "$BIN/pcre2-context-replacement.out"
printf 'barfoo\nfoobar\n' > "$BIN/pcre2-smoke-2.txt"
"$BIN/scout" -P -n 'foo(?=bar)' "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke-2.txt" > "$BIN/pcre2-multi-file.out"
printf '%s:1:foobar\n%s:3:foobarfoo\n%s:2:foobar\n' "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke-2.txt" > "$BIN/pcre2-multi-file.expected"
expect_equal_file "PCRE2 multi-file prefixes" "$BIN/pcre2-multi-file.expected" "$BIN/pcre2-multi-file.out"
printf 'foobar\nfoo\nfoobarfoo\n' | "$BIN/scout" -P 'foo(?=bar)' - > "$BIN/pcre2-stdin.out"
printf 'foobar\nfoobarfoo\n' > "$BIN/pcre2-stdin.expected"
expect_equal_file "PCRE2 explicit stdin" "$BIN/pcre2-stdin.expected" "$BIN/pcre2-stdin.out"
"$BIN/scout" -P --column 'bar' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-column.out"
printf '1:4:foobar\n3:4:foobarfoo\n' > "$BIN/pcre2-column.expected"
expect_equal_file "PCRE2 column" "$BIN/pcre2-column.expected" "$BIN/pcre2-column.out"
"$BIN/scout" -P -H -n --column -b -o 'o(?=o)' "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-fields-only-matching.out"
printf '%s:1:2:1:o\n%s:2:2:8:o\n%s:3:2:12:o\n%s:3:8:18:o\n' "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" "$BIN/pcre2-smoke.txt" > "$BIN/pcre2-fields-only-matching.expected"
expect_equal_file "PCRE2 fields with only-matching" "$BIN/pcre2-fields-only-matching.expected" "$BIN/pcre2-fields-only-matching.out"
cat > "$BIN/pcre2-multiline.txt" <<'EOF'
Start
middle
thing2
EOF
"$BIN/scout" -P --multiline '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline.out"
printf 'Start\n' > "$BIN/pcre2-multiline.expected"
expect_equal_file "PCRE2 multiline lookahead" "$BIN/pcre2-multiline.expected" "$BIN/pcre2-multiline.out"
"$BIN/scout" -P --json --multiline '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-json-multiline.out"
expect_contains "JSON multiline line text" '"lines":{"text":"Start\\n"}' "$BIN/pcre2-json-multiline.out"
expect_contains "JSON multiline match text" '"match":{"text":"Start"}' "$BIN/pcre2-json-multiline.out"
expect_contains "JSON multiline summary event" '"type":"summary"' "$BIN/pcre2-json-multiline.out"
"$BIN/scout" -P --multiline --files-with-matches '(?s)Start(?=.*thing2)' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline-files.out"
printf '%s\n' "$BIN/pcre2-multiline.txt" > "$BIN/pcre2-multiline-files.expected"
expect_equal_file "PCRE2 multiline files-with-matches" "$BIN/pcre2-multiline-files.expected" "$BIN/pcre2-multiline-files.out"
cat > "$BIN/pcre2-multiline-count.txt" <<'EOF'
def A;
def B;
use A;
use B;
EOF
"$BIN/scout" -P --multiline --count '(?s)def (\w+);(?=.*use \w+)' "$BIN/pcre2-multiline-count.txt" > "$BIN/pcre2-multiline-count.out"
printf '2\n' > "$BIN/pcre2-multiline-count.expected"
expect_equal_file "PCRE2 multiline count" "$BIN/pcre2-multiline-count.expected" "$BIN/pcre2-multiline-count.out"
"$BIN/scout" -P --multiline --count-matches '(?s)def (\w+);(?=.*use \w+)' "$BIN/pcre2-multiline-count.txt" > "$BIN/pcre2-multiline-count-matches.out"
printf '2\n' > "$BIN/pcre2-multiline-count-matches.expected"
expect_equal_file "PCRE2 multiline count-matches" "$BIN/pcre2-multiline-count-matches.expected" "$BIN/pcre2-multiline-count-matches.out"
if [ "$DIFFERENTIAL_MODE" = "--with-differentials" ]; then
    "$ROOT/native/test-generated-artifacts-unix.sh" "$RID" "$BIN/scout"
    "$ROOT/native/test-pcre2-differential-unix.sh" "$RID" "$BIN/scout"
    "$ROOT/native/test-invalid-utf8-differential-unix.sh" "$RID" "$BIN/scout"
fi
PCRE2_SYMBOL_PREFIX=
case "$RID" in
    osx-*)
        PCRE2_SYMBOL_PREFIX=_
        ;;
esac
for symbol in \
    pcre2_code_free_8 \
    pcre2_compile_8 \
    pcre2_config_8 \
    pcre2_get_error_message_8 \
    pcre2_get_ovector_pointer_8 \
    pcre2_jit_compile_8 \
    pcre2_match_8 \
    pcre2_match_data_create_from_pattern_8 \
    pcre2_match_data_free_8; do
    exported_symbol="$PCRE2_SYMBOL_PREFIX$symbol"
    if ! nm -g "$BIN/scout" | grep " $exported_symbol$" >/dev/null; then
        printf 'Missing native PCRE2 symbol %s in %s.\n' "$exported_symbol" "$BIN/scout" >&2
        exit 1
    fi
done
printf 'OK %s: Scout.App native export linked with PCRE2 and smoke checks passed\n' "$RID"
