#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
MODE="smoke"
RUNS="3"
RUNS_SPECIFIED="0"
WARMUP="1"
WARMUP_SPECIFIED="0"
OUT_DIR="$ROOT/artifacts/bench/hyperfine"
GATE_OPENSUBTITLES_RUNS="6"
GATE_OPENSUBTITLES_WARMUP="6"
GATE_TREE_RUNS="6"
GATE_TREE_WARMUP="6"
GATE_COLD_RUNS="6"
GATE_COLD_WARMUP="6"
GATE_BOUNDED_ASSIGNMENT_RUNS="6"
GATE_BOUNDED_ASSIGNMENT_WARMUP="6"
GATE_LARGE_BOUNDED_UNICODE_CLASS_RUNS="6"
GATE_LARGE_BOUNDED_UNICODE_CLASS_WARMUP="6"
GATE_LINE_REGEX_RUNS="6"
GATE_LINE_REGEX_WARMUP="6"
GATE_LARGE_FILE_THREADS="4"
GATE_LARGE_FILE_SEGMENT_BUFFER_LENGTH="131072"
GATE_RETRY_FAILED_WORKLOADS="${SCOUT_GATE_RETRY_FAILED_WORKLOADS:-2}"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' \
        'usage: bench/run-hyperfine.sh [--smoke|--gate|--list] [--runs N] [--warmup N] [--output-dir DIR]' \
        '' \
        'Environment:' \
        '  SCOUT_BIN                         Native AOT scout binary. Defaults to artifacts/bin/<rid>/scout.' \
        '  SCOUT_RSS_BASELINE_BIN            Native AOT real binary for RSS-floor measurement. Defaults to sibling scout-real when present.' \
        '  SCOUT_BENCH_OPENSUBTITLES_EN     OpenSubtitles en.txt path for --gate.' \
        '  SCOUT_BENCH_LINUX_TREE           Linux source tree path for --gate.' \
        '  SCOUT_GATE_RETRY_FAILED_WORKLOADS Retry a failed gate workload this many times. Defaults to 2; set 0 to disable.' \
        '' \
        'The --gate mode requires frozen corpus hashes in tests/PREREQS.lock.'
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

read_lock_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v key="$3" "$strip_toml_value"'
        $0 == header {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            matched = value_of($0) == name
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

oracle_environment() {
    if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        printf 'github-actions\n'
    else
        printf 'local\n'
    fi
}

read_lock_rid_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v rid="$3" -v environment="$4" -v key="$5" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_rid == rid && table_value != "" &&
                ((environment == "" && table_environment == "") || (environment != "" && table_environment == environment))) {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            table_rid = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_lock_environment_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v environment="$3" -v key="$4" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_environment == environment && table_value != "") {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_macos_tool_value() {
    name="$1"
    key="$2"

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$RID" "$HOST_ORACLE_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$RID" "" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_environment_table_value "tool.macos" "$name" "$HOST_ORACLE_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    read_lock_table_value "tool.macos" "$name" "$key"
}

read_corpus_value() {
    awk -v name="$1" -v key="$2" "$strip_toml_value"'
        $0 == "[[corpus]]" {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            matched = value_of($0) == name
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

sha256_stream() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

sha256_tree() {
    tree="$1"
    [ -d "$tree" ] || fail "Missing tree corpus: $tree"
    (
        cd "$tree"
        if command -v shasum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 shasum -a 256
        elif command -v sha256sum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum
        else
            find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | while IFS= read -r relative_path; do
                file_sha256="$(sha256_file "$relative_path")"
                printf '%s  %s\n' "$file_sha256" "$relative_path"
            done
        fi | sed 's#  \./#  #'
    ) | sha256_stream
}

shell_quote() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
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

read_ripgrep_oracle_value() {
    sh "$ROOT/eng/read-ripgrep-oracle.sh" "$1" "$2"
}

host_rid() {
    os="$(uname -s)"
    arch="$(uname -m)"
    case "$os:$arch" in
        Darwin:arm64)
            printf 'osx-arm64\n'
            ;;
        Darwin:x86_64)
            printf 'osx-x64\n'
            ;;
        Linux:x86_64)
            printf 'linux-x64\n'
            ;;
        Linux:aarch64|Linux:arm64)
            printf 'linux-arm64\n'
            ;;
        *)
            fail "Unsupported benchmark host: $os/$arch"
            ;;
    esac
}

require_frozen_value() {
    value="$1"
    label="$2"
    case "$value" in
        resolved@*)
            fail "$label is not frozen in tests/PREREQS.lock: $value"
            ;;
        "")
            fail "$label is empty in tests/PREREQS.lock"
            ;;
    esac
}

check_file_hash() {
    label="$1"
    path="$2"
    expected="$3"
    require_frozen_value "$expected" "$label sha256"
    [ -f "$path" ] || fail "Missing $label: $path"
    actual="$(sha256_file "$path")"
    [ "$actual" = "$expected" ] || fail "$label hash mismatch: expected $expected, got $actual"
}

check_tree_hash() {
    label="$1"
    path="$2"
    expected="$3"
    require_frozen_value "$expected" "$label tree_sha256"
    [ -d "$path" ] || fail "Missing $label tree: $path"
    actual="$(sha256_tree "$path")"
    [ "$actual" = "$expected" ] || fail "$label tree hash mismatch: expected $expected, got $actual"
}

