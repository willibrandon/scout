#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/msbuild-warning-gates}"

cd "$ROOT"

export MSBUILDDISABLENODEREUSE=1
dotnet build-server shutdown >/dev/null 2>&1 || true
MSBUILD_EVALUATION_TIMEOUT_SECONDS="${SCOUT_MSBUILD_WARNING_GATE_TIMEOUT_SECONDS:-300}"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

relative_path() {
    path="$1"
    case "$path" in
        "$ROOT"/*)
            printf '%s\n' "${path#"$ROOT/"}"
            ;;
        *)
            printf '%s\n' "$path"
            ;;
    esac
}

json_property() {
    property="$1"
    file="$2"
    awk -v property="$property" '
        $1 == "\"" property "\":" {
            value = $0
            sub(/^[[:space:]]*"[^"]*":[[:space:]]*"/, "", value)
            sub(/",?[[:space:]]*$/, "", value)
            print value
            found = 1
            exit 0
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$file"
}

json_item_full_paths() {
    file="$1"
    awk '
        $1 == "\"FullPath\":" {
            value = $0
            sub(/^[[:space:]]*"FullPath":[[:space:]]*"/, "", value)
            sub(/",?[[:space:]]*$/, "", value)
            print value
        }
    ' "$file"
}

normalize_json_path() {
    path="$1"
    unescaped="$(printf '%s' "$path" | sed 's/\\\\/\\/g')"
    if [ -f "$unescaped" ]; then
        printf '%s\n' "$unescaped"
        return
    fi

    if command -v cygpath >/dev/null 2>&1; then
        converted="$(cygpath -u "$unescaped" 2>/dev/null || true)"
        if [ -n "$converted" ]; then
            printf '%s\n' "$converted"
            return
        fi
    fi

    printf '%s\n' "$unescaped"
}

write_property() {
    label="$1"
    value="$2"
    property_output_file="$3"
    printf '%s=%s\n' "$label" "$value" >> "$property_output_file"
}

require_positive_integer() {
    value="$1"
    label="$2"

    case "$value" in
        ""|*[!0-9]*)
            fail "$label must be a positive integer."
            ;;
        0)
            fail "$label must be greater than zero."
            ;;
    esac
}

run_with_timeout() {
    timeout_seconds="$1"
    description="$2"
    shift 2

    "$@" &
    command_pid="$!"
    (
        sleep "$timeout_seconds"
        if kill -0 "$command_pid" 2>/dev/null; then
            printf '%s timed out after %s seconds.\n' "$description" "$timeout_seconds" >&2
            kill "$command_pid" 2>/dev/null || true
            sleep 2
            if kill -0 "$command_pid" 2>/dev/null; then
                kill -KILL "$command_pid" 2>/dev/null || true
            fi
        fi
    ) &
    watchdog_pid="$!"

    set +e
    wait "$command_pid"
    status="$?"
    set -e

    if kill -0 "$watchdog_pid" 2>/dev/null; then
        kill "$watchdog_pid" 2>/dev/null || true
    fi
    wait "$watchdog_pid" 2>/dev/null || true

    case "$status" in
        137|143)
            fail "$description timed out after $timeout_seconds seconds."
            ;;
    esac

    return "$status"
}

run_msbuild_evaluation() {
    description="$1"
    evaluation_file="$2"
    shift 2

    printf '  %s\n' "$description"
    if ! run_with_timeout "$MSBUILD_EVALUATION_TIMEOUT_SECONDS" "$description" "$@" > "$evaluation_file"; then
        fail "$description failed."
    fi
}

check_raw_nowarn() {
    raw_project="$1"
    raw_no_warn_value="$2"

    case "$raw_no_warn_value" in
        ""|"1701;1702")
            ;;
        *)
            fail "$raw_project has raw evaluated NoWarn='$raw_no_warn_value'; only the C# SDK baseline 1701;1702 may appear before repo policy is applied."
            ;;
    esac
}

check_true() {
    project="$1"
    property="$2"
    value="$3"

    if [ "$value" != "true" ]; then
        fail "$project has evaluated $property='$value', expected true."
    fi
}

check_empty() {
    project="$1"
    property="$2"
    value="$3"

    if [ -n "$value" ]; then
        fail "$project has evaluated $property='$value', expected empty."
    fi
}

require_config_severity() {
    file="$1"
    key_pattern="$2"
    label="$3"

    if ! grep -E "^$key_pattern[[:space:]]*=[[:space:]]*error($|[[:space:]#;])" "$file" >/dev/null; then
        fail "$label is not pinned to error in $(relative_path "$file")."
    fi
}

require_scout_diagnostic_severity_configs() {
    descriptors="$ROOT/src/Scout.SourceGen/DiagnosticDescriptors.cs"
    grep -Eo 'SCOUT[0-9]{4}' "$descriptors" | sort -u | while IFS= read -r diagnostic_id; do
        [ -n "$diagnostic_id" ] || continue
        require_config_severity "$ROOT/.editorconfig" "dotnet_diagnostic\\.$diagnostic_id\\.severity" "$diagnostic_id"
    done
}

scan_editor_config_file() {
    project="$1"
    config="$2"

    if grep -E 'dotnet_(analyzer_diagnostic|diagnostic)\.[^\r\n]*severity[[:space:]]*=[[:space:]]*(none|silent)($|[[:space:]#;])' "$config" >/dev/null; then
        fail "$project imports analyzer config with none/silent severity: $(relative_path "$config")."
    fi
}

scan_repository_suppression_files() {
    suppression_scan_output="$OUT/repository-suppression-scan.txt"
    : > "$suppression_scan_output"

    find "$ROOT" \
        \( -path "$ROOT/.git" -o -path "$ROOT/.git/*" -o -path "*/bin" -o -path "*/bin/*" \) -prune -o \
        \( -name '*.cs' -o -name '*.props' -o -name '*.targets' -o -name '.editorconfig' -o -name '.globalconfig' -o -name 'GlobalSuppressions.cs' \) \
        -type f -print | sort | while IFS= read -r file; do
            relative="$(relative_path "$file")"
            printf '%s\n' "$relative" >> "$suppression_scan_output"

            if [ "$(basename "$file")" = "GlobalSuppressions.cs" ]; then
                fail "$relative: GlobalSuppressions.cs files are forbidden."
            fi

            if grep -E '#pragma[[:space:]]+warning[[:space:]]+disable' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token '#pragma warning disable'."
            fi

            if grep -E '\[[[:space:]]*(Unconditional)?SuppressMessage([^[:alnum:]_]|$)' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token 'SuppressMessage'."
            fi

            if grep -E '<[[:space:]]*NoWarn([^[:alnum:]_]|$)' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token '<NoWarn'."
            fi

            if grep -E '<[[:space:]]*WarningsNotAsErrors([^[:alnum:]_]|$)' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token '<WarningsNotAsErrors'."
            fi

            if grep -E '<[[:space:]]*DisabledWarnings([^[:alnum:]_]|$)' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token '<DisabledWarnings'."
            fi

            if grep -E '#nullable[[:space:]]+disable' "$file" >/dev/null; then
                fail "$relative: forbidden repository suppression token '#nullable disable'."
            fi

            if grep -E 'dotnet_(analyzer_diagnostic|diagnostic)\.[^\r\n]*severity[[:space:]]*=[[:space:]]*(none|silent)($|[[:space:]#;])' "$file" >/dev/null; then
                fail "$relative: analyzer severity config contains none/silent."
            fi

            if grep -E 'dotnet_diagnostic\.(SCOUT[0-9]+|IDE0130)\.severity[[:space:]]*=[[:space:]]*([^e[:space:]#;]|e[^r[:space:]#;]|er[^r[:space:]#;]|err[^o[:space:]#;]|erro[^r[:space:]#;]|error[^[:space:]#;])' "$file" >/dev/null; then
                fail "$relative: Scout structural analyzers and IDE0130 must stay pinned to error."
            fi

            if grep -E 'dotnet_analyzer_diagnostic\.category-Scout\.Structure\.severity[[:space:]]*=[[:space:]]*([^e[:space:]#;]|e[^r[:space:]#;]|er[^r[:space:]#;]|err[^o[:space:]#;]|erro[^r[:space:]#;]|error[^[:space:]#;])' "$file" >/dev/null; then
                fail "$relative: Scout.Structure analyzer category must stay pinned to error."
            fi
        done
}

check_evaluated_editor_config_files() {
    project="$1"
    evaluation_output="$2"
    property_output_file="$3"
    config_list="$4"

    json_item_full_paths "$evaluation_output" > "$config_list"
    while IFS= read -r raw_config; do
        [ -n "$raw_config" ] || continue
        config="$(normalize_json_path "$raw_config")"
        write_property "EditorConfigFile" "$(relative_path "$config")" "$property_output_file"
        if [ ! -f "$config" ]; then
            fail "$project imports missing analyzer config '$raw_config'."
        fi

        scan_editor_config_file "$project" "$config"
    done < "$config_list"
}

check_analyzer_severity_config() {
    severity_output="$OUT/analyzer-severities.txt"
    : > "$severity_output"

    for config in "$ROOT/.globalconfig" "$ROOT/.editorconfig"; do
        [ -f "$config" ] || continue
        printf '# %s\n' "$(relative_path "$config")" >> "$severity_output"
        grep -E 'dotnet_(analyzer_diagnostic|diagnostic)\..*severity[[:space:]]*=' "$config" >> "$severity_output" || true
    done

    if grep -E 'dotnet_diagnostic\.[^\r\n]*severity[[:space:]]*=[[:space:]]*(none|silent)($|[[:space:]#;])' "$ROOT/.globalconfig" "$ROOT/.editorconfig" >/dev/null 2>&1; then
        fail "Analyzer severity config contains none/silent."
    fi

    require_config_severity "$ROOT/.globalconfig" 'dotnet_analyzer_diagnostic\.category-Scout\.Structure\.severity' "Scout.Structure analyzer category"
    require_config_severity "$ROOT/.editorconfig" 'dotnet_diagnostic\.IDE0130\.severity' "IDE0130"
    require_scout_diagnostic_severity_configs
}

rm -rf "$OUT"
mkdir -p "$OUT"

require_positive_integer "$MSBUILD_EVALUATION_TIMEOUT_SECONDS" "SCOUT_MSBUILD_WARNING_GATE_TIMEOUT_SECONDS"
scan_repository_suppression_files
check_analyzer_severity_config

find "$ROOT/src" "$ROOT/tests" "$ROOT/bench" "$ROOT/fuzz" -name '*.csproj' -type f | sort | while IFS= read -r project; do
    relative_project="$(relative_path "$project")"
    safe_name="$(printf '%s' "$relative_project" | tr '/\\:' '___')"
    property_output="$OUT/$safe_name.properties"
    evaluation_output="$OUT/$safe_name.evaluation.json"
    raw_evaluation_output="$OUT/$safe_name.raw-evaluation.json"
    editor_config_list="$OUT/$safe_name.editorconfig-files"
    : > "$property_output"

    printf 'Checking MSBuild warning gates for %s\n' "$relative_project"

    run_msbuild_evaluation "Raw MSBuild property evaluation for $relative_project" "$raw_evaluation_output" \
        dotnet msbuild -noAutoResponse "$project" -nologo -nodeReuse:false \
        -getProperty:NoWarn \
        -getProperty:TreatWarningsAsErrors

    run_msbuild_evaluation "Imported MSBuild property evaluation for $relative_project" "$evaluation_output" \
        dotnet msbuild "$project" -nologo -nodeReuse:false \
        -getProperty:NoWarn \
        -getProperty:WarningsNotAsErrors \
        -getProperty:TreatWarningsAsErrors \
        -getProperty:MSBuildTreatWarningsAsErrors \
        -getProperty:AnalysisLevel \
        -getProperty:AnalysisMode \
        -getProperty:EnforceCodeStyleInBuild \
        -getProperty:GenerateDocumentationFile \
        -getProperty:Nullable \
        -getProperty:LangVersion \
        -getItem:EditorConfigFiles

    raw_no_warn="$(json_property "NoWarn" "$raw_evaluation_output")"
    no_warn="$(json_property "NoWarn" "$evaluation_output")"
    warnings_not_as_errors="$(json_property "WarningsNotAsErrors" "$evaluation_output")"
    treat_warnings_as_errors="$(json_property "TreatWarningsAsErrors" "$evaluation_output")"
    msbuild_treat_warnings_as_errors="$(json_property "MSBuildTreatWarningsAsErrors" "$evaluation_output")"
    analysis_level="$(json_property "AnalysisLevel" "$evaluation_output")"
    analysis_mode="$(json_property "AnalysisMode" "$evaluation_output")"
    enforce_code_style="$(json_property "EnforceCodeStyleInBuild" "$evaluation_output")"
    generate_documentation_file="$(json_property "GenerateDocumentationFile" "$evaluation_output")"
    nullable="$(json_property "Nullable" "$evaluation_output")"
    lang_version="$(json_property "LangVersion" "$evaluation_output")"

    write_property "Project" "$relative_project" "$property_output"
    write_property "RawNoWarn" "$raw_no_warn" "$property_output"
    write_property "NoWarn" "$no_warn" "$property_output"
    write_property "WarningsNotAsErrors" "$warnings_not_as_errors" "$property_output"
    write_property "TreatWarningsAsErrors" "$treat_warnings_as_errors" "$property_output"
    write_property "MSBuildTreatWarningsAsErrors" "$msbuild_treat_warnings_as_errors" "$property_output"
    write_property "AnalysisLevel" "$analysis_level" "$property_output"
    write_property "AnalysisMode" "$analysis_mode" "$property_output"
    write_property "EnforceCodeStyleInBuild" "$enforce_code_style" "$property_output"
    write_property "GenerateDocumentationFile" "$generate_documentation_file" "$property_output"
    write_property "Nullable" "$nullable" "$property_output"
    write_property "LangVersion" "$lang_version" "$property_output"

    check_evaluated_editor_config_files "$relative_project" "$evaluation_output" "$property_output" "$editor_config_list"
    check_raw_nowarn "$relative_project" "$raw_no_warn"
    check_empty "$relative_project" "NoWarn" "$no_warn"
    check_empty "$relative_project" "WarningsNotAsErrors" "$warnings_not_as_errors"
    check_true "$relative_project" "TreatWarningsAsErrors" "$treat_warnings_as_errors"
    check_true "$relative_project" "MSBuildTreatWarningsAsErrors" "$msbuild_treat_warnings_as_errors"
    if [ "$analysis_level" != "10.0" ]; then
        fail "$relative_project has evaluated AnalysisLevel='$analysis_level', expected 10.0."
    fi

    if [ "$analysis_mode" != "AllEnabledByDefault" ]; then
        fail "$relative_project has evaluated AnalysisMode='$analysis_mode', expected AllEnabledByDefault."
    fi

    check_true "$relative_project" "EnforceCodeStyleInBuild" "$enforce_code_style"
    check_true "$relative_project" "GenerateDocumentationFile" "$generate_documentation_file"
    if [ "$nullable" != "enable" ]; then
        fail "$relative_project has evaluated Nullable='$nullable', expected enable."
    fi

    if [ "$lang_version" != "14.0" ]; then
        fail "$relative_project has evaluated LangVersion='$lang_version', expected 14.0."
    fi
done

if ! find "$OUT" -name '*.properties' -type f | grep . >/dev/null; then
    fail "No project warning-gate property dumps were produced."
fi

empty_property_dump="$(find "$OUT" -name '*.properties' -type f -size 0c | head -n 1)"
if [ -n "$empty_property_dump" ]; then
    fail "Project warning-gate property dump is empty: $(relative_path "$empty_property_dump")."
fi

printf 'MSBuild warning gates passed. Dumps written to %s\n' "$OUT"
