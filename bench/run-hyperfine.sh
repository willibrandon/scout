#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
MODE="smoke"
RUNS="3"
WARMUP="1"
OUT_DIR="$ROOT/artifacts/bench/hyperfine"

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
        '  SCOUT_BENCH_OPENSUBTITLES_EN     OpenSubtitles en.txt path for --gate.' \
        '  SCOUT_BENCH_LINUX_TREE           Linux source tree path for --gate.' \
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

shell_quote() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
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

require_gate_corpus_file() {
    name="$1"
    path_value="$2"
    expected_sha256="$(read_corpus_value "$name" "sha256")" || fail "Missing corpus hash for $name in tests/PREREQS.lock."
    require_frozen_value "$expected_sha256" "corpus $name"
    [ -n "$path_value" ] || fail "Missing path for corpus $name."
    check_file_hash "corpus $name" "$path_value" "$expected_sha256"
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

hyperfine_json_mean() {
    json="$1"
    index="$2"
    awk -v want="$index" '
        $1 == "\"mean\":" {
            gsub(/,/, "", $2)
            seen++
            if (seen == want) {
                print $2
                exit 0
            }
        }
        END {
            if (seen < want) {
                exit 1
            }
        }
    ' "$json"
}

hyperfine_json_max_memory() {
    json="$1"
    index="$2"
    awk -v want="$index" '
        $1 == "\"memory_usage_byte\":" {
            seen++
            in_memory = seen == want
            max = 0
            next
        }
        in_memory && $0 ~ /\]/ {
            print max
            found = 1
            exit 0
        }
        in_memory {
            value = $1
            gsub(/,/, "", value)
            if (value ~ /^[0-9]+$/ && value > max) {
                max = value
            }
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$json"
}

check_ratio_gate() {
    name="$1"
    gate="$2"
    json="$3"
    rg_mean="$(hyperfine_json_mean "$json" 1)" || fail "Could not read rg mean from $json."
    scout_mean="$(hyperfine_json_mean "$json" 2)" || fail "Could not read scout mean from $json."
    rg_memory="$(hyperfine_json_max_memory "$json" 1)" || fail "Could not read rg memory from $json."
    scout_memory="$(hyperfine_json_max_memory "$json" 2)" || fail "Could not read scout memory from $json."
    awk -v name="$name" -v rg="$rg_mean" -v scout="$scout_mean" -v gate="$gate" '
        BEGIN {
            if (rg <= 0) {
                printf "%s: rg mean is not positive: %s\n", name, rg > "/dev/stderr"
                exit 2
            }
            ratio = scout / rg
            printf "%s ratio %.3fx (gate %.3fx)\n", name, ratio, gate
            if (ratio > gate) {
                exit 1
            }
        }
    ' || fail "$name exceeded the performance gate."
    awk -v name="$name" -v rg="$rg_memory" -v scout="$scout_memory" '
        BEGIN {
            if (rg <= 0 || scout <= 0) {
                printf "%s: missing positive memory data: rg=%s scout=%s\n", name, rg, scout > "/dev/stderr"
                exit 2
            }
            ratio = scout / rg
            printf "%s peak RSS ratio %.3fx (gate 1.500x)\n", name, ratio
            if (ratio > 1.5) {
                exit 1
            }
        }
    ' || fail "$name exceeded the peak RSS gate."
}

run_pair() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    json="$OUT_DIR/$name.json"

    printf '%s\n' "== $name"
    "$HYPERFINE" \
        --warmup "$WARMUP" \
        --runs "$RUNS" \
        --export-json "$json" \
        --command-name "rg:$name" "$rg_command" \
        --command-name "scout:$name" "$scout_command"

    if [ "$MODE" = "gate" ]; then
        check_ratio_gate "$name" "$gate" "$json"
    fi
}