check_tool_version() {
    label="$1"
    path="$2"
    expected="$3"
    actual="$("$path" --version | sed -n '1p')"
    [ "$actual" = "$expected" ] || fail "$label version mismatch: expected $expected, got $actual"
}

resolve_hyperfine() {
    pinned_path="$(read_macos_tool_value "hyperfine" "path")" || pinned_path=""
    pinned_sha256="$(read_macos_tool_value "hyperfine" "sha256")" || pinned_sha256=""
    pinned_version="$(read_macos_tool_value "hyperfine" "version")" || pinned_version=""

    if [ "$MODE" = "gate" ]; then
        [ -n "$pinned_path" ] || fail "Missing pinned hyperfine path in tests/PREREQS.lock."
        [ -n "$pinned_sha256" ] || fail "Missing pinned hyperfine hash in tests/PREREQS.lock."
        [ -n "$pinned_version" ] || fail "Missing pinned hyperfine version in tests/PREREQS.lock."
        check_file_hash "hyperfine" "$pinned_path" "$pinned_sha256"
        check_tool_version "hyperfine" "$pinned_path" "hyperfine $pinned_version"
        printf '%s\n' "$pinned_path"
        return
    fi

    if [ -n "$pinned_path" ] && [ -x "$pinned_path" ]; then
        if [ -n "$pinned_sha256" ]; then
            check_file_hash "hyperfine" "$pinned_path" "$pinned_sha256"
        fi
        if [ -n "$pinned_version" ]; then
            check_tool_version "hyperfine" "$pinned_path" "hyperfine $pinned_version"
        fi
        printf '%s\n' "$pinned_path"
        return
    fi

    command -v hyperfine || true
}

resolve_python() {
    command -v python3 || command -v python || true
}

require_gate_corpus_file() {
    name="$1"
    path_value="$2"
    expected_sha256="$(read_corpus_value "$name" "sha256")" || fail "Missing corpus hash for $name in tests/PREREQS.lock."
    require_frozen_value "$expected_sha256" "corpus $name"
    if [ -z "$path_value" ]; then
        path_value="$(read_corpus_value "$name" "path")" || fail "Missing path for corpus $name."
        path_value="$(resolve_repo_path "$path_value")"
    fi
    check_file_hash "corpus $name" "$path_value" "$expected_sha256"
    printf '%s\n' "$path_value"
}

require_gate_corpus_tree() {
    name="$1"
    path_value="$2"
    expected_sha256="$(read_corpus_value "$name" "tree_sha256")" || fail "Missing tree hash for $name in tests/PREREQS.lock."
    require_frozen_value "$expected_sha256" "corpus $name"
    if [ -z "$path_value" ]; then
        path_value="$(read_corpus_value "$name" "tree_path")" || fail "Missing tree_path for corpus $name."
        path_value="$(resolve_repo_path "$path_value")"
    fi
    check_tree_hash "corpus $name" "$path_value" "$expected_sha256"
    printf '%s\n' "$path_value"
}

make_smoke_corpus() {
    smoke_dir="$OUT_DIR/smoke-corpus"
    single_file="$smoke_dir/large-single.txt"
    tree_dir="$smoke_dir/many-small"
    mkdir -p "$tree_dir"

    if [ ! -f "$single_file" ]; then
        awk 'BEGIN { for (i = 0; i < 50000; i++) print "alpha beta gamma needle delta epsilon" }' > "$single_file"
    fi

    index=0
    while [ "$index" -lt 240 ]; do
        file="$tree_dir/file-$index.txt"
        if [ ! -f "$file" ]; then
            awk -v n="$index" 'BEGIN { for (i = 0; i < 24; i++) print "file " n " has a needle and a haystack" }' > "$file"
        fi
        index=$((index + 1))
    done
}

make_cold_tiny_corpus() {
    tiny_file="$OUT_DIR/cold-tiny.txt"
    if [ ! -f "$tiny_file" ]; then
        printf '%s\n' 'needle' > "$tiny_file"
    fi
}

make_bounded_assignment_corpus() {
    bounded_assignment_dir="$OUT_DIR/bounded-assignment"
    bounded_assignment_pattern="$bounded_assignment_dir/pattern.txt"
    bounded_assignment_input="$bounded_assignment_dir/no-match-800.txt"
    mkdir -p "$bounded_assignment_dir"

    cat > "$bounded_assignment_pattern" <<'EOF'
(?i)[\w.-]{0,50}?(?:bitbucket)(?:[ \t\w.-]{0,20})[\s'"]{0,3}(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)[\x60'"\s=]{0,5}([a-z0-9]{32})(?:[\x60'"\s;]|\\[nr]|$)
EOF
    awk 'BEGIN { for (i = 0; i < 800; i++) print "bitbucket repository setting without a credential" }' > "$bounded_assignment_input"
}

make_large_bounded_unicode_class_corpus() {
    large_bounded_unicode_class_dir="$OUT_DIR/large-bounded-unicode-class"
    large_bounded_unicode_class_pattern="$large_bounded_unicode_class_dir/pattern.txt"
    large_bounded_unicode_class_input="$large_bounded_unicode_class_dir/no-match-5000.txt"
    mkdir -p "$large_bounded_unicode_class_dir"

    cat > "$large_bounded_unicode_class_pattern" <<'EOF'
x[\w-]{50,1000}
EOF
    awk 'BEGIN { for (i = 0; i < 5000; i++) printf "x[%cw-]{50,1000}\n", 92 }' > "$large_bounded_unicode_class_input"
}

