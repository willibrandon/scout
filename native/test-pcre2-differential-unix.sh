#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
RID="${1:-osx-arm64}"
SCOUT="${2:-$ROOT/artifacts/bin/$RID/scout}"
OUT="$ROOT/artifacts/native/pcre2-differential/$RID"
WORK="$OUT/work"

case "$SCOUT" in
    /*)
        ;;
    *)
        SCOUT="$ROOT/$SCOUT"
        ;;
esac

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

normalize_elapsed() {
    sed -E \
        -e 's/"elapsed":\{"secs":[0-9]+,"nanos":[0-9]+,"human":"[^"]+"\}/"elapsed":{"secs":0,"nanos":0,"human":"<elapsed>"}/g' \
        -e 's/"elapsed":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}/"elapsed":{"human":"<elapsed>","nanos":0,"secs":0}/g' \
        -e 's/"elapsed_total":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}/"elapsed_total":{"human":"<elapsed>","nanos":0,"secs":0}/g'
}

run_tool() {
    tool="$1"
    stdout="$2"
    stderr="$3"
    shift 3
    (cd "$WORK" && "$tool" "$@") > "$stdout" 2> "$stderr"
}

run_tool_stdin() {
    input="$1"
    tool="$2"
    stdout="$3"
    stderr="$4"
    shift 4
    (cd "$WORK" && "$tool" "$@" < "$input") > "$stdout" 2> "$stderr"
}

compare_case() {
    name="$1"
    mode="$2"
    shift 2

    scout_stdout="$OUT/$name.scout.stdout"
    scout_stderr="$OUT/$name.scout.stderr"
    rg_stdout="$OUT/$name.rg.stdout"
    rg_stderr="$OUT/$name.rg.stderr"
    normalized_scout_stdout="$OUT/$name.scout.normalized.stdout"
    normalized_rg_stdout="$OUT/$name.rg.normalized.stdout"
    normalized_scout_stderr="$OUT/$name.scout.normalized.stderr"
    normalized_rg_stderr="$OUT/$name.rg.normalized.stderr"

    if run_tool "$SCOUT" "$scout_stdout" "$scout_stderr" "$@"; then
        scout_status=0
    else
        scout_status=$?
    fi

    if run_tool "$RG_PCRE2" "$rg_stdout" "$rg_stderr" "$@"; then
        rg_status=0
    else
        rg_status=$?
    fi

    if [ "$scout_status" -ne "$rg_status" ]; then
        printf 'PCRE2 differential status mismatch for %s\n' "$name" >&2
        printf 'scout=%s rg=%s\n' "$scout_status" "$rg_status" >&2
        exit 1
    fi

    case "$mode" in
        exact)
            cp "$scout_stdout" "$normalized_scout_stdout"
            cp "$rg_stdout" "$normalized_rg_stdout"
            cp "$scout_stderr" "$normalized_scout_stderr"
            cp "$rg_stderr" "$normalized_rg_stderr"
            ;;
        mask-elapsed)
            normalize_elapsed < "$scout_stdout" > "$normalized_scout_stdout"
            normalize_elapsed < "$rg_stdout" > "$normalized_rg_stdout"
            normalize_elapsed < "$scout_stderr" > "$normalized_scout_stderr"
            normalize_elapsed < "$rg_stderr" > "$normalized_rg_stderr"
            ;;
        mask-elapsed-sort-lines)
            normalize_elapsed < "$scout_stdout" | LC_ALL=C sort > "$normalized_scout_stdout"
            normalize_elapsed < "$rg_stdout" | LC_ALL=C sort > "$normalized_rg_stdout"
            normalize_elapsed < "$scout_stderr" | LC_ALL=C sort > "$normalized_scout_stderr"
            normalize_elapsed < "$rg_stderr" | LC_ALL=C sort > "$normalized_rg_stderr"
            ;;
        sort-lines)
            LC_ALL=C sort "$scout_stdout" > "$normalized_scout_stdout"
            LC_ALL=C sort "$rg_stdout" > "$normalized_rg_stdout"
            cp "$scout_stderr" "$normalized_scout_stderr"
            cp "$rg_stderr" "$normalized_rg_stderr"
            ;;
        *)
            fail "Unknown PCRE2 differential normalization mode: $mode"
            ;;
    esac

    if ! diff -u "$normalized_rg_stdout" "$normalized_scout_stdout"; then
        printf 'PCRE2 differential stdout mismatch for %s\n' "$name" >&2
        exit 1
    fi

    if ! diff -u "$normalized_rg_stderr" "$normalized_scout_stderr"; then
        printf 'PCRE2 differential stderr mismatch for %s\n' "$name" >&2
        exit 1
    fi
}

compare_stdin_case() {
    name="$1"
    mode="$2"
    input="$3"
    shift 3

    scout_stdout="$OUT/$name.scout.stdout"
    scout_stderr="$OUT/$name.scout.stderr"
    rg_stdout="$OUT/$name.rg.stdout"
    rg_stderr="$OUT/$name.rg.stderr"
    normalized_scout_stdout="$OUT/$name.scout.normalized.stdout"
    normalized_rg_stdout="$OUT/$name.rg.normalized.stdout"
    normalized_scout_stderr="$OUT/$name.scout.normalized.stderr"
    normalized_rg_stderr="$OUT/$name.rg.normalized.stderr"

    if run_tool_stdin "$input" "$SCOUT" "$scout_stdout" "$scout_stderr" "$@"; then
        scout_status=0
    else
        scout_status=$?
    fi

    if run_tool_stdin "$input" "$RG_PCRE2" "$rg_stdout" "$rg_stderr" "$@"; then
        rg_status=0
    else
        rg_status=$?
    fi

    if [ "$scout_status" -ne "$rg_status" ]; then
        printf 'PCRE2 differential status mismatch for %s\n' "$name" >&2
        printf 'scout=%s rg=%s\n' "$scout_status" "$rg_status" >&2
        exit 1
    fi

    case "$mode" in
        exact)
            cp "$scout_stdout" "$normalized_scout_stdout"
            cp "$rg_stdout" "$normalized_rg_stdout"
            cp "$scout_stderr" "$normalized_scout_stderr"
            cp "$rg_stderr" "$normalized_rg_stderr"
            ;;
        mask-elapsed)
            normalize_elapsed < "$scout_stdout" > "$normalized_scout_stdout"
            normalize_elapsed < "$rg_stdout" > "$normalized_rg_stdout"
            normalize_elapsed < "$scout_stderr" > "$normalized_scout_stderr"
            normalize_elapsed < "$rg_stderr" > "$normalized_rg_stderr"
            ;;
        mask-elapsed-sort-lines)
            normalize_elapsed < "$scout_stdout" | LC_ALL=C sort > "$normalized_scout_stdout"
            normalize_elapsed < "$rg_stdout" | LC_ALL=C sort > "$normalized_rg_stdout"
            normalize_elapsed < "$scout_stderr" | LC_ALL=C sort > "$normalized_scout_stderr"
            normalize_elapsed < "$rg_stderr" | LC_ALL=C sort > "$normalized_rg_stderr"
            ;;
        sort-lines)
            LC_ALL=C sort "$scout_stdout" > "$normalized_scout_stdout"
            LC_ALL=C sort "$rg_stdout" > "$normalized_rg_stdout"
            cp "$scout_stderr" "$normalized_scout_stderr"
            cp "$rg_stderr" "$normalized_rg_stderr"
            ;;
        *)
            fail "Unknown PCRE2 differential normalization mode: $mode"
            ;;
    esac

    if ! diff -u "$normalized_rg_stdout" "$normalized_scout_stdout"; then
        printf 'PCRE2 differential stdout mismatch for %s\n' "$name" >&2
        exit 1
    fi

    if ! diff -u "$normalized_rg_stderr" "$normalized_scout_stderr"; then
        printf 'PCRE2 differential stderr mismatch for %s\n' "$name" >&2
        exit 1
    fi
}

[ -x "$SCOUT" ] || fail "Missing executable Scout binary: $SCOUT"

RG_PCRE2="$(read_lock_value "ripgrep_pcre2_rg_path")" || fail "Missing ripgrep_pcre2_rg_path in tests/PREREQS.lock."
RG_PCRE2_SHA256="$(read_lock_value "ripgrep_pcre2_rg_sha256")" || fail "Missing ripgrep_pcre2_rg_sha256 in tests/PREREQS.lock."
RG_PCRE2_REPORTED_VERSION="$(read_lock_value "ripgrep_pcre2_reported_version")" || fail "Missing ripgrep_pcre2_reported_version in tests/PREREQS.lock."
[ -x "$RG_PCRE2" ] || fail "Missing executable PCRE2 reference rg binary: $RG_PCRE2"
expect_equal "PCRE2 reference rg sha256" "$RG_PCRE2_SHA256" "$(sha256_file "$RG_PCRE2")"
expect_equal "PCRE2 reference rg PCRE2 version" "$RG_PCRE2_REPORTED_VERSION" "$("$RG_PCRE2" --pcre2-version | sed -n '1p')"

rm -rf "$OUT"
mkdir -p "$WORK"

printf 'foobar\nfoo\nfoobarfoo\n' > "$WORK/pcre2-smoke.txt"
printf 'barfoo\nfoobar\n' > "$WORK/pcre2-smoke-2.txt"
printf 'foo-bar\nfoobar\n' > "$WORK/pcre2-word.txt"
printf 'foo\nbar\n' > "$WORK/lookbehind"
printf 'For the Doctor Watsons of this world, as opposed to the Sherlock\n' > "$WORK/sherlock"
printf 'foo 42\nxoyz\ncat\tdog\n' > "$WORK/ip1.txt"
printf 'foo 42\nxoyz\ncat\tdog\nfoo' > "$WORK/ip2.txt"
mkdir -p "$WORK/pcre2-dir/sub"
printf 'foo\nfoobar\n' > "$WORK/pcre2-dir/a.txt"
printf 'foobarfoo\n' > "$WORK/pcre2-dir/sub/b.txt"
cat > "$WORK/counts" <<'EOF'
def A;
def B;
use A;
use B;
EOF
cat > "$WORK/multiline-lookahead" <<'EOF'
Start 
   

   XXXXXXXXXXXXXXXXXXXXXXXXXX
   YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY
   
      thing2 

EOF

compare_case basic_lookahead exact -P 'foo(?=bar)' pcre2-smoke.txt
compare_case basic_lookahead_multi_file sort-lines -P -n 'foo(?=bar)' pcre2-smoke.txt pcre2-smoke-2.txt
compare_case recursive_lookahead sort-lines -P -n 'foo(?=bar)' pcre2-dir
compare_case line_regexp exact -P -x 'foo(?=bar)bar' pcre2-smoke.txt
compare_case word_regexp exact -P -w 'foo(?=-)' pcre2-word.txt
compare_stdin_case explicit_stdin_lookahead exact "$WORK/pcre2-smoke.txt" -P 'foo(?=bar)' -
compare_stdin_case implicit_stdin_lookahead exact "$WORK/pcre2-smoke.txt" -P 'foo(?=bar)'
compare_case f1155_auto_hybrid_regex exact --no-pcre2 --auto-hybrid-regex '(?<=the )Sherlock' sherlock
compare_case json_lookahead mask-elapsed -P --json 'foo(?=bar)' pcre2-smoke.txt
compare_case json_multi_file mask-elapsed-sort-lines -P --json 'foo(?=bar)' pcre2-smoke.txt pcre2-smoke-2.txt
compare_case json_quiet mask-elapsed -P --json -q 'foo(?=bar)' pcre2-smoke.txt
compare_case json_lookbehind mask-elapsed -P -U --json '(?<=foo\n)bar' lookbehind
compare_case r1412_lookbehind_replacement exact -P -nU -rquux '(?<=foo\n)bar' lookbehind
compare_case r1401_lookahead_only_matching_1 exact -P -N -o '.*o(?!.*\s)' ip1.txt
compare_case r1401_lookahead_only_matching_1_tabs exact -P -N -o '.*o(?!.*[ \t])' ip1.txt
compare_case r1401_lookahead_only_matching_2 exact -P -N -o '.*o(?!.*\s)' ip2.txt
compare_case r1573_count exact -P --multiline --count '(?s)def (\w+);(?=.*use \w+)' counts
compare_case r1573_count_matches exact -P --multiline --count-matches '(?s)def (\w+);(?=.*use \w+)' counts
compare_case r3139_multiline_lookahead exact -P --multiline '(?s)Start(?=.*thing2)' multiline-lookahead
compare_case r3139_multiline_files_with_matches exact -P --multiline --files-with-matches '(?s)Start(?=.*thing2)' multiline-lookahead

printf 'OK %s: PCRE2 native differentials matched pinned rg\n' "$RID"