list_workloads() {
    printf '%s\n' \
        'smoke_large_literal          generated single file, no release gate' \
        'smoke_many_small             generated many-small-files tree, no release gate' \
        'smoke_cold_version           scout --version vs rg --version, no release gate' \
        'subtitles_en_literal         OpenSubtitles literal scan, gate <= 1.20x' \
        'subtitles_en_regex           OpenSubtitles regex scan, gate <= 1.20x' \
        'linux_recursive_literal      Linux tree recursive walk, gate <= 1.25x' \
        'linux_many_small_parallel    Linux tree many-small-files search, gate <= 1.30x' \
        'cold_version                 cold start, gate <= 1.00x' \
        'all --gate workloads also enforce peak RSS <= 1.50x'
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
            shift 2
            ;;
        --warmup)
            [ "$#" -ge 2 ] || fail "Missing value for --warmup."
            WARMUP="$2"
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

if [ "$MODE" = "list" ]; then
    list_workloads
    exit 0
fi

RID="$(host_rid)"
DEFAULT_SCOUT_BIN="$ROOT/artifacts/bin/$RID/scout"
SCOUT_BIN="${SCOUT_BIN:-$DEFAULT_SCOUT_BIN}"
RG_BIN="$(read_lock_value "ripgrep_rg_path")" || fail "Missing ripgrep_rg_path in tests/PREREQS.lock."
RG_SHA256="$(read_lock_value "ripgrep_rg_sha256")" || fail "Missing ripgrep_rg_sha256 in tests/PREREQS.lock."
HYPERFINE="$(read_lock_table_value "tool.macos" "hyperfine" "path")" || HYPERFINE="$(command -v hyperfine || true)"

[ -x "$SCOUT_BIN" ] || fail "Missing executable Native AOT scout binary: $SCOUT_BIN"
[ -x "$RG_BIN" ] || fail "Missing executable reference rg binary: $RG_BIN"
[ -x "$HYPERFINE" ] || fail "Missing executable hyperfine binary."
check_file_hash "reference rg" "$RG_BIN" "$RG_SHA256"

mkdir -p "$OUT_DIR"
Q_SCOUT="$(shell_quote "$SCOUT_BIN")"
Q_RG="$(shell_quote "$RG_BIN")"

if [ "$MODE" = "smoke" ]; then
    make_smoke_corpus
    Q_SINGLE="$(shell_quote "$OUT_DIR/smoke-corpus/large-single.txt")"
    Q_TREE="$(shell_quote "$OUT_DIR/smoke-corpus/many-small")"

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
    run_pair \
        "smoke_cold_version" \
        "1.00" \
        "$Q_RG --no-config --version" \
        "$Q_SCOUT --no-config --version"
    exit 0
fi

OPENSUBTITLES_EN="${SCOUT_BENCH_OPENSUBTITLES_EN:-}"
LINUX_TREE="${SCOUT_BENCH_LINUX_TREE:-}"
require_gate_corpus_file "opensubtitles-en" "$OPENSUBTITLES_EN"
require_gate_corpus_file "linux-kernel" "$LINUX_TREE"
[ -d "$LINUX_TREE" ] || fail "Linux corpus is not a directory: $LINUX_TREE"

Q_OPEN="$(shell_quote "$OPENSUBTITLES_EN")"
Q_LINUX="$(shell_quote "$LINUX_TREE")"

run_pair \
    "subtitles_en_literal" \
    "1.20" \
    "$Q_RG --no-config --mmap -n 'Sherlock Holmes' $Q_OPEN" \
    "$Q_SCOUT --no-config --mmap -n 'Sherlock Holmes' $Q_OPEN"
run_pair \
    "subtitles_en_regex" \
    "1.20" \
    "$Q_RG --no-config -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN" \
    "$Q_SCOUT --no-config -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN"
run_pair \
    "linux_recursive_literal" \
    "1.25" \
    "$Q_RG --no-config -n 'PM_RESUME' $Q_LINUX" \
    "$Q_SCOUT --no-config -n 'PM_RESUME' $Q_LINUX"
run_pair \
    "linux_many_small_parallel" \
    "1.30" \
    "$Q_RG --no-config -n 'struct' $Q_LINUX" \
    "$Q_SCOUT --no-config -n 'struct' $Q_LINUX"
run_pair \
    "cold_version" \
    "1.00" \
    "$Q_RG --no-config --version" \
    "$Q_SCOUT --no-config --version"