make_line_regex_corpus() {
    line_regex_dir="$OUT_DIR/line-regex"
    line_regex_input="$line_regex_dir/paladin-like-200000.txt"
    line_regex_absent_patterns="$line_regex_dir/absent-patterns-64.txt"
    mkdir -p "$line_regex_dir"

    awk 'BEGIN {
        for (i = 0; i < 200000; i++) {
            printf "alpha bravo charl delta eagle foxtt and unrelated symbols.\r\n"
            printf "internal sealed class GeneratedRecord\r\n"
            printf "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_abcdefghijklmnopqrstuvwxyz_\r\n"
            printf "internal sealed class OtherRecord { private readonly int _state; }\r\n"
        }

        printf "    public delegate bool ShowMessageBoxHandler(string message, string caption, bool buttons);\r\n"
        printf "    public delegate bool ShowCheckboxMessageBoxHandler(string message, string caption, bool buttons);\r\n"
        printf "    public delegate void SetProgressBarValue(int percentComplete, int currentValue);\r\n"
        printf "    public delegate void UpdateEDIEvent(string eventString);\r\n"
    }' > "$line_regex_input"
    awk 'BEGIN {
        for (i = 0; i < 64; i++) {
            printf "issue44_absent_pattern_%03d\n", i
        }
    }' > "$line_regex_absent_patterns"
}

make_absent_regexp_arguments() {
    awk '{ printf " -e %s", $0 }' "$1"
}

expect_no_match_command() {
    no_match_command="$1"
    no_match_label="$2"
    no_match_message="$(shell_quote "$no_match_label unexpectedly matched.")"
    printf '%s; status=$?; case $status in 1) exit 0 ;; 0) printf "%%s\\n" %s >&2; exit 125 ;; *) exit $status ;; esac' \
        "$no_match_command" \
        "$no_match_message"
}

hyperfine_json_samples() {
    json="$1"
    index="$2"
    metric="$3"
    "$PYTHON" - "$json" "$index" "$metric" <<'PY'
import json
import sys

path = sys.argv[1]
index = int(sys.argv[2]) - 1
metric = sys.argv[3]

with open(path, encoding="utf-8") as handle:
    document = json.load(handle)

try:
    value = document["results"][index][metric]
except (IndexError, KeyError):
    sys.exit(1)

if isinstance(value, list):
    for item in value:
        print(item)
else:
    print(value)
PY
}

hyperfine_json_memory_samples() {
    json="$1"
    index="$2"
    hyperfine_json_samples "$json" "$index" "memory_usage_byte"
}

median_numbers() {
    awk '
        BEGIN {
            count = 0
        }
        /^[0-9]+([.][0-9]+)?$/ {
            count++
            values[count] = $1 + 0
        }
        END {
            if (count == 0) {
                exit 1
            }

            for (i = 1; i <= count; i++) {
                for (j = i + 1; j <= count; j++) {
                    if (values[j] < values[i]) {
                        tmp = values[i]
                        values[i] = values[j]
                        values[j] = tmp
                    }
                }
            }

            middle = int(count / 2)
            if (count % 2 == 1) {
                printf "%.12g\n", values[middle + 1]
            } else {
                printf "%.12g\n", (values[middle] + values[middle + 1]) / 2
            }
        }
    '
}

hyperfine_json_median_memory() {
    hyperfine_json_memory_samples "$1" "$2" | median_numbers | awk '{ printf "%.0f\n", $1 }'
}

resolve_scout_rss_baseline_bin() {
    if [ -n "${SCOUT_RSS_BASELINE_BIN:-}" ]; then
        printf '%s\n' "$SCOUT_RSS_BASELINE_BIN"
        return
    fi

    scout_dir="$(dirname -- "$SCOUT_BIN")"
    scout_name="$(basename -- "$SCOUT_BIN")"
    if [ "$scout_name" = "scout" ] && [ -x "$scout_dir/scout-real" ]; then
        printf '%s\n' "$scout_dir/scout-real"
        return
    fi

    printf '%s\n' "$SCOUT_BIN"
}

measure_rss_floor() {
    runs="$1"
    warmup="$2"
    json="$OUT_DIR/rss_floor.json"

    printf '\n== rss_floor ==\n'
    run_hyperfine_interleaved \
        "1" \
        "$json" \
        "rss_floor" \
        "$Q_RG --no-config --mmap -n 'needle' $Q_TINY" \
        "$Q_SCOUT_RSS_BASELINE --no-config --mmap -n 'needle' $Q_TINY" \
        "$runs" \
        "$warmup"

    RG_RSS_FLOOR="$(hyperfine_json_median_memory "$json" 1)" || fail "Could not read rg RSS floor from $json."
    SCOUT_RSS_FLOOR="$(hyperfine_json_median_memory "$json" 2)" || fail "Could not read scout RSS floor from $json."
    [ -n "$RG_RSS_FLOOR" ] || fail "Could not read rg RSS floor from $json."
    [ -n "$SCOUT_RSS_FLOOR" ] || fail "Could not read scout RSS floor from $json."
    printf '\nRSS floor:\n'
    printf '  rg      %d bytes\n' "$RG_RSS_FLOOR"
    printf '  Scout  %d bytes\n' "$SCOUT_RSS_FLOOR"
}

