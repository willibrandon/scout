#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
ARTIFACTS="$ROOT/src/Scout.App/GeneratedArtifacts"

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

RG_PATH="${1:-}"
if [ -z "$RG_PATH" ]; then
    RG_PATH="$(read_lock_value "ripgrep_rg_path")" || fail "Missing ripgrep_rg_path in tests/PREREQS.lock."
fi

[ -f "$RG_PATH" ] || fail "Missing pinned rg: $RG_PATH"
[ -x "$RG_PATH" ] || fail "Pinned rg is not executable: $RG_PATH"

TMP="$ROOT/artifacts/preflight/generated-artifacts"
rm -rf "$TMP"
mkdir -p "$TMP"

decode_artifact() {
    tr -d '[:space:]' < "$1" | base64 -d | gzip -dc
}

is_windows_shell() {
    case "$(uname -s 2>/dev/null || printf unknown)" in
        MINGW*|MSYS*|CYGWIN*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

normalize_windows_generated_artifact_output() {
    path="$1"

    if ! is_windows_shell; then
        return 0
    fi

    normalized="$path.lf"
    sed 's/\r$//' "$path" > "$normalized"
    mv "$normalized" "$path"
}

compare_artifact() {
    name="$1"
    artifact="$2"
    shift 2
    kind="${artifact%.base64}"

    expected="$TMP/$name.expected"
    actual="$TMP/$name.actual"

    "$RG_PATH" "$@" | "$ROOT/eng/transform-ripgrep-artifact.sh" "$kind" > "$expected"
    decode_artifact "$ARTIFACTS/$artifact" > "$actual"
    normalize_windows_generated_artifact_output "$expected"
    normalize_windows_generated_artifact_output "$actual"

    [ -f "$expected" ] || fail "Generated artifact verifier did not create expected output for $name: $expected"
    [ -f "$actual" ] || fail "Generated artifact verifier did not create actual output for $name: $actual"

    if ! cmp -s "$expected" "$actual"; then
        printf 'Generated artifact drift for %s.\n' "$name" >&2
        diff -u "$expected" "$actual" | sed -n '1,120p' >&2
        exit 1
    fi

    printf 'OK %s\n' "$name"
}

compare_artifact help_short help-short.base64 -h
compare_artifact help_long help-long.base64 --help
compare_artifact generate_man man.base64 --generate man
compare_artifact generate_complete_bash complete-bash.base64 --generate complete-bash
compare_artifact generate_complete_zsh complete-zsh.base64 --generate complete-zsh
compare_artifact generate_complete_fish complete-fish.base64 --generate complete-fish
compare_artifact generate_complete_powershell complete-powershell.base64 --generate complete-powershell

printf 'Scout generated artifacts match transformed pinned rg artifacts.\n'
