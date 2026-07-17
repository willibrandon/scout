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

read_ripgrep_oracle_value() {
    sh "$ROOT/eng/read-ripgrep-oracle.sh" "$1" "$2"
}

resolve_repo_path() {
    case "$1" in
        /*)
            printf '%s\n' "$1"
            ;;
        *)
            printf '%s/%s\n' "$ROOT" "$1"
            ;;
    esac
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

compare_files() {
    expected="$1"
    actual="$2"

    if command -v diff >/dev/null 2>&1; then
        diff -u "$expected" "$actual"
        return
    fi

    if command -v cmp >/dev/null 2>&1; then
        cmp -s "$expected" "$actual"
        return
    fi

    expected_sha256="$(sha256_file "$expected")"
    actual_sha256="$(sha256_file "$actual")"
    if [ "$expected_sha256" = "$actual_sha256" ]; then
        return 0
    fi

    printf 'Files differ: %s sha256=%s, %s sha256=%s\n' \
        "$expected" "$expected_sha256" "$actual" "$actual_sha256" >&2
    return 1
}

normalize_elapsed() {
    sed -E \
        -e 's/"elapsed":\{"secs":[0-9]+,"nanos":[0-9]+,"human":"[^"]+"\}/"elapsed":{"secs":0,"nanos":0,"human":"<elapsed>"}/g' \
        -e 's/"elapsed":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}/"elapsed":{"human":"<elapsed>","nanos":0,"secs":0}/g' \
        -e 's/"elapsed_total":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}/"elapsed_total":{"human":"<elapsed>","nanos":0,"secs":0}/g' \
        -e 's/^[0-9]+\.[0-9]{6} seconds spent searching$/0.000000 seconds spent searching/g' \
        -e 's/^[0-9]+\.[0-9]{6} seconds total$/0.000000 seconds total/g'
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

    if ! compare_files "$normalized_rg_stdout" "$normalized_scout_stdout"; then
        printf 'PCRE2 differential stdout mismatch for %s\n' "$name" >&2
        exit 1
    fi

    if ! compare_files "$normalized_rg_stderr" "$normalized_scout_stderr"; then
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

    if ! compare_files "$normalized_rg_stdout" "$normalized_scout_stdout"; then
        printf 'PCRE2 differential stdout mismatch for %s\n' "$name" >&2
        exit 1
    fi

    if ! compare_files "$normalized_rg_stderr" "$normalized_scout_stderr"; then
        printf 'PCRE2 differential stderr mismatch for %s\n' "$name" >&2
        exit 1
    fi
}

[ -x "$SCOUT" ] || fail "Missing executable Scout binary: $SCOUT"

RG_PCRE2_VALUE="$(read_ripgrep_oracle_value "pcre2_path" "ripgrep_pcre2_rg_path")" || fail "Missing ripgrep PCRE2 oracle path in tests/PREREQS.lock."
RG_PCRE2="$(resolve_repo_path "$RG_PCRE2_VALUE")"
RG_PCRE2_SHA256="$(read_ripgrep_oracle_value "pcre2_sha256" "ripgrep_pcre2_rg_sha256")" || fail "Missing ripgrep PCRE2 oracle sha256 in tests/PREREQS.lock."
RG_PCRE2_REPORTED_VERSION="$(read_lock_value "ripgrep_pcre2_reported_version")" || fail "Missing ripgrep_pcre2_reported_version in tests/PREREQS.lock."
[ -x "$RG_PCRE2" ] || fail "Missing executable PCRE2 reference rg binary: $RG_PCRE2"
expect_equal "PCRE2 reference rg sha256" "$RG_PCRE2_SHA256" "$(sha256_file "$RG_PCRE2")"
expect_equal "PCRE2 reference rg PCRE2 version" "$RG_PCRE2_REPORTED_VERSION" "$("$RG_PCRE2" --pcre2-version | sed -n '1p')"

rm -rf "$OUT"
mkdir -p "$WORK"

printf 'foobar\nfoo\nfoobarfoo\n' > "$WORK/pcre2-smoke.txt"
printf 'barfoo\nfoobar\n' > "$WORK/pcre2-smoke-2.txt"
printf 'foobar\nfoo\n' > "$WORK/pcre2-capture-lf"
printf 'foobar\r\nfoo\r\n' > "$WORK/pcre2-capture-crlf"
printf 'foo-bar\nfoobar\n' > "$WORK/pcre2-word.txt"
cat > "$WORK/pcre2-context" <<'EOF'
one
foobar
middle
foo
last
EOF
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
cat > "$WORK/pcre2-multiline-vimgrep" <<'EOF'
one
xxfoo
barxx
foo
last
EOF
cat > "$WORK/pcre2-fixed" <<'EOF'
foo(?=bar)
foobar
foo\nbar
EOF
printf 'one\0needle\0two\nneedle\0' > "$WORK/pcre2-null"
printf 'zero\0foo\0bar\0tail\nfoo\0bar\0' > "$WORK/pcre2-null-multiline"

compare_case basic_lookahead exact -P 'foo(?=bar)' pcre2-smoke.txt
compare_case fixed_literal exact -P -F -n 'foo(?=bar)' pcre2-fixed
compare_case fixed_literal_json mask-elapsed -P -F --json 'foo(?=bar)' pcre2-fixed
compare_case fixed_literal_multiline exact -P -F --multiline -n 'foo\nbar' pcre2-fixed
compare_case null_data_line exact -P --null-data -n 'needle' pcre2-null
compare_case null_data_only_matching exact -P --null-data -o 'needle' pcre2-null
compare_case null_data_count exact -P --null-data --count 'needle' pcre2-null
compare_case null_data_json mask-elapsed -P --null-data --json 'needle' pcre2-null
compare_case null_data_context exact -P --null-data -n -C1 'needle' pcre2-null
compare_case null_data_multiline exact -P --null-data --multiline -n '(?s)foo.*bar' pcre2-null-multiline
compare_case null_data_multiline_only_matching exact -P --null-data --multiline -o '(?s)foo.*bar' pcre2-null-multiline
compare_case null_data_multiline_count exact -P --null-data --multiline --count '(?s)foo.*bar' pcre2-null-multiline
compare_case null_data_multiline_json mask-elapsed -P --null-data --json --multiline '(?s)foo.*bar' pcre2-null-multiline
compare_case null_data_multiline_context exact -P --null-data --multiline -n -C1 '(?s)foo.*bar' pcre2-null-multiline
compare_case basic_lookahead_multi_file sort-lines -P -n 'foo(?=bar)' pcre2-smoke.txt pcre2-smoke-2.txt
compare_case recursive_lookahead sort-lines -P -n 'foo(?=bar)' pcre2-dir
compare_case recursive_lookahead_threads sort-lines -P --threads 4 -n 'foo(?=bar)' pcre2-dir
compare_case line_regexp exact -P -x 'foo(?=bar)bar' pcre2-smoke.txt
compare_case word_regexp exact -P -w 'foo(?=-)' pcre2-word.txt
compare_case context exact -P -n -C1 'foo(?=bar)' pcre2-context
compare_case passthru exact -P -n --passthru 'foo(?=bar)' pcre2-context
compare_case context_only_matching exact -P -n -o -C1 'foo(?=bar)' pcre2-context
compare_case context_replacement exact -P -n -r X -C1 'foo(?=bar)' pcre2-context
compare_case numbered_capture_replacement exact -P -r '$2:$1' '(foo)(bar)' pcre2-smoke.txt
compare_case named_capture_replacement exact -P -r '${right}:${left}' '(?<left>foo)(?<right>bar)' pcre2-smoke.txt
compare_case unmatched_capture_replacement exact -P -r '$1:$2:${right}' '(?<left>foo)(?<right>bar)?' pcre2-smoke.txt
compare_case duplicate_named_capture_replacement exact -P -r '${value}' '(?J)(?<value>foo)|(?<value>bar)' pcre2-smoke.txt
compare_case reset_start_capture_replacement exact -P -r '$1:${tail}' '(foo)\K(?<tail>bar)' pcre2-smoke.txt
compare_case lf_numbered_capture_replacement exact -P -r '$1:$2' '(foo)(bar)?$' pcre2-capture-lf
compare_case lf_named_capture_replacement exact -P -r '${left}:${right}' '(?<left>foo)(?<right>bar)?$' pcre2-capture-lf
compare_case crlf_numbered_capture_replacement exact -P --crlf -r '$1:$2' '(foo)(bar)?$' pcre2-capture-crlf
compare_case crlf_named_capture_replacement exact -P --crlf -r '${left}:${right}' '(?<left>foo)(?<right>bar)?$' pcre2-capture-crlf
compare_case only_matching_replacement exact -P -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt
compare_case only_matching_replacement_columns exact -P -n --column -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt
compare_case vimgrep_lookahead exact -P --vimgrep 'foo(?=bar)' pcre2-smoke.txt
compare_case vimgrep_only_matching exact -P --vimgrep -o 'foo(?=bar)|foo' pcre2-smoke.txt
compare_case vimgrep_replacement exact -P --vimgrep -r X 'foo(?=bar)' pcre2-smoke.txt
compare_case vimgrep_only_matching_replacement exact -P --vimgrep -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt
compare_case vimgrep_context exact -P --vimgrep -C1 'foo(?=bar)' pcre2-context
compare_case vimgrep_context_replacement exact -P --vimgrep -r X -C1 'foo(?=bar)' pcre2-context
compare_case vimgrep_invert exact -P --vimgrep -v 'foo(?=bar)' pcre2-context
compare_case vimgrep_count exact -P --vimgrep --count 'foo(?=bar)' pcre2-smoke.txt
compare_case count_replacement exact -P --count -r X 'foo(?=bar)' pcre2-smoke.txt
compare_case count_matches_replacement exact -P --count-matches -r X 'foo(?=bar)|foo' pcre2-smoke.txt
compare_case files_with_matches_replacement exact -P --files-with-matches -r X 'foo(?=bar)' pcre2-smoke.txt
compare_case files_without_match_replacement exact -P --files-without-match -r X 'notfound' pcre2-smoke.txt
compare_case count_context_replacement exact -P --count -C1 -r X 'foo(?=bar)' pcre2-smoke.txt
compare_stdin_case vimgrep_implicit_stdin exact "$WORK/pcre2-smoke.txt" -P --vimgrep 'foo(?=bar)'
compare_stdin_case vimgrep_count_stdin exact "$WORK/pcre2-smoke.txt" -P --vimgrep --count 'foo(?=bar)'
compare_stdin_case explicit_stdin_lookahead exact "$WORK/pcre2-smoke.txt" -P 'foo(?=bar)' -
compare_stdin_case implicit_stdin_lookahead exact "$WORK/pcre2-smoke.txt" -P 'foo(?=bar)'
compare_case f1155_auto_hybrid_regex exact --no-pcre2 --auto-hybrid-regex '(?<=the )Sherlock' sherlock
compare_case json_lookahead mask-elapsed -P --json 'foo(?=bar)' pcre2-smoke.txt
compare_case json_lookahead_only_matching mask-elapsed -P --json -o 'foo(?=bar)' pcre2-smoke.txt
compare_case json_multi_file mask-elapsed-sort-lines -P --json 'foo(?=bar)' pcre2-smoke.txt pcre2-smoke-2.txt
compare_case json_quiet mask-elapsed -P --json -q 'foo(?=bar)' pcre2-smoke.txt
compare_case json_stats_lookahead mask-elapsed -P --json --stats 'foo(?=bar)' pcre2-smoke.txt
compare_case json_replacement mask-elapsed -P --json -r X 'foo(?=bar)' pcre2-smoke.txt
compare_case json_numbered_capture_replacement mask-elapsed -P --json -r '$2:$1' '(foo)(bar)' pcre2-smoke.txt
compare_case json_named_capture_replacement mask-elapsed -P --json -r '${right}:${left}' '(?<left>foo)(?<right>bar)' pcre2-smoke.txt
compare_case json_unmatched_capture_replacement mask-elapsed -P --json -r '$1:$2:${right}' '(?<left>foo)(?<right>bar)?' pcre2-smoke.txt
compare_case json_duplicate_named_capture_replacement mask-elapsed -P --json -r '${value}' '(?J)(?<value>foo)|(?<value>bar)' pcre2-smoke.txt
compare_case json_reset_start_capture_replacement mask-elapsed -P --json -r '$1:${tail}' '(foo)\K(?<tail>bar)' pcre2-smoke.txt
compare_case json_only_matching_replacement mask-elapsed -P --json -o -r X 'foo(?=bar)' pcre2-smoke.txt
compare_case json_context_lookahead mask-elapsed -P --json -C1 'foo(?=bar)' pcre2-context
compare_case json_context_replacement mask-elapsed -P --json -r X -C1 'foo(?=bar)' pcre2-context
compare_case json_passthru_lookahead mask-elapsed -P --json --passthru 'foo(?=bar)' pcre2-context
compare_case json_multiline_replacement mask-elapsed -P --json --multiline -r X '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case json_multiline_context mask-elapsed -P --json --multiline -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case json_multiline_context_replacement mask-elapsed -P --json --multiline -r X -C1 '(?s)foo\nbar|foo' pcre2-multiline-vimgrep
compare_case json_multiline_passthru_max_count mask-elapsed -P --json --multiline --passthru -m1 '(?s)foo\nbar|foo' pcre2-multiline-vimgrep
compare_case json_multiline_invert_context mask-elapsed -P --json --multiline -v -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case stats_lookahead mask-elapsed -P --stats 'foo(?=bar)' pcre2-smoke.txt
compare_case stats_recursive_lookahead_threads mask-elapsed-sort-lines -P --threads 4 --stats 'foo(?=bar)' pcre2-dir
compare_case json_lookbehind mask-elapsed -P -U --json '(?<=foo\n)bar' lookbehind
compare_case r1412_lookbehind_replacement exact -P -nU -rquux '(?<=foo\n)bar' lookbehind
compare_case r1401_lookahead_only_matching_1 exact -P -N -o '.*o(?!.*\s)' ip1.txt
compare_case r1401_lookahead_only_matching_1_tabs exact -P -N -o '.*o(?!.*[ \t])' ip1.txt
compare_case r1401_lookahead_only_matching_2 exact -P -N -o '.*o(?!.*\s)' ip2.txt
compare_case r1573_count exact -P --multiline --count '(?s)def (\w+);(?=.*use \w+)' counts
compare_case r1573_count_matches exact -P --multiline --count-matches '(?s)def (\w+);(?=.*use \w+)' counts
compare_case multiline_count_only_matching exact -P --multiline -o --count 'foo' pcre2-smoke.txt
compare_case r3139_multiline_lookahead exact -P --multiline '(?s)Start(?=.*thing2)' multiline-lookahead
compare_case r3139_multiline_files_with_matches exact -P --multiline --files-with-matches '(?s)Start(?=.*thing2)' multiline-lookahead
compare_case multiline_only_matching exact -P --multiline -o '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_only_matching_replacement exact -P --multiline -o -r X '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_context exact -P --multiline -n -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_before_after exact -P --multiline -n -B1 -A2 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_passthru exact -P --multiline -n --passthru '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_passthru_max_count exact -P --multiline -n --passthru -m1 '(?s)foo\nbar|foo' pcre2-multiline-vimgrep
compare_case multiline_context_max_count exact -P --multiline -n -C1 -m1 '(?s)foo\nbar|foo' pcre2-multiline-vimgrep
compare_case multiline_context_only_matching exact -P --multiline -n -o -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_context_only_matching_replacement exact -P --multiline -n -o -r X -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_context_replacement exact -P --multiline -n -r X -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_context_replacement_max_count exact -P --multiline -n -r X -C1 -m1 '(?s)foo\nbar|foo' pcre2-multiline-vimgrep
compare_case vimgrep_multiline exact -P --vimgrep --multiline '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_byte_offset exact -P --vimgrep -b --multiline '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_only_matching exact -P --vimgrep --multiline -o '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_replacement exact -P --vimgrep --multiline -r X '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_context exact -P --vimgrep --multiline -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_context_only_matching exact -P --vimgrep --multiline -o -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case vimgrep_multiline_context_replacement exact -P --vimgrep --multiline -r X -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert exact -P --multiline -n -v '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_context exact -P --multiline -n -v -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_passthru exact -P --multiline -n -v --passthru '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_count exact -P --multiline -v --count '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_count_matches exact -P --multiline -v --count-matches '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_files_with_matches exact -P --multiline -v --files-with-matches '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_files_without_match exact -P --multiline -v --files-without-match '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_vimgrep exact -P --vimgrep --multiline -v '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_vimgrep_context exact -P --vimgrep --multiline -v -C1 '(?s)foo\nbar' pcre2-multiline-vimgrep
compare_case multiline_invert_quiet exact -P --multiline -q -v '(?s)foo\nbar' pcre2-multiline-vimgrep

printf 'OK %s: PCRE2 native differentials matched pinned rg\n' "$RID"