analyze_large_file_segments() {
    name="$1"
    path="$2"
    buffer_length="$3"
    thread_count="$4"
    "$PYTHON" - "$name" "$path" "$buffer_length" "$thread_count" <<'PY'
import statistics
import sys

name = sys.argv[1]
path = sys.argv[2]
buffer_length = int(sys.argv[3])
thread_count = int(sys.argv[4])
terminator = b"\n"
carry = b""
segment_lengths = []
line_counts = []

with open(path, "rb", buffering=0) as handle:
    while True:
        read_length = buffer_length - len(carry)
        if read_length <= 0:
            segment_lengths.append(len(carry))
            line_counts.append(carry.count(terminator))
            carry = b""
            read_length = buffer_length

        chunk = handle.read(read_length)
        if not chunk:
            break

        combined = carry + chunk
        last_terminator = combined.rfind(terminator)
        if last_terminator < 0:
            carry = combined
            continue

        segment = combined[: last_terminator + 1]
        segment_lengths.append(len(segment))
        line_counts.append(segment.count(terminator))
        carry = combined[last_terminator + 1 :]

if carry:
    segment_lengths.append(len(carry))
    line_counts.append(max(1, carry.count(terminator)))

if not segment_lengths:
    print(f"{name} segment balance: no segments")
    sys.exit(1)

balanced_lengths = segment_lengths[:-1] if len(segment_lengths) > 1 else segment_lengths
balanced_lines = line_counts[:-1] if len(line_counts) > 1 else line_counts
median_length = statistics.median(balanced_lengths)
median_lines = statistics.median(balanced_lines)
max_length = max(balanced_lengths)
max_lines = max(balanced_lines)
length_ratio = max_length / median_length if median_length else float("inf")
line_ratio = max_lines / median_lines if median_lines else float("inf")
print(
    f"{name} segment balance: segments={len(segment_lengths)} "
    f"buffer={buffer_length} bytes median={median_length:.0f} max={max_length} "
    f"max/median={length_ratio:.3f} line-max/median={line_ratio:.3f} threads={thread_count}"
)

if len(segment_lengths) < thread_count * 4:
    print(f"{name}: too few segments for stable {thread_count}-thread large-file timing", file=sys.stderr)
    sys.exit(1)

if length_ratio > 1.10:
    print(f"{name}: uneven byte segment split: max/median={length_ratio:.3f}", file=sys.stderr)
    sys.exit(1)

PY
}

gate_opensubtitles_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_OPENSUBTITLES_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_opensubtitles_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_OPENSUBTITLES_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_tree_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_TREE_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_tree_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_TREE_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_cold_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_COLD_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_cold_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_COLD_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_bounded_assignment_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_BOUNDED_ASSIGNMENT_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_bounded_assignment_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_BOUNDED_ASSIGNMENT_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_large_bounded_unicode_class_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LARGE_BOUNDED_UNICODE_CLASS_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_large_bounded_unicode_class_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LARGE_BOUNDED_UNICODE_CLASS_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_line_regex_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LINE_REGEX_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_line_regex_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LINE_REGEX_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

report_interleaved_gate() {
    report_gate_name="$1"
    report_gate_limit="$2"
    report_gate_json="$3"
    report_gate_final_context="${4:-}"
    report_gate_status="0"

    if [ -n "$report_gate_final_context" ]; then
        "$PYTHON" "$ROOT/bench/hyperfine_gate.py" \
            --input "$report_gate_json" \
            --wall-limit "$report_gate_limit" \
            --scout-rss-floor "$SCOUT_RSS_FLOOR" \
            --workload "$report_gate_name" \
            --final-failure-context "$report_gate_final_context" || report_gate_status="$?"
    else
        "$PYTHON" "$ROOT/bench/hyperfine_gate.py" \
            --input "$report_gate_json" \
            --wall-limit "$report_gate_limit" \
            --scout-rss-floor "$SCOUT_RSS_FLOOR" \
            --workload "$report_gate_name" || report_gate_status="$?"
    fi

    case "$report_gate_status" in
        0)
            return 0
            ;;
        10|11|12)
            return 1
            ;;
        *)
            fail "Could not evaluate the Hyperfine gate from $report_gate_json."
            ;;
    esac
}

run_hyperfine_pair() {
    no_shell="$1"
    pair_json="$2"
    first_name="$3"
    first_command="$4"
    second_name="$5"
    second_command="$6"
    runs="$7"
    warmup="$8"

    if [ "$no_shell" = "1" ]; then
        "$HYPERFINE" \
            -N \
            --warmup "$warmup" \
            --runs "$runs" \
            --export-json "$pair_json" \
            --command-name "$first_name" "$first_command" \
            --command-name "$second_name" "$second_command"
        return
    fi

    "$HYPERFINE" \
        --warmup "$warmup" \
        --runs "$runs" \
        --export-json "$pair_json" \
        --command-name "$first_name" "$first_command" \
        --command-name "$second_name" "$second_command"
}

