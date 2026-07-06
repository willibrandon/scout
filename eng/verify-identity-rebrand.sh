#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
ARTIFACTS="$ROOT/src/Scout.App/GeneratedArtifacts"
TMP="$ROOT/artifacts/preflight/identity-rebrand"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

decode_artifact() {
    tr -d '[:space:]' < "$1" | base64 -d | gzip -dc
}

require_contains() {
    file="$1"
    text="$2"
    if ! grep -F -q -- "$text" "$file"; then
        fail "Identity audit expected '$text' in $file."
    fi
}

require_absent() {
    file="$1"
    text="$2"
    if grep -F -q -- "$text" "$file"; then
        fail "Identity audit found forbidden '$text' in $file."
    fi
}

require_absent_regex() {
    file="$1"
    pattern="$2"
    if grep -E -q -- "$pattern" "$file"; then
        fail "Identity audit found forbidden pattern '$pattern' in $file."
    fi
}

rm -rf "$TMP"
mkdir -p "$TMP"

for artifact in help-short help-long man complete-bash complete-zsh complete-fish complete-powershell; do
    decode_artifact "$ARTIFACTS/$artifact.base64" > "$TMP/$artifact"
done

require_contains "$TMP/help-short" "scout 0.4.2 (ripgrep 15.1.0 compatible, rev 4857d6fa67)"
require_contains "$TMP/help-short" "Scout ports ripgrep, originally authored by Andrew Gallant."
require_contains "$TMP/help-short" "Project home page: https://github.com/willibrandon/scout"
require_contains "$TMP/help-short" "scout [OPTIONS] PATTERN [PATH ...]"
require_contains "$TMP/help-short" "Don't use .ignore, .rgignore, or .scoutignore files."
require_contains "$TMP/help-long" "SCOUT_CONFIG_PATH or RIPGREP_CONFIG_PATH"
require_contains "$TMP/man" "SCOUT_CONFIG_PATH"
require_contains "$TMP/man" "RIPGREP_CONFIG_PATH"
require_contains "$TMP/man" ".scoutignore"
require_contains "$TMP/man" "https://github.com/willibrandon/scout"
require_contains "$TMP/man" "Scout ports ripgrep, originally authored by Andrew Gallant."

require_contains "$TMP/complete-bash" "complete -F _scout -o bashdefault -o default scout"
require_contains "$TMP/complete-zsh" "#compdef scout"
require_contains "$TMP/complete-zsh" "compdef _scout scout"
require_contains "$TMP/complete-fish" "complete -c scout"
require_contains "$TMP/complete-fish" "if set -qx SCOUT_CONFIG_PATH"
require_contains "$TMP/complete-fish" "else if set -qx RIPGREP_CONFIG_PATH"
require_contains "$TMP/complete-powershell" "Register-ArgumentCompleter -Native -CommandName 'scout'"

for artifact in "$TMP"/help-short "$TMP"/help-long "$TMP"/man "$TMP"/complete-*; do
    require_absent "$artifact" "ripgrep 15.1.0 (rev 4857d6fa67)"
    require_absent "$artifact" "Project home page: https://github.com/BurntSushi/ripgrep"
    require_absent "$artifact" "https://github.com/BurntSushi/Scout"
    require_absent "$artifact" "Andrew Gallant <jamslam@gmail.com>"
    require_absent "$artifact" "Don't use .ignore or .rgignore files."
    require_absent "$artifact" "#compdef rg"
    require_absent "$artifact" "CommandName 'rg'"
    require_absent "$artifact" "complete -c rg"
    require_absent "$artifact" "complete -F _rg"
    require_absent "$artifact" "__rg"
    require_absent_regex "$artifact" '(^|[^[:alnum:]_])_rg([^[:alnum:]_]|$)'
    require_absent_regex "$artifact" '(^|[[:space:]])rg \[OPTIONS\]'
done

require_contains "$ROOT/Directory.Build.props" "<Product>Scout</Product>"
require_contains "$ROOT/Directory.Build.props" '<AssemblyInformationalVersion>$(VersionPrefix)+ripgrep.$(ScoutRipgrepVersion).$(ScoutRipgrepRevisionShort)</AssemblyInformationalVersion>'
require_contains "$ROOT/Directory.Build.targets" "BuildIdentity"
require_contains "$ROOT/native/entry/scout_main.c" "scout \" SCOUT_VERSION \" (ripgrep"
require_contains "$ROOT/eng/package-release.sh" "behavioral parity; identity is Scout-specific"
require_contains "$ROOT/eng/package-release.ps1" "behavioral parity; identity is Scout-specific"
require_contains "$ROOT/docs/PARITY.md" "intentionally Scout-specific"
require_contains "$ROOT/docs/DESIGN.md" "SCOUT_CONFIG_PATH"
require_contains "$ROOT/docs/DESIGN.md" ".scoutignore"

require_absent "$ROOT/src/Scout.Pcre2/Pcre2Library.cs" "this build of ripgrep"
require_absent "$ROOT/src/Scout.SourceGen/GeneratedArtifactSourceGenerator.cs" "compressed ripgrep artifact payload"

printf 'OK identity rebrand audit\n'
