#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
RID="${1:-osx-arm64}"
SCOUT="${2:-$ROOT/artifacts/bin/$RID/scout}"
OUT="$ROOT/artifacts/native/generated-artifacts/$RID"

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

run_tool() {
    tool="$1"
    stdout="$2"
    stderr="$3"
    shift 3
    "$tool" "$@" > "$stdout" 2> "$stderr"
}

compare_case() {
    name="$1"
    kind="$2"
    shift
    shift

    scout_stdout="$OUT/$name.scout.stdout"
    scout_stderr="$OUT/$name.scout.stderr"
    rg_stdout="$OUT/$name.rg.stdout"
    rg_transformed_stdout="$OUT/$name.rg.transformed.stdout"
    rg_stderr="$OUT/$name.rg.stderr"

    if run_tool "$SCOUT" "$scout_stdout" "$scout_stderr" "$@"; then
        scout_status=0
    else
        scout_status=$?
    fi

    if run_tool "$RG" "$rg_stdout" "$rg_stderr" "$@"; then
        rg_status=0
    else
        rg_status=$?
    fi

    if [ "$scout_status" -ne "$rg_status" ]; then
        printf 'Generated artifact status mismatch for %s\n' "$name" >&2
        printf 'scout=%s rg=%s\n' "$scout_status" "$rg_status" >&2
        exit 1
    fi

    "$ROOT/eng/transform-ripgrep-artifact.sh" "$kind" < "$rg_stdout" > "$rg_transformed_stdout"

    if ! diff -u "$rg_transformed_stdout" "$scout_stdout"; then
        printf 'Generated artifact stdout mismatch for %s\n' "$name" >&2
        exit 1
    fi

    if ! diff -u "$rg_stderr" "$scout_stderr"; then
        printf 'Generated artifact stderr mismatch for %s\n' "$name" >&2
        exit 1
    fi
}

[ -x "$SCOUT" ] || fail "Missing executable Scout binary: $SCOUT"

RG_VALUE="$(read_ripgrep_oracle_value "path" "ripgrep_rg_path")" || fail "Missing ripgrep oracle path in tests/PREREQS.lock."
RG="$(resolve_repo_path "$RG_VALUE")"
RG_SHA256="$(read_ripgrep_oracle_value "sha256" "ripgrep_rg_sha256")" || fail "Missing ripgrep oracle sha256 in tests/PREREQS.lock."
[ -x "$RG" ] || fail "Missing executable reference rg binary: $RG"
expect_equal "reference rg sha256" "$RG_SHA256" "$(sha256_file "$RG")"

rm -rf "$OUT"
mkdir -p "$OUT"

compare_case help_long help-long --help
compare_case help_short help-short -h
compare_case generate_man man --generate man
compare_case generate_man_inline man --generate=man
compare_case generate_complete_bash complete-bash --generate complete-bash
compare_case generate_complete_zsh complete-zsh --generate complete-zsh
compare_case generate_complete_fish complete-fish --generate complete-fish
compare_case generate_complete_powershell complete-powershell --generate complete-powershell

printf 'OK %s: generated help/man/completion artifacts matched transformed pinned rg\n' "$RID"