run_hyperfine_interleaved() {
    no_shell="$1"
    interleaved_json="$2"
    interleaved_name="$3"
    interleaved_rg_command="$4"
    interleaved_scout_command="$5"
    interleaved_runs="$6"
    interleaved_warmup="$7"

    if [ "$no_shell" = "1" ]; then
        "$PYTHON" "$ROOT/bench/hyperfine_interleaved.py" \
            --hyperfine "$HYPERFINE" \
            --name "$interleaved_name" \
            --rg-command "$interleaved_rg_command" \
            --scout-command "$interleaved_scout_command" \
            --rounds "$interleaved_runs" \
            --warmup-rounds "$interleaved_warmup" \
            --output "$interleaved_json" \
            --no-shell
        return
    fi

    "$PYTHON" "$ROOT/bench/hyperfine_interleaved.py" \
        --hyperfine "$HYPERFINE" \
        --name "$interleaved_name" \
        --rg-command "$interleaved_rg_command" \
        --scout-command "$interleaved_scout_command" \
        --rounds "$interleaved_runs" \
        --warmup-rounds "$interleaved_warmup" \
        --output "$interleaved_json"
}

run_pair_impl() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="$5"
    warmup="$6"
    no_shell="$7"
    json="$OUT_DIR/$name.json"

    printf '\n== %s ==\n' "$name"
    printf 'Limits: wall %.3fx; RSS 1.500x rg + Native AOT floor\n' "$gate"
    if [ "$MODE" = "gate" ]; then
        run_hyperfine_interleaved "$no_shell" "$json" "$name" "$rg_command" "$scout_command" "$runs" "$warmup"

        if ! report_interleaved_gate "$name" "$gate" "$json"; then
            if [ "$GATE_RETRY_FAILED_WORKLOADS" = "0" ]; then
                return 1
            fi

            retry_attempt=0
            while [ "$retry_attempt" -lt "$GATE_RETRY_FAILED_WORKLOADS" ]; do
                retry_attempt=$((retry_attempt + 1))
                retry_label="$name-retry-$retry_attempt"
                retry_json="$OUT_DIR/$name.retry-$retry_attempt.json"

                printf '\n-- %s retry %s/%s --\n' "$name" "$retry_attempt" "$GATE_RETRY_FAILED_WORKLOADS"
                run_hyperfine_interleaved "$no_shell" "$retry_json" "$retry_label" "$rg_command" "$scout_command" "$runs" "$warmup"

                retry_final_context=""
                if [ "$retry_attempt" = "$GATE_RETRY_FAILED_WORKLOADS" ]; then
                    retry_noun="retries"
                    if [ "$GATE_RETRY_FAILED_WORKLOADS" = "1" ]; then
                        retry_noun="retry"
                    fi
                    retry_final_context="after the initial attempt and $GATE_RETRY_FAILED_WORKLOADS $retry_noun"
                fi

                if report_interleaved_gate "$name" "$gate" "$retry_json" "$retry_final_context"; then
                    return 0
                fi
            done

            return 1
        fi
        return
    fi

    run_hyperfine_pair "$no_shell" "$json" "rg:$name" "$rg_command" "scout:$name" "$scout_command" "$runs" "$warmup"
}

run_pair() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="${5:-$RUNS}"
    warmup="${6:-$WARMUP}"

    run_pair_impl "$name" "$gate" "$rg_command" "$scout_command" "$runs" "$warmup" "0"
}

run_pair_no_shell() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="${5:-$RUNS}"
    warmup="${6:-$WARMUP}"

    run_pair_impl "$name" "$gate" "$rg_command" "$scout_command" "$runs" "$warmup" "1"
}

list_workloads() {
    printf '%s\n' \
        'smoke_large_literal          generated single file, no release gate' \
        'smoke_many_small             generated many-small-files tree, no release gate' \
        'smoke_cold_version           scout --version vs rg --version, no release gate' \
        'smoke_cold_tiny_search       cold tiny search, no release gate' \
        'bounded_assignment_no_match  generated 800-candidate issue #30 scan, gate <= 1.50x' \
        'large_bounded_unicode_class_no_match generated 5,000-candidate issue #32 scan in general mode, gate <= 1.50x' \
        'line_regex_word_boundary_general generated issue #37 prefilter-free word-boundary match count in general mode, gate <= 1.50x' \
        'line_regex_word_boundary_line_count_general generated issue #37 prefilter-free word-boundary line count in general mode, gate <= 1.50x' \
        'line_regex_anchored_general  generated issue #37 anchored-line scan in general mode, gate <= 1.50x' \
        'line_regex_bounded_class_general generated issue #37 bounded-class scan in general mode, gate <= 1.50x' \
        'shared_delegate_prefix_general generated issue #36 shared-prefix alternation in general mode, gate <= 1.50x' \
        'many_absent_regexp_general  generated issue #44 64-pattern -e scan in general mode, gate <= 1.50x' \
        'many_absent_pattern_file_general generated issue #44 64-pattern -f scan in general mode, gate <= 1.50x' \
        'subtitles_en_literal         OpenSubtitles literal scan, gate <= 1.20x' \
        'subtitles_en_regex           OpenSubtitles regex scan, gate <= 1.20x' \
        'linux_recursive_literal      Linux tree recursive walk, gate <= 1.25x' \
        'linux_heldout_regex_general  Linux tree held-out regex scan with Scout general-only mode, gate <= 1.50x' \
        'linux_heldout_capture_general Linux tree held-out replacement/capture scan with Scout general-only mode, gate <= 1.75x' \
        'linux_many_small_parallel    Linux tree many-small-files search, gate <= 1.30x' \
        'cold_version                 cold start, gate <= 1.00x' \
        'cold_tiny_search             cold tiny search, gate <= 1.00x' \
        'Peak RSS allows the measured Native AOT fixed RSS floor from docs/PARITY.md'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --smoke)
            MODE="smoke"
            shift
            ;;
        --gate)
            MODE="gate"
            shift
            ;;
        --list)
            MODE="list"
            shift
            ;;
        --runs)
            [ "$#" -ge 2 ] || fail "Missing value for --runs."
            RUNS="$2"
            RUNS_SPECIFIED="1"
            shift 2
            ;;
        --warmup)
            [ "$#" -ge 2 ] || fail "Missing value for --warmup."
            WARMUP="$2"
            WARMUP_SPECIFIED="1"
            shift 2
            ;;
        --output-dir)
            [ "$#" -ge 2 ] || fail "Missing value for --output-dir."
            OUT_DIR="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: $1"
            ;;
    esac
