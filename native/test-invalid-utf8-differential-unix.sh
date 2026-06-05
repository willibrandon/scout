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

[ -x "$SCOUT" ] || fail "Missing executable Scout binary: $SCOUT"
command -v python3 >/dev/null 2>&1 || fail "python3 is required for invalid UTF-8 argv differential tests."

RG_VALUE="$(read_ripgrep_oracle_value "path" "ripgrep_rg_path")" || fail "Missing ripgrep oracle path in tests/PREREQS.lock."
RG="$(resolve_repo_path "$RG_VALUE")"
RG_SHA256="$(read_ripgrep_oracle_value "sha256" "ripgrep_rg_sha256")" || fail "Missing ripgrep oracle sha256 in tests/PREREQS.lock."
[ -x "$RG" ] || fail "Missing executable reference rg binary: $RG"
expect_equal "reference rg sha256" "$RG_SHA256" "$(sha256_file "$RG")"

rm -rf "$OUT"
mkdir -p "$WORK"

python3 - "$SCOUT" "$RG" "$WORK" "$OUT" <<'PY'
import os
import errno
import re
import subprocess
import sys


def try_write_bytes(path, contents):
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o666)
    except OSError as error:
        if error.errno == errno.EILSEQ:
            return False

        raise SystemExit(f"invalid UTF-8 path differential fixture creation failed: {error}") from error

    with os.fdopen(fd, "wb") as handle:
        handle.write(contents)
    return True


def write_bytes(path, contents):
    if not try_write_bytes(path, contents):
        raise SystemExit("invalid UTF-8 path differential fixture creation failed: filesystem rejected invalid byte sequences")


def normalize_elapsed(output):
    output = re.sub(
        rb'"elapsed":\{"secs":[0-9]+,"nanos":[0-9]+,"human":"[^"]+"\}',
        b'"elapsed":{"secs":0,"nanos":0,"human":"<elapsed>"}',
        output,
    )
    output = re.sub(
        rb'"elapsed":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}',
        b'"elapsed":{"human":"<elapsed>","nanos":0,"secs":0}',
        output,
    )
    output = re.sub(
        rb'"elapsed_total":\{"human":"[^"]+","nanos":[0-9]+,"secs":[0-9]+\}',
        b'"elapsed_total":{"human":"<elapsed>","nanos":0,"secs":0}',
        output,
    )
    return output


def normalize_stderr_identity(output):
    if output.startswith(b"rg: "):
        output = b"scout: " + output[len(b"rg: ") :]
    output = output.replace(b"RIPGREP_CONFIG_PATH", b"SCOUT_CONFIG_PATH")
    output = output.replace(
        b"ripgrep requires at least one pattern to execute a search",
        b"scout requires at least one pattern to execute a search",
    )
    output = output.replace(b"this build of ripgrep", b"this build of scout")
    return output


def run(tool, work, args, input_bytes=None):
    options = {
        "cwd": work,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.PIPE,
        "check": False,
    }
    if input_bytes is None:
        options["stdin"] = subprocess.DEVNULL
    else:
        options["input"] = input_bytes

    return subprocess.run([tool, *args], **options)


def compare(name, mode, scout, rg, work, out, args, input_bytes=None):
    scout_result = run(scout, work, args, input_bytes)
    rg_result = run(rg, work, args, input_bytes)

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
    if normalize_stderr_identity(scout_result.stderr) != normalize_stderr_identity(rg_result.stderr):
        raise SystemExit(f"invalid UTF-8 differential stderr mismatch for {name}")


scout = os.fsencode(sys.argv[1])
rg = os.fsencode(sys.argv[2])
work = os.fsencode(sys.argv[3])
out = sys.argv[4]

bad_name = b"foo\xffbar"
if try_write_bytes(os.path.join(work, bad_name), b"test"):
    compare("r210_explicit_invalid_utf8_path", "exact", scout, rg, work, out, [b"-H", b"test", bad_name])

    json_name = b"json\xffpath"
    write_bytes(os.path.join(work, json_name), b"quux\xffbaz")
    compare("json_explicit_invalid_utf8_path", "mask-elapsed", scout, rg, work, out, [b"--json", b"(?-u)\\xFF", json_name])

    recursive = os.path.join(work, b"notutf8")
    os.mkdir(recursive)
    recursive_name = b"foo\xffbar"
    write_bytes(os.path.join(recursive, recursive_name), b"quux\xffbaz")
    compare("json_recursive_invalid_utf8_path", "mask-elapsed", scout, rg, recursive, out, [b"--json", b"(?-u)\\xFF"])

    print("OK invalid UTF-8 path native differentials matched pinned rg")
else:
    compare("invalid_utf8_pattern_argv", "exact", scout, rg, work, out, [b"(?-u)\xff", b"-"], b"a\xffb\n")
    compare("invalid_utf8_regexp_argv", "exact", scout, rg, work, out, [b"-e", b"(?-u)\xff", b"-"], b"a\xffb\n")
    print("OK invalid UTF-8 argv native differentials matched pinned rg")
PY
