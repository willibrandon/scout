#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
VERSION="$(cat "$ROOT/upstream/UNICODE-VERSION")"
ARCHIVE="$ROOT/upstream/ucd/UCD-$VERSION.zip"
EXPECTED_SHA256="c86dd81f2b14a43b0cc064aa5f89aa7241386801e35c59c7984e579832634eb2"
TABLES="$ROOT/upstream/regex-syntax-0.8.8/unicode_tables"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
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

require_table_header() {
    table="$1"
    command="$2"
    path="$TABLES/$table"

    [ -f "$path" ] || fail "Missing Unicode table: $path"
    grep -F "ucd-generate $command ucd-$VERSION" "$path" >/dev/null ||
        fail "$table does not record the pinned ucd-generate command for Unicode $VERSION."
    grep -F "Unicode version: $VERSION." "$path" >/dev/null ||
        fail "$table does not record Unicode version $VERSION."
    grep -F "ucd-generate 0.3.1" "$path" >/dev/null ||
        fail "$table does not record ucd-generate 0.3.1."
}

require_archive_entry() {
    entry="$1"
    unzip -l "$ARCHIVE" "$entry" >/dev/null ||
        fail "Vendored UCD archive is missing $entry."
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

normalize_windows_text_file() {
    path="$1"

    if ! is_windows_shell; then
        return 0
    fi

    normalized="$path.lf"
    sed 's/\r$//' "$path" > "$normalized"
    mv "$normalized" "$path"
}

[ "$VERSION" = "16.0.0" ] || fail "Unexpected Unicode version: $VERSION"
[ -f "$ARCHIVE" ] || fail "Missing vendored UCD archive: $ARCHIVE"

actual_sha256="$(sha256_file "$ARCHIVE")"
[ "$actual_sha256" = "$EXPECTED_SHA256" ] ||
    fail "Expected UCD archive sha256 $EXPECTED_SHA256, got $actual_sha256"

if ! command -v unzip >/dev/null 2>&1; then
    fail "unzip is required to inspect the vendored UCD archive."
fi

PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
    if command -v python3 >/dev/null 2>&1; then
        PYTHON=python3
    elif command -v python >/dev/null 2>&1; then
        PYTHON=python
    else
        fail "python3 or python is required to regenerate Scout's regex Unicode tables."
    fi
fi

require_archive_entry "UnicodeData.txt"
require_archive_entry "CaseFolding.txt"
require_archive_entry "DerivedAge.txt"
require_archive_entry "PropList.txt"
require_archive_entry "PropertyAliases.txt"
require_archive_entry "PropertyValueAliases.txt"
require_archive_entry "Scripts.txt"
require_archive_entry "ScriptExtensions.txt"
require_archive_entry "extracted/DerivedGeneralCategory.txt"
require_archive_entry "auxiliary/GraphemeBreakProperty.txt"
require_archive_entry "auxiliary/SentenceBreakProperty.txt"
require_archive_entry "auxiliary/WordBreakProperty.txt"

require_table_header "age.rs" "age"
require_table_header "case_folding_simple.rs" "case-folding-simple"
require_table_header "general_category.rs" "general-category"
require_table_header "grapheme_cluster_break.rs" "grapheme-cluster-break"
require_table_header "perl_decimal.rs" "general-category"
require_table_header "perl_space.rs" "property-bool"
require_table_header "perl_word.rs" "perl-word"
require_table_header "property_bool.rs" "property-bool"
require_table_header "property_names.rs" "property-names"
require_table_header "property_values.rs" "property-values"
require_table_header "script.rs" "script"
require_table_header "script_extension.rs" "script-extension"
require_table_header "sentence_break.rs" "sentence-break"
require_table_header "word_break.rs" "word-break"

TMP="$ROOT/artifacts/preflight/unicode-data"
rm -rf "$TMP"
mkdir -p "$TMP"
generated="$TMP/RegexUnicodeTables.generated.cs"
actual="$TMP/RegexUnicodeTables.actual.cs"
"$PYTHON" "$ROOT/eng/generate-regex-unicode-tables.py" > "$generated"
cp "$ROOT/src/Scout.Automata/RegexUnicodeTables.cs" "$actual"
normalize_windows_text_file "$generated"
normalize_windows_text_file "$actual"
[ -f "$generated" ] || fail "Unicode verifier did not create generated output: $generated"
[ -f "$actual" ] || fail "Unicode verifier did not create actual output: $actual"
cmp "$generated" "$actual" >/dev/null ||
    fail "src/Scout.Automata/RegexUnicodeTables.cs is stale; run eng/generate-regex-unicode-tables.py."

printf 'Scout Unicode data and generated table provenance match Unicode %s.\n' "$VERSION"