done

case "$GATE_RETRY_FAILED_WORKLOADS" in
    ''|*[!0-9]*)
        fail "SCOUT_GATE_RETRY_FAILED_WORKLOADS must be a non-negative integer."
        ;;
esac

case "$RUNS" in
    ''|*[!0-9]*|0*)
        fail "--runs must be a positive integer."
        ;;
esac

case "$WARMUP" in
    ''|*[!0-9]*|0[0-9]*)
        fail "--warmup must be a non-negative integer."
        ;;
esac

if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "1" ] && [ $((RUNS % 2)) -ne 0 ]; then
    fail "--gate requires an even --runs value for complete ABBA/BAAB cycles."
fi

if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "1" ] && [ $((WARMUP % 2)) -ne 0 ]; then
    fail "--gate requires an even --warmup value for complete ABBA/BAAB cycles."
fi

if [ "$MODE" = "list" ]; then
    list_workloads
    exit 0
fi

RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"
DEFAULT_SCOUT_BIN="$ROOT/artifacts/bin/$RID/scout"
SCOUT_BIN="${SCOUT_BIN:-$DEFAULT_SCOUT_BIN}"
RG_VALUE="$(read_ripgrep_oracle_value "path" "ripgrep_rg_path")" || fail "Missing ripgrep oracle path in tests/PREREQS.lock."
RG_BIN="$(resolve_repo_path "$RG_VALUE")"
RG_SHA256="$(read_ripgrep_oracle_value "sha256" "ripgrep_rg_sha256")" || fail "Missing ripgrep oracle sha256 in tests/PREREQS.lock."
HYPERFINE="$(resolve_hyperfine)"
PYTHON=""
if [ "$MODE" = "gate" ]; then
    PYTHON="$(resolve_python)"
fi

[ -x "$SCOUT_BIN" ] || fail "Missing executable Native AOT scout binary: $SCOUT_BIN"
[ -x "$RG_BIN" ] || fail "Missing executable reference rg binary: $RG_BIN"
[ -x "$HYPERFINE" ] || fail "Missing executable hyperfine binary."
if [ "$MODE" = "gate" ]; then
    [ -x "$PYTHON" ] || fail "python3 or python is required to run benchmark gates."
fi
check_file_hash "reference rg" "$RG_BIN" "$RG_SHA256"

mkdir -p "$OUT_DIR"
Q_SCOUT="$(shell_quote "$SCOUT_BIN")"
Q_RG="$(shell_quote "$RG_BIN")"
SCOUT_RSS_BASELINE_BIN="$(resolve_scout_rss_baseline_bin)"
[ -x "$SCOUT_RSS_BASELINE_BIN" ] || fail "Missing executable Native AOT RSS baseline binary: $SCOUT_RSS_BASELINE_BIN"
Q_SCOUT_RSS_BASELINE="$(shell_quote "$SCOUT_RSS_BASELINE_BIN")"
RG_RSS_FLOOR="0"
SCOUT_RSS_FLOOR="0"

if [ "$MODE" = "smoke" ]; then
    make_smoke_corpus
    make_cold_tiny_corpus
    make_bounded_assignment_corpus
    make_large_bounded_unicode_class_corpus
    Q_SINGLE="$(shell_quote "$OUT_DIR/smoke-corpus/large-single.txt")"
    Q_TREE="$(shell_quote "$OUT_DIR/smoke-corpus/many-small")"
    Q_TINY="$(shell_quote "$OUT_DIR/cold-tiny.txt")"
    Q_BOUNDED_ASSIGNMENT_PATTERN="$(shell_quote "$OUT_DIR/bounded-assignment/pattern.txt")"
    Q_BOUNDED_ASSIGNMENT_INPUT="$(shell_quote "$OUT_DIR/bounded-assignment/no-match-800.txt")"
    Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/pattern.txt")"
    Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/no-match-5000.txt")"
    RG_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "rg bounded-assignment search")"
    SCOUT_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "Scout bounded-assignment search")"
    RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "rg large bounded Unicode-class search")"
    SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "Scout large bounded Unicode-class search")"

    run_pair \
        "smoke_large_literal" \
        "1.20" \
        "$Q_RG --no-config --mmap -n 'needle' $Q_SINGLE" \
        "$Q_SCOUT --no-config --mmap -n 'needle' $Q_SINGLE"
    run_pair \
        "smoke_many_small" \
        "1.30" \
        "$Q_RG --no-config -n 'needle' $Q_TREE" \
        "$Q_SCOUT --no-config -n 'needle' $Q_TREE"
    run_pair_no_shell \
        "smoke_cold_version" \
        "1.00" \
        "$Q_RG --no-config --version" \
        "$Q_SCOUT --no-config --version"
    run_pair_no_shell \
        "smoke_cold_tiny_search" \
        "1.00" \
        "$Q_RG --no-config 'needle' $Q_TINY" \
        "$Q_SCOUT --no-config 'needle' $Q_TINY"
    run_pair \
        "bounded_assignment_no_match" \
        "1.50" \
        "$RG_BOUNDED_ASSIGNMENT_COMMAND" \
        "$SCOUT_BOUNDED_ASSIGNMENT_COMMAND"
    run_pair \
        "large_bounded_unicode_class_no_match" \
        "1.50" \
        "$RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
        "$SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND"
    exit 0
