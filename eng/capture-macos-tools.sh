#!/usr/bin/env sh
set -eu

fail() {
    printf '%s\n' "$1" >&2
    exit 1
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
        *)
            fail "Unsupported host for macOS tool capture: $os $arch"
            ;;
    esac
}

oracle_environment() {
    if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        printf 'github-actions\n'
    else
        printf 'local\n'
    fi
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

tool_path() {
    name="$1"
    path="$(command -v "$name" 2>/dev/null || true)"
    [ -n "$path" ] || fail "Missing macOS tool: $name"
    printf '%s\n' "$path"
}

tool_version() {
    name="$1"
    path="$2"

    case "$name" in
        gzip)
            "$path" --version 2>&1 | sed -n '1p'
            ;;
        bzip2)
            "$path" --version 2>&1 | sed -n '1s/.*Version \([^,]*\),.*/\1/p'
            ;;
        xz)
            "$path" --version 2>&1 | sed -n '1s/^xz (XZ Utils) //p'
            ;;
        zstd)
            "$path" --version 2>&1 | sed -n '1s/.* v\([^,]*\),.*/\1/p'
            ;;
        lz4)
            "$path" --version 2>&1 | sed -n '1s/.* v\([^ ]*\) .*/\1/p'
            ;;
        brotli)
            "$path" --version 2>&1 | awk 'NR == 1 { print $2 }'
            ;;
        uncompress)
            what "$path" 2>/dev/null | awk '
                /PROGRAM:compress[[:space:]]+PROJECT:/ {
                    sub(/^.*PROJECT:/, "", $0)
                    print "Apple compress " $0
                    exit 0
                }'
            ;;
        hyperfine)
            "$path" --version 2>&1 | awk 'NR == 1 { print $2 }'
            ;;
        *)
            fail "No version capture rule for $name"
            ;;
    esac
}

json_string_field() {
    json_path="$1"
    ruby_expression="$2"

    brew ruby -rjson -e "
        document = JSON.parse(File.read(ARGV[0]))
        formula = document.fetch('formulae').find { |item| item.fetch('name') == 'hyperfine' }
        exit 1 if formula.nil?
        value = $ruby_expression
        exit 1 if value.nil? || value == ''
        puts value
    " "$json_path"
}

capture_hyperfine_metadata() {
    [ "$1" = "hyperfine" ] || return 0
    command -v brew >/dev/null 2>&1 || return 0

    json_path="$(mktemp "${TMPDIR:-/tmp}/hyperfine-brew-info.XXXXXX.json")"
    trap 'rm -f "$json_path"' EXIT
    brew info --json=v2 hyperfine > "$json_path"

    source_url="$(json_string_field "$json_path" "formula.fetch('urls').fetch('stable').fetch('url')" || true)"
    source_sha256="$(json_string_field "$json_path" "formula.fetch('urls').fetch('stable').fetch('checksum')" || true)"
    if [ -n "$source_url" ] && [ -n "$source_sha256" ]; then
        printf 'source_url = "%s"\n' "$source_url"
        printf 'source_sha256 = "%s"\n' "$source_sha256"
    fi

    bottle_url="$(json_string_field "$json_path" "formula.fetch('bottle').fetch('stable').fetch('files').values.first&.fetch('url')" || true)"
    bottle_sha256="$(json_string_field "$json_path" "formula.fetch('bottle').fetch('stable').fetch('files').values.first&.fetch('sha256')" || true)"
    if [ -n "$bottle_url" ] && [ -n "$bottle_sha256" ]; then
        printf 'bottle_url = "%s"\n' "$bottle_url"
        printf 'bottle_sha256 = "%s"\n' "$bottle_sha256"
    fi
}

capture_tool() {
    name="$1"
    path="$(tool_path "$name")"
    version="$(tool_version "$name" "$path")"
    [ -n "$version" ] || fail "Could not determine version for $name at $path."
    sha256="$(sha256_file "$path")"

    printf '%s\n' '[[tool.macos]]'
    printf 'name = "%s"\n' "$name"
    printf 'rid = "%s"\n' "$HOST_RID"
    printf 'environment = "%s"\n' "$HOST_ENVIRONMENT"
    printf 'version = "%s"\n' "$version"
    printf 'path = "%s"\n' "$path"
    capture_hyperfine_metadata "$name"
    printf 'sha256 = "%s"\n' "$sha256"
    printf '\n'
}

[ "$(uname -s)" = "Darwin" ] || fail "macOS tool capture must run on macOS."

if ! command -v hyperfine >/dev/null 2>&1 && command -v brew >/dev/null 2>&1; then
    brew install --formula hyperfine
fi

HOST_RID="$(host_rid)"
HOST_ENVIRONMENT="$(oracle_environment)"

printf '%s\n' '--- tests/PREREQS.lock macOS tool rows ---'
capture_tool gzip
capture_tool bzip2
capture_tool xz
capture_tool zstd
capture_tool lz4
capture_tool brotli
capture_tool uncompress
capture_tool hyperfine
printf '%s\n' '--- end macOS tool rows ---'
