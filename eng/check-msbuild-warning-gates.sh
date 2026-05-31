#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/msbuild-warning-gates}"

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
    output="$3"
    printf '%s=%s\n' "$label" "$value" >> "$output"
}

check_empty_or_sdk_default_nowarn() {
    project="$1"
    no_warn="$2"

    case "$no_warn" in
        ""|"1701;1702")
            ;;
        *)
            fail "$project has evaluated NoWarn='$no_warn'."
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

scan_editor_config_file() {
    project="$1"
    config="$2"

    if grep -E 'dotnet_(analyzer_diagnostic|diagnostic)\.[^\r\n]*severity[[:space:]]*=[[:space:]]*(none|silent)($|[[:space:]#;])' "$config" >/dev/null; then
        fail "$project imports analyzer config with none/silent severity: $(relative_path "$config")."
    fi
}

check_evaluated_editor_config_files() {
    project="$1"
    evaluation_output="$2"
    output="$3"
    config_list="$4"

    json_item_full_paths "$evaluation_output" > "$config_list"
    while IFS= read -r raw_config; do
        [ -n "$raw_config" ] || continue
        config="$(normalize_json_path "$raw_config")"
        write_property "EditorConfigFile" "$(relative_path "$config")" "$output"
        if [ ! -f "$config" ]; then
            fail "$project imports missing analyzer config '$raw_config'."
        fi

        scan_editor_config_file "$project" "$config"
    done < "$config_list"
}

check_analyzer_severity_config() {
    output="$OUT/analyzer-severities.txt"
    : > "$output"

    for config in "$ROOT/.globalconfig" "$ROOT/.editorconfig"; do
        [ -f "$config" ] || continue
        printf '# %s\n' "$(relative_path "$config")" >> "$output"
        grep -E 'dotnet_(analyzer_diagnostic|diagnostic)\..*severity[[:space:]]*=' "$config" >> "$output" || true
    done

    if grep -E 'dotnet_diagnostic\.[^\r\n]*severity[[:space:]]*=[[:space:]]*(none|silent)($|[[:space:]#;])' "$ROOT/.globalconfig" "$ROOT/.editorconfig" >/dev/null 2>&1; then
        fail "Analyzer severity config contains none/silent."
    fi

    require_config_severity "$ROOT/.globalconfig" 'dotnet_analyzer_diagnostic\.category-Scout\.Structure\.severity' "Scout.Structure analyzer category"
    require_config_severity "$ROOT/.editorconfig" 'dotnet_diagnostic\.IDE0130\.severity' "IDE0130"
    require_config_severity "$ROOT/.editorconfig" 'dotnet_diagnostic\.SCOUT0001\.severity' "SCOUT0001"
    require_config_severity "$ROOT/.editorconfig" 'dotnet_diagnostic\.SCOUT0002\.severity' "SCOUT0002"
    require_config_severity "$ROOT/.editorconfig" 'dotnet_diagnostic\.SCOUT0003\.severity' "SCOUT0003"
}

rm -rf "$OUT"
mkdir -p "$OUT"

check_analyzer_severity_config

find "$ROOT/src" "$ROOT/tests" "$ROOT/bench" "$ROOT/fuzz" -name '*.csproj' -type f | sort | while IFS= read -r project; do
    relative_project="$(relative_path "$project")"
    safe_name="$(printf '%s' "$relative_project" | tr '/\\:' '___')"
    output="$OUT/$safe_name.properties"
    evaluation_output="$OUT/$safe_name.evaluation.json"
    editor_config_list="$OUT/$safe_name.editorconfig-files"
    : > "$output"

    dotnet msbuild "$project" -nologo \
        -getProperty:NoWarn \
        -getProperty:WarningsNotAsErrors \
        -getProperty:TreatWarningsAsErrors \
        -getProperty:MSBuildTreatWarningsAsErrors \
        -getProperty:AnalysisLevel \
        -getProperty:AnalysisMode \
        -getProperty:EnforceCodeStyleInBuild \
        -getItem:EditorConfigFiles \
        > "$evaluation_output"

    no_warn="$(json_property "NoWarn" "$evaluation_output")"
    warnings_not_as_errors="$(json_property "WarningsNotAsErrors" "$evaluation_output")"
    treat_warnings_as_errors="$(json_property "TreatWarningsAsErrors" "$evaluation_output")"
    msbuild_treat_warnings_as_errors="$(json_property "MSBuildTreatWarningsAsErrors" "$evaluation_output")"
    analysis_level="$(json_property "AnalysisLevel" "$evaluation_output")"
    analysis_mode="$(json_property "AnalysisMode" "$evaluation_output")"
    enforce_code_style="$(json_property "EnforceCodeStyleInBuild" "$evaluation_output")"

    write_property "Project" "$relative_project" "$output"
    write_property "NoWarn" "$no_warn" "$output"
    write_property "WarningsNotAsErrors" "$warnings_not_as_errors" "$output"
    write_property "TreatWarningsAsErrors" "$treat_warnings_as_errors" "$output"
    write_property "MSBuildTreatWarningsAsErrors" "$msbuild_treat_warnings_as_errors" "$output"
    write_property "AnalysisLevel" "$analysis_level" "$output"
    write_property "AnalysisMode" "$analysis_mode" "$output"
    write_property "EnforceCodeStyleInBuild" "$enforce_code_style" "$output"

    check_evaluated_editor_config_files "$relative_project" "$evaluation_output" "$output" "$editor_config_list"
    check_empty_or_sdk_default_nowarn "$relative_project" "$no_warn"
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
done

if ! find "$OUT" -name '*.properties' -type f | grep . >/dev/null; then
    fail "No project warning-gate property dumps were produced."
fi

printf 'MSBuild warning gates passed. Dumps written to %s\n' "$OUT"