fi

make_cold_tiny_corpus
make_bounded_assignment_corpus
make_large_bounded_unicode_class_corpus
make_line_regex_corpus
OPENSUBTITLES_EN="${SCOUT_BENCH_OPENSUBTITLES_EN:-}"
LINUX_TREE="${SCOUT_BENCH_LINUX_TREE:-}"
OPENSUBTITLES_EN="$(require_gate_corpus_file "opensubtitles-en" "$OPENSUBTITLES_EN")"
LINUX_TREE="$(require_gate_corpus_tree "linux-kernel" "$LINUX_TREE")"

Q_OPEN="$(shell_quote "$OPENSUBTITLES_EN")"
Q_LINUX="$(shell_quote "$LINUX_TREE")"
Q_TINY="$(shell_quote "$OUT_DIR/cold-tiny.txt")"
Q_BOUNDED_ASSIGNMENT_PATTERN="$(shell_quote "$OUT_DIR/bounded-assignment/pattern.txt")"
Q_BOUNDED_ASSIGNMENT_INPUT="$(shell_quote "$OUT_DIR/bounded-assignment/no-match-800.txt")"
Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/pattern.txt")"
Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/no-match-5000.txt")"
Q_LINE_REGEX_INPUT="$(shell_quote "$OUT_DIR/line-regex/paladin-like-200000.txt")"
Q_LINE_REGEX_ABSENT_PATTERNS="$(shell_quote "$OUT_DIR/line-regex/absent-patterns-64.txt")"
LINE_REGEX_ABSENT_REGEXP_ARGUMENTS="$(make_absent_regexp_arguments "$OUT_DIR/line-regex/absent-patterns-64.txt")"
RG_LINE_REGEX_PREFIX="$Q_RG --no-config --threads 1 --mmap --count-matches --no-messages"
SCOUT_LINE_REGEX_PREFIX="env SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config --threads 1 --mmap --count-matches --no-messages"
RG_LINE_REGEX_LINE_COUNT_PREFIX="$Q_RG --no-config --threads 1 --mmap --count --no-messages"
SCOUT_LINE_REGEX_LINE_COUNT_PREFIX="env SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config --threads 1 --mmap --count --no-messages"
RG_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "rg bounded-assignment search")"
SCOUT_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "Scout bounded-assignment search")"
RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "rg large bounded Unicode-class search")"
SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "Scout large bounded Unicode-class search")"
RG_MANY_ABSENT_REGEXP_COMMAND="$(expect_no_match_command "$RG_LINE_REGEX_PREFIX $LINE_REGEX_ABSENT_REGEXP_ARGUMENTS $Q_LINE_REGEX_INPUT" "rg 64-pattern -e search")"
SCOUT_MANY_ABSENT_REGEXP_COMMAND="$(expect_no_match_command "$SCOUT_LINE_REGEX_PREFIX $LINE_REGEX_ABSENT_REGEXP_ARGUMENTS $Q_LINE_REGEX_INPUT" "Scout 64-pattern -e search")"
RG_MANY_ABSENT_PATTERN_FILE_COMMAND="$(expect_no_match_command "$RG_LINE_REGEX_PREFIX -f $Q_LINE_REGEX_ABSENT_PATTERNS $Q_LINE_REGEX_INPUT" "rg 64-pattern -f search")"
SCOUT_MANY_ABSENT_PATTERN_FILE_COMMAND="$(expect_no_match_command "$SCOUT_LINE_REGEX_PREFIX -f $Q_LINE_REGEX_ABSENT_PATTERNS $Q_LINE_REGEX_INPUT" "Scout 64-pattern -f search")"
OPENSUBTITLES_RUNS="$(gate_opensubtitles_runs)"
OPENSUBTITLES_WARMUP="$(gate_opensubtitles_warmup)"
TREE_RUNS="$(gate_tree_runs)"
TREE_WARMUP="$(gate_tree_warmup)"
COLD_RUNS="$(gate_cold_runs)"
COLD_WARMUP="$(gate_cold_warmup)"
BOUNDED_ASSIGNMENT_RUNS="$(gate_bounded_assignment_runs)"
BOUNDED_ASSIGNMENT_WARMUP="$(gate_bounded_assignment_warmup)"
LARGE_BOUNDED_UNICODE_CLASS_RUNS="$(gate_large_bounded_unicode_class_runs)"
LARGE_BOUNDED_UNICODE_CLASS_WARMUP="$(gate_large_bounded_unicode_class_warmup)"
LINE_REGEX_RUNS="$(gate_line_regex_runs)"
LINE_REGEX_WARMUP="$(gate_line_regex_warmup)"

