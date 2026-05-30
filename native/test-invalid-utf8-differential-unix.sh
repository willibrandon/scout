#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
LOCK="$ROOT/tests/PREREQS.lock"
RID="${1:-osx-arm64}"
SCOUT="${2:-$ROOT/artifacts/bin/$RID/scout}"
OUT="$ROOT/artifacts/native/invalid-utf8-differential/$RID"
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

[ -x "$SCOUT" ] || fail "Missing executable Scout binary: $SCOUT"
command -v python3 >/dev/null 2>&1 || fail "python3 is required for invalid UTF-8 argv differential tests."

RG="$(read_lock_value "ripgrep_rg_path")" || fail "Missing ripgrep_rg_path in tests/PREREQS.lock."
RG_SHA256="$(read_lock_value "ripgrep_rg_sha256")" || fail "Missing ripgrep_rg_sha256 in tests/PREREQS.lock."
[ -x "$RG" ] || fail "Missing executable reference rg binary: $RG"
expect_equal "reference rg sha256" "$RG_SHA256" "$(sha256_file "$RG")"

rm -rf "$OUT"
mkdir -p "$WORK"

python3 - "$SCOUT" "$RG" "$WORK" "$OUT" <<'PY'
import os
import re
import subprocess
import sys


def write_bytes(path, contents):
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o666)
    except OSError as error:
        print(f"SKIP invalid UTF-8 path differentials: filesystem rejected test file: {error}")
        sys.exit(0)

    with os.fdopen(fd, "wb") as handle:
        handle.write(contents)


def normalize_elapsed(output):
    output = re.sub(
        rb'"elapsed":\{"secs":[0-9]+,"nanos":[0-9]+,"human":"[^"]+"\}',
        b'"elapsed":{"secs":0,"nanos":0,"human":"<elapsed>"}',
        output,
    )
    output = re.sub(
        rb'"elapsed_total":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}',
        b'"elapsed_total":{"human":"<elapsed>","nanos":0,"secs":0}',
        output,
    )
    return output


def run(tool, work, args):
    return subprocess.run(
        [tool, *args],
        cwd=work,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def compare(name, mode, scout, rg, work, out, args):
    scout_result = run(scout, work, args)
    rg_result = run(rg, work, args)

    for label, payload in (
        ("scout.stdout", scout_result.stdout),
        ("scout.stderr", scout_result.stderr),
        ("rg.stdout", rg_result.stdout),
        ("rg.stderr", rg_result.stderr),
    ):
        with open(os.path.join(out, f"{name}.{label}"), "wb") as handle:
            handle.write(payload)

    if scout_result.returncode != rg_result.returncode:
        raise SystemExit(
            f"invalid UTF-8 differential status mismatch for {name}: "
            f"scout={scout_result.returncode} rg={rg_result.returncode}"
        )

    scout_stdout = scout_result.stdout
    rg_stdout = rg_result.stdout
    if mode == "mask-elapsed":
        scout_stdout = normalize_elapsed(scout_stdout)
        rg_stdout = normalize_elapsed(rg_stdout)
    elif mode != "exact":
        raise SystemExit(f"unknown invalid UTF-8 comparison mode: {mode}")

    if scout_stdout != rg_stdout:
        raise SystemExit(f"invalid UTF-8 differential stdout mismatch for {name}")
    if scout_result.stderr != rg_result.stderr:
        raise SystemExit(f"invalid UTF-8 differential stderr mismatch for {name}")


scout = os.fsencode(sys.argv[1])
rg = os.fsencode(sys.argv[2])
work = os.fsencode(sys.argv[3])
out = sys.argv[4]

bad_name = b"foo\xffbar"
write_bytes(os.path.join(work, bad_name), b"test")
compare("r210_explicit_invalid_utf8_path", "exact", scout, rg, work, out, [b"-H", b"test", bad_name])

json_name = b"json\xffpath"
write_bytes(os.path.join(work, json_name), b"quux\xffbaz")
compare("json_explicit_invalid_utf8_path", "mask-elapsed", scout, rg, work, out, [b"--json", b"(?-u)\\xFF", json_name])

print("OK invalid UTF-8 native differentials matched pinned rg")
PY
