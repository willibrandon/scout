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

json_hyperfine_bottle_field() {
    json_path="$1"
    bottle_tag="$2"
    field="$3"

    brew ruby -rjson -e '
        document = JSON.parse(File.read(ARGV[0]))
        formula = document.fetch("formulae").find { |item| item.fetch("name") == "hyperfine" }
        exit 1 if formula.nil?
        files = formula.fetch("bottle").fetch("stable").fetch("files")
        bottle = files[ARGV[1]]
        exit 1 if bottle.nil?
        value = bottle.fetch(ARGV[2])
        exit 1 if value.nil? || value == ""
        puts value
    ' "$json_path" "$bottle_tag" "$field"
}

hyperfine_bottle_tag() {
    version="$1"
    archive="$(brew --cache --formula --force-bottle hyperfine 2>/dev/null || true)"
    [ -f "$archive" ] || return 1

    file="${archive##*/}"
    payload="${file##*--hyperfine--}"
    case "$payload" in
        "$version".*.bottle.tar.gz)
            tag="${payload#$version.}"
            tag="${tag%.bottle.tar.gz}"
            [ -n "$tag" ] || return 1
            printf '%s\n' "$tag"
            ;;
        *)
            return 1
            ;;
    esac
}

capture_hyperfine_metadata() {
    [ "$1" = "hyperfine" ] || return 0
    version="$2"
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

    bottle_tag="$(hyperfine_bottle_tag "$version" || true)"
    if [ -z "$bottle_tag" ]; then
        brew fetch --formula --force-bottle hyperfine >/dev/null 2>&1 || true
        bottle_tag="$(hyperfine_bottle_tag "$version" || true)"
    fi

    [ -n "$bottle_tag" ] || fail "Could not determine installed Homebrew bottle tag for hyperfine $version."
    bottle_url="$(json_hyperfine_bottle_field "$json_path" "$bottle_tag" "url" || true)"
    bottle_sha256="$(json_hyperfine_bottle_field "$json_path" "$bottle_tag" "sha256" || true)"
    if [ -n "$bottle_url" ] && [ -n "$bottle_sha256" ]; then
        printf 'bottle_tag = "%s"\n' "$bottle_tag"
        printf 'bottle_url = "%s"\n' "$bottle_url"
        printf 'bottle_sha256 = "%s"\n' "$bottle_sha256"
    else
        fail "Homebrew metadata did not contain bottle tag $bottle_tag for hyperfine $version."
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
    capture_hyperfine_metadata "$name" "$version"
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