printf '\n== corpus balance ==\n'
analyze_large_file_segments "subtitles_en_regex" "$OPENSUBTITLES_EN" "$GATE_LARGE_FILE_SEGMENT_BUFFER_LENGTH" "$GATE_LARGE_FILE_THREADS"
measure_rss_floor "$OPENSUBTITLES_RUNS" "$OPENSUBTITLES_WARMUP"

run_pair \
    "bounded_assignment_no_match" \
    "1.50" \
    "$RG_BOUNDED_ASSIGNMENT_COMMAND" \
    "$SCOUT_BOUNDED_ASSIGNMENT_COMMAND" \
    "$BOUNDED_ASSIGNMENT_RUNS" \
    "$BOUNDED_ASSIGNMENT_WARMUP"
run_pair \
    "large_bounded_unicode_class_no_match" \
    "1.50" \
    "$RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
    "$SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
    "$LARGE_BOUNDED_UNICODE_CLASS_RUNS" \
    "$LARGE_BOUNDED_UNICODE_CLASS_WARMUP"
run_pair \
    "line_regex_word_boundary_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "line_regex_word_boundary_line_count_general" \
    "1.50" \
    "$RG_LINE_REGEX_LINE_COUNT_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_LINE_COUNT_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "line_regex_anchored_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '^internal sealed class GeneratedRecord\\r?$' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '^internal sealed class GeneratedRecord\\r?$' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "line_regex_bounded_class_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}\\r?$' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}\\r?$' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "shared_delegate_prefix_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX 'delegate .*ShowMessageBoxHandler|delegate .*UpdateEDIEvent|delegate .*SetProgressBarValue|delegate .*ShowCheckboxMessageBoxHandler' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX 'delegate .*ShowMessageBoxHandler|delegate .*UpdateEDIEvent|delegate .*SetProgressBarValue|delegate .*ShowCheckboxMessageBoxHandler' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "many_absent_regexp_general" \
    "1.50" \
    "$RG_MANY_ABSENT_REGEXP_COMMAND" \
    "$SCOUT_MANY_ABSENT_REGEXP_COMMAND" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "many_absent_pattern_file_general" \
    "1.50" \
    "$RG_MANY_ABSENT_PATTERN_FILE_COMMAND" \
    "$SCOUT_MANY_ABSENT_PATTERN_FILE_COMMAND" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP"
run_pair \
    "subtitles_en_literal" \
    "1.20" \
    "$Q_RG --no-config --threads $GATE_LARGE_FILE_THREADS --mmap -n 'Sherlock Holmes' $Q_OPEN" \
    "$Q_SCOUT --no-config --threads $GATE_LARGE_FILE_THREADS --mmap -n 'Sherlock Holmes' $Q_OPEN" \
    "$OPENSUBTITLES_RUNS" \
    "$OPENSUBTITLES_WARMUP"
run_pair \
    "subtitles_en_regex" \
    "1.20" \
    "$Q_RG --no-config --threads $GATE_LARGE_FILE_THREADS -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN" \
    "$Q_SCOUT --no-config --threads $GATE_LARGE_FILE_THREADS -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN" \
    "$OPENSUBTITLES_RUNS" \
    "$OPENSUBTITLES_WARMUP"
run_pair \
    "linux_recursive_literal" \
    "1.25" \
    "$Q_RG --no-config -n 'PM_RESUME' $Q_LINUX" \
    "$Q_SCOUT --no-config -n 'PM_RESUME' $Q_LINUX" \
    "$TREE_RUNS" \
    "$TREE_WARMUP"
run_pair \
    "linux_heldout_regex_general" \
    "1.50" \
    "$Q_RG --no-config -n '\\b(?:struct|enum|union)\\s+[A-Za-z_][A-Za-z0-9_]*' $Q_LINUX" \
    "env SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config -n '\\b(?:struct|enum|union)\\s+[A-Za-z_][A-Za-z0-9_]*' $Q_LINUX" \
    "$TREE_RUNS" \
    "$TREE_WARMUP"
run_pair \
    "linux_heldout_capture_general" \
    "1.75" \
    "$Q_RG --no-config -n --replace '\$1 \$2' '\\b(struct|enum|union)\\s+([A-Za-z_][A-Za-z0-9_]*)' $Q_LINUX" \
    "env SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config -n --replace '\$1 \$2' '\\b(struct|enum|union)\\s+([A-Za-z_][A-Za-z0-9_]*)' $Q_LINUX" \
    "$TREE_RUNS" \
    "$TREE_WARMUP"
run_pair \
    "linux_many_small_parallel" \
    "1.30" \
    "$Q_RG --no-config -n 'struct' $Q_LINUX" \
    "$Q_SCOUT --no-config -n 'struct' $Q_LINUX" \
    "$TREE_RUNS" \
    "$TREE_WARMUP"
run_pair_no_shell \
    "cold_version" \
    "1.00" \
    "$Q_RG --no-config --version" \
    "$Q_SCOUT --no-config --version" \
    "$COLD_RUNS" \
    "$COLD_WARMUP"
run_pair_no_shell \
    "cold_tiny_search" \
    "1.00" \
    "$Q_RG --no-config 'needle' $Q_TINY" \
    "$Q_SCOUT --no-config 'needle' $Q_TINY" \
    "$COLD_RUNS" \
    "$COLD_WARMUP"
