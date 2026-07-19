#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(/usr/bin/dirname -- "$0")/.." && pwd)"

if [ "${SCOUT_PERFORMANCE_GATE_INNER:-}" = "1" ]; then
    unset SCOUT_PERFORMANCE_GATE_INNER
else
    release_gate_requested="0"
    for argument in "$@"; do
        case "$argument" in
            --gate)
                release_gate_requested="1"
                ;;
        esac
    done

    if [ "$release_gate_requested" = "1" ]; then
        unset \
            BASH_ENV \
            ENV \
            SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED \
            SCOUT_PERFORMANCE_GATE_DOTNET_COMMAND \
            SCOUT_PERFORMANCE_GATE_TOOL_ENVIRONMENT
        exec /usr/bin/env -u BASHOPTS -u SHELLOPTS \
            "$ROOT/eng/run-performance-gate.sh" "$@"
    fi
fi

export LANG=C
export LC_ALL=C
export TZ=UTC
unset SCOUT_REGEX_SPECIALIZATION_MODE
LOCK="$ROOT/tests/PREREQS.lock"
MODE="smoke"
WORKLOAD=""
RUNS="3"
RUNS_SPECIFIED="0"
WARMUP="1"
WARMUP_SPECIFIED="0"
OUT_DIR="$ROOT/artifacts/bench/hyperfine"
GATE_OPENSUBTITLES_RUNS="10"
GATE_OPENSUBTITLES_WARMUP="2"
GATE_TREE_RUNS="10"
GATE_TREE_WARMUP="2"
GATE_COLD_RUNS="10"
GATE_COLD_WARMUP="2"
GATE_BOUNDED_ASSIGNMENT_RUNS="10"
GATE_BOUNDED_ASSIGNMENT_WARMUP="2"
GATE_LARGE_BOUNDED_UNICODE_CLASS_RUNS="10"
GATE_LARGE_BOUNDED_UNICODE_CLASS_WARMUP="2"
GATE_LINE_REGEX_RUNS="10"
GATE_LINE_REGEX_WARMUP="2"
GATE_GENERATED_THREADS="1"
GATE_LARGE_FILE_THREADS="4"
GATE_TREE_THREADS="3"
GATE_LARGE_FILE_SEGMENT_BUFFER_LENGTH="131072"
GATE_MANY_ABSENT_INPUT_COUNT="16"
GATE_NESTED_LITERAL_MATCH_INPUT_COUNT="2"
GATE_NESTED_LITERAL_NO_MATCH_INPUT_COUNT="4"
PERFORMANCE_GATE_FAILED_STATUS="10"
PERFORMANCE_INPUT_MANIFEST=""
PERFORMANCE_REPRO_MANIFEST=""
FAILED_GATE_WORKLOADS=""
FAILED_GATE_COUNT="0"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' \
        'usage: bench/run-hyperfine.sh [--smoke|--gate|--list] [--workload NAME] [--runs N] [--warmup N] [--output-dir DIR]' \
        '' \
        'Environment:' \
        '  SCOUT_BIN                         Native AOT scout binary. Defaults to artifacts/bin/<rid>/scout.' \
        '  SCOUT_RSS_BASELINE_BIN            Native AOT real binary for RSS-floor measurement. Defaults to sibling scout-real when present.' \
        '  SCOUT_BUILD_PROVENANCE            Native build provenance sidecar. Defaults to <scout-real>.provenance.' \
        '  SCOUT_BENCH_OPENSUBTITLES_EN     OpenSubtitles en.txt path for --gate.' \
        '  SCOUT_BENCH_LINUX_TREE           Linux source tree path for --gate.' \
        '  SCOUT_HYPERFINE_BIN               Absolute Hyperfine binary. Hash and version must match tests/PREREQS.lock.' \
        '  SCOUT_ORACLE_ENVIRONMENT          Pinned rg oracle environment: github-actions or local. Gate default: github-actions.' \
        '  SCOUT_TOOL_ENVIRONMENT            Pinned host-tool environment: github-actions or local. Defaults from the host.' \
        '' \
        'The --gate mode requires frozen corpus hashes in tests/PREREQS.lock.' \
        'Use --gate --workload NAME to run one listed release-gate workload.'
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

read_lock_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v key="$3" "$strip_toml_value"'
        $0 == header {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            matched = value_of($0) == name
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

oracle_environment() {
    if [ -n "${SCOUT_ORACLE_ENVIRONMENT:-}" ]; then
        case "$SCOUT_ORACLE_ENVIRONMENT" in
            github-actions|local)
                printf '%s\n' "$SCOUT_ORACLE_ENVIRONMENT"
                return
                ;;
            *)
                fail "Unsupported SCOUT_ORACLE_ENVIRONMENT for pinned ripgrep oracle: $SCOUT_ORACLE_ENVIRONMENT"
                ;;
        esac
    fi

    if [ "$MODE" = "gate" ] || [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        printf 'github-actions\n'
    else
        printf 'local\n'
    fi
}

tool_environment() {
    if [ -n "${SCOUT_TOOL_ENVIRONMENT:-}" ]; then
        case "$SCOUT_TOOL_ENVIRONMENT" in
            github-actions|local)
                printf '%s\n' "$SCOUT_TOOL_ENVIRONMENT"
                return
                ;;
            *)
                fail "Unsupported SCOUT_TOOL_ENVIRONMENT for pinned host tools: $SCOUT_TOOL_ENVIRONMENT"
                ;;
        esac
    fi

    if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        printf 'github-actions\n'
    else
        printf 'local\n'
    fi
}

read_lock_rid_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v rid="$3" -v environment="$4" -v key="$5" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_rid == rid && table_value != "" &&
                ((environment == "" && table_environment == "") || (environment != "" && table_environment == environment))) {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_rid = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*rid[[:space:]]*=/ {
            table_rid = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_lock_environment_table_value() {
    awk -v header="[[${1}]]" -v name="$2" -v environment="$3" -v key="$4" "$strip_toml_value"'
        function reset_table() {
            in_table = 0
            table_name = ""
            table_environment = ""
            table_value = ""
        }
        function maybe_emit() {
            if (found) {
                return
            }
            if (in_table && table_name == name && table_environment == environment && table_value != "") {
                print table_value
                found = 1
                exit 0
            }
        }
        $0 == header {
            maybe_emit()
            in_table = 1
            table_name = ""
            table_environment = ""
            table_value = ""
            next
        }
        in_table && $0 ~ /^\[/ {
            maybe_emit()
            reset_table()
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            table_name = value_of($0)
            next
        }
        in_table && $0 ~ /^[[:space:]]*environment[[:space:]]*=/ {
            table_environment = value_of($0)
            next
        }
        in_table && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            table_value = value_of($0)
            next
        }
        END {
            maybe_emit()
            if (!found) {
                exit 1
            }
        }
    ' "$LOCK"
}

read_macos_tool_value() {
    name="$1"
    key="$2"

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$RID" "$HOST_TOOL_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_rid_table_value "tool.macos" "$name" "$RID" "" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    if value="$(read_lock_environment_table_value "tool.macos" "$name" "$HOST_TOOL_ENVIRONMENT" "$key")"; then
        printf '%s\n' "$value"
        return 0
    fi

    read_lock_table_value "tool.macos" "$name" "$key"
}

read_corpus_value() {
    awk -v name="$1" -v key="$2" "$strip_toml_value"'
        $0 == "[[corpus]]" {
            in_table = 1
            matched = 0
            next
        }
        in_table && $0 ~ /^\[\[/ {
            in_table = 0
            matched = 0
        }
        in_table && $0 ~ /^[[:space:]]*name[[:space:]]*=/ {
            matched = value_of($0) == name
            next
        }
        in_table && matched && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
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

read_provenance_value() {
    provenance_path="$1"
    provenance_key="$2"
    awk -F= -v key="$provenance_key" '
        $1 == key {
            sub(/^[^=]*=/, "")
            print
            found = 1
            exit
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$provenance_path"
}

validate_scout_build_provenance() {
    SCOUT_BUILD_PROVENANCE="$(printenv SCOUT_BUILD_PROVENANCE 2>/dev/null || true)"
    if [ -z "$SCOUT_BUILD_PROVENANCE" ]; then
        SCOUT_BUILD_PROVENANCE="$SCOUT_RSS_BASELINE_BIN.provenance"
    fi
    [ -f "$SCOUT_BUILD_PROVENANCE" ] ||
        fail "Missing Scout build provenance: $SCOUT_BUILD_PROVENANCE. Rebuild with native/build-app-unix.sh."

    SCOUT_SOURCE_COMMIT="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" source_commit)" ||
        fail "Scout build provenance has no source commit."
    SCOUT_SOURCE_FINGERPRINT="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" source_fingerprint)" ||
        fail "Scout build provenance has no source fingerprint."
    SCOUT_SOURCE_DIRTY="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" source_dirty)" ||
        fail "Scout build provenance has no dirty-state marker."
    SCOUT_BUILD_DOTNET_SDK="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" dotnet_sdk)" ||
        fail "Scout build provenance has no .NET SDK."
    SCOUT_BUILD_DOTNET_HOST_RUNTIME="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" dotnet_host_runtime)" ||
        fail "Scout build provenance has no .NET host runtime."
    SCOUT_BUILD_NATIVEAOT_RUNTIME="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" runtime_framework_version)" ||
        fail "Scout build provenance has no Native AOT runtime framework."
    SCOUT_BUILD_COMPILER="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" compiler)" ||
        fail "Scout build provenance has no compiler."
    SCOUT_BUILD_COMPILER_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" compiler_sha256)" ||
        fail "Scout build provenance has no compiler hash."
    SCOUT_BUILD_XCODE_VERSION="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" xcode_version)" ||
        fail "Scout build provenance has no Xcode version."
    SCOUT_BUILD_XCODE_BUILD="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" xcode_build)" ||
        fail "Scout build provenance has no Xcode build."
    SCOUT_BUILD_MACOS_SDK="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" macos_sdk)" ||
        fail "Scout build provenance has no macOS SDK."
    SCOUT_BUILD_MACOS_DEPLOYMENT_TARGET="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" macos_deployment_target)" ||
        fail "Scout build provenance has no macOS deployment target."
    SCOUT_BUILD_LINKER="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" linker)" ||
        fail "Scout build provenance has no linker."
    SCOUT_BUILD_LINKER_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" linker_sha256)" ||
        fail "Scout build provenance has no linker hash."
    SCOUT_BUILD_ARCHIVER_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" archiver_sha256)" ||
        fail "Scout build provenance has no archiver hash."
    SCOUT_BUILD_RANLIB_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" ranlib_sha256)" ||
        fail "Scout build provenance has no ranlib hash."
    SCOUT_BUILD_STRIP_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" strip_sha256)" ||
        fail "Scout build provenance has no strip hash."
    SCOUT_BUILD_NM_SHA256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" nm_sha256)" ||
        fail "Scout build provenance has no nm hash."
    expected_launcher_sha256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" launcher_sha256)" ||
        fail "Scout build provenance has no launcher hash."
    expected_payload_sha256="$(read_provenance_value "$SCOUT_BUILD_PROVENANCE" payload_sha256)" ||
        fail "Scout build provenance has no payload hash."

    actual_source_fingerprint="$(sh "$ROOT/eng/source-fingerprint.sh")"
    [ "$actual_source_fingerprint" = "$SCOUT_SOURCE_FINGERPRINT" ] ||
        fail "Scout Native AOT payload is stale for the current source content. Rebuild with native/build-app-unix.sh."
    [ "$SCOUT_SOURCE_DIRTY" = "0" ] ||
        fail "Scout Native AOT payload was built from dirty source inputs."
    [ "$SCOUT_BUILD_DOTNET_SDK" = "$(read_lock_value dotnet_sdk)" ] ||
        fail "Scout Native AOT payload was built by an unpinned .NET SDK."
    [ "$SCOUT_BUILD_DOTNET_HOST_RUNTIME" = "$(read_lock_value dotnet_host_runtime)" ] ||
        fail "Scout Native AOT payload was built by an unpinned .NET host runtime."
    [ "$SCOUT_BUILD_NATIVEAOT_RUNTIME" = "$(read_lock_value nativeaot_runtime_framework)" ] ||
        fail "Scout Native AOT payload was built with an unpinned runtime framework."
    [ "$SCOUT_BUILD_XCODE_VERSION" = "$(read_lock_value xcode_version)" ] ||
        fail "Scout Native AOT payload was built by an unpinned Xcode version."
    [ "$SCOUT_BUILD_XCODE_BUILD" = "$(read_lock_value xcode_build)" ] ||
        fail "Scout Native AOT payload was built by an unpinned Xcode build."
    [ "$SCOUT_BUILD_MACOS_SDK" = "$(read_lock_value macos_sdk)" ] ||
        fail "Scout Native AOT payload was built against an unpinned macOS SDK."
    [ "$SCOUT_BUILD_MACOS_DEPLOYMENT_TARGET" = "$(read_lock_value macos_deployment_target)" ] ||
        fail "Scout Native AOT payload has an unpinned macOS deployment target."
    [ "$SCOUT_BUILD_COMPILER" = "Apple clang version $(read_lock_value apple_clang)" ] ||
        fail "Scout Native AOT payload was built by an unpinned Apple Clang."
    [ "$SCOUT_BUILD_LINKER" = "$(read_lock_value apple_ld)" ] ||
        fail "Scout Native AOT payload was linked by an unpinned Apple ld."
    [ "$SCOUT_BUILD_COMPILER_SHA256" = "$(read_lock_value apple_clang_sha256)" ] ||
        fail "Scout Native AOT compiler hash does not match the prerequisite lock."
    [ "$SCOUT_BUILD_LINKER_SHA256" = "$(read_lock_value apple_ld_sha256)" ] ||
        fail "Scout Native AOT linker hash does not match the prerequisite lock."
    [ "$SCOUT_BUILD_ARCHIVER_SHA256" = "$(read_lock_value apple_ar_sha256)" ] ||
        fail "Scout Native AOT archiver hash does not match the prerequisite lock."
    [ "$SCOUT_BUILD_RANLIB_SHA256" = "$(read_lock_value apple_ranlib_sha256)" ] ||
        fail "Scout Native AOT ranlib hash does not match the prerequisite lock."
    [ "$SCOUT_BUILD_STRIP_SHA256" = "$(read_lock_value apple_strip_sha256)" ] ||
        fail "Scout Native AOT strip hash does not match the prerequisite lock."
    [ "$SCOUT_BUILD_NM_SHA256" = "$(read_lock_value apple_nm_sha256)" ] ||
        fail "Scout Native AOT nm hash does not match the prerequisite lock."
    check_file_hash "Scout launcher provenance" "$SCOUT_BIN" "$expected_launcher_sha256"
    check_file_hash "Scout payload provenance" "$SCOUT_RSS_BASELINE_BIN" "$expected_payload_sha256"
}

sha256_stream() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum | awk '{ print $1 }'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 -r | awk '{ print $1 }'
    else
        fail "No SHA-256 tool found."
    fi
}

sha256_tree() {
    tree="$1"
    [ -d "$tree" ] || fail "Missing tree corpus: $tree"
    (
        cd "$tree"
        if command -v shasum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 shasum -a 256
        elif command -v sha256sum >/dev/null 2>&1; then
            find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum
        else
            find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | while IFS= read -r relative_path; do
                file_sha256="$(sha256_file "$relative_path")"
                printf '%s  %s\n' "$file_sha256" "$relative_path"
            done
        fi | sed 's#  \./#  #'
    ) | sha256_stream
}

logical_cpu_count() {
    if command -v getconf >/dev/null 2>&1; then
        count="$(getconf _NPROCESSORS_ONLN 2>/dev/null || true)"
        case "$count" in
            ''|*[!0-9]*)
                ;;
            *)
                printf '%s\n' "$count"
                return
                ;;
        esac
    fi

    if command -v sysctl >/dev/null 2>&1; then
        count="$(sysctl -n hw.logicalcpu 2>/dev/null || true)"
        case "$count" in
            ''|*[!0-9]*)
                ;;
            *)
                printf '%s\n' "$count"
                return
                ;;
        esac
    fi

    printf 'unknown\n'
}

print_repro_manifest() {
    manifest_os="$(uname -s)"
    manifest_arch="$(uname -m)"
    manifest_cpu_count="$(logical_cpu_count)"
    manifest_os_version="$(sw_vers -productVersion 2>/dev/null || uname -r)"
    manifest_os_build="$(sw_vers -buildVersion 2>/dev/null || printf 'unknown')"
    manifest_hardware_model="$(sysctl -n hw.model 2>/dev/null || printf 'unknown')"
    manifest_runner_name="${SCOUT_PERFORMANCE_GATE_RUNNER_NAME:-local}"
    manifest_runner_image_os="${SCOUT_PERFORMANCE_GATE_IMAGE_OS:-local}"
    manifest_runner_image_version="${SCOUT_PERFORMANCE_GATE_IMAGE_VERSION:-local}"
    manifest_process_umask="${PERFORMANCE_PROCESS_UMASK:-unmanaged}"
    manifest_process_soft_nofile="${PERFORMANCE_PROCESS_SOFT_NOFILE:-unmanaged}"
    manifest_process_nice="${PERFORMANCE_PROCESS_NICE:-unmanaged}"
    manifest_rg_version="$("$RG_BIN" --version | sed -n '1p')"
    manifest_rg_sha256="$(sha256_file "$RG_BIN")"
    manifest_scout_version="$("$SCOUT_BIN" --version | sed -n '1p')"
    manifest_scout_sha256="$(sha256_file "$SCOUT_BIN")"
    manifest_scout_payload_sha256="$(sha256_file "$SCOUT_RSS_BASELINE_BIN")"
    manifest_scout_provenance_sha256="$(sha256_file "$SCOUT_BUILD_PROVENANCE")"
    manifest_script_sha256="$(sha256_file "$ROOT/bench/run-hyperfine.sh")"
    manifest_harness_fingerprint="$(sh "$ROOT/eng/performance-harness-fingerprint.sh")"
    manifest_harness_commit="$(git -c safe.directory="$ROOT" -C "$ROOT" rev-parse HEAD)"
    manifest_harness_dirty="$(performance_inputs_dirty)"
    manifest_hyperfine_version="$("$HYPERFINE" --version | sed -n '1p')"
    manifest_hyperfine_sha256="$(sha256_file "$HYPERFINE")"
    manifest_opensubtitles_sha256="$(read_corpus_value "opensubtitles-en" "sha256")"
    manifest_linux_tree_sha256="$(read_corpus_value "linux-kernel" "tree_sha256")"
    manifest_performance_inputs_sha256="$(sha256_file "$PERFORMANCE_INPUT_MANIFEST")"
    manifest_selection="${WORKLOAD:-all gate workloads}"
    PERFORMANCE_REPRO_MANIFEST="$OUT_DIR/reproducibility.json"

    "$PYTHON" "$ROOT/bench/write_performance_manifest.py" \
        --output "$PERFORMANCE_REPRO_MANIFEST" \
        --value "host.os=$manifest_os" \
        --value "host.os_version=$manifest_os_version" \
        --value "host.os_build=$manifest_os_build" \
        --value "host.architecture=$manifest_arch" \
        --value "host.hardware_model=$manifest_hardware_model" \
        --value "host.logical_cpu_count=$manifest_cpu_count" \
        --value "runner.name=$manifest_runner_name" \
        --value "runner.image_os=$manifest_runner_image_os" \
        --value "runner.image_version=$manifest_runner_image_version" \
        --value "process.umask=$manifest_process_umask" \
        --value "process.soft_nofile=$manifest_process_soft_nofile" \
        --value "process.nice=$manifest_process_nice" \
        --value "selection.workload=$manifest_selection" \
        --value "source.commit=$SCOUT_SOURCE_COMMIT" \
        --value "source.fingerprint=$SCOUT_SOURCE_FINGERPRINT" \
        --value "source.dirty=$SCOUT_SOURCE_DIRTY" \
        --value "harness.commit=$manifest_harness_commit" \
        --value "harness.fingerprint=$manifest_harness_fingerprint" \
        --value "harness.dirty=$manifest_harness_dirty" \
        --value "harness.script_sha256=$manifest_script_sha256" \
        --value "generated_inputs.manifest_sha256=$manifest_performance_inputs_sha256" \
        --value "oracle.environment=$HOST_ORACLE_ENVIRONMENT" \
        --value "host_tools.environment=$HOST_TOOL_ENVIRONMENT" \
        --value "tool.rg_version=$manifest_rg_version" \
        --value "tool.rg_sha256=$manifest_rg_sha256" \
        --value "tool.scout_version=$manifest_scout_version" \
        --value "tool.scout_launcher_sha256=$manifest_scout_sha256" \
        --value "tool.scout_payload_sha256=$manifest_scout_payload_sha256" \
        --value "tool.scout_provenance_sha256=$manifest_scout_provenance_sha256" \
        --value "tool.hyperfine_version=$manifest_hyperfine_version" \
        --value "tool.hyperfine_sha256=$manifest_hyperfine_sha256" \
        --value "toolchain.dotnet_sdk=$SCOUT_BUILD_DOTNET_SDK" \
        --value "toolchain.dotnet_host_runtime=$SCOUT_BUILD_DOTNET_HOST_RUNTIME" \
        --value "toolchain.nativeaot_runtime=$SCOUT_BUILD_NATIVEAOT_RUNTIME" \
        --value "toolchain.xcode_version=$SCOUT_BUILD_XCODE_VERSION" \
        --value "toolchain.xcode_build=$SCOUT_BUILD_XCODE_BUILD" \
        --value "toolchain.macos_sdk=$SCOUT_BUILD_MACOS_SDK" \
        --value "toolchain.macos_deployment_target=$SCOUT_BUILD_MACOS_DEPLOYMENT_TARGET" \
        --value "toolchain.compiler=$SCOUT_BUILD_COMPILER" \
        --value "toolchain.compiler_sha256=$SCOUT_BUILD_COMPILER_SHA256" \
        --value "toolchain.linker=$SCOUT_BUILD_LINKER" \
        --value "toolchain.linker_sha256=$SCOUT_BUILD_LINKER_SHA256" \
        --value "toolchain.archiver_sha256=$SCOUT_BUILD_ARCHIVER_SHA256" \
        --value "toolchain.ranlib_sha256=$SCOUT_BUILD_RANLIB_SHA256" \
        --value "toolchain.strip_sha256=$SCOUT_BUILD_STRIP_SHA256" \
        --value "toolchain.nm_sha256=$SCOUT_BUILD_NM_SHA256" \
        --value "corpus.opensubtitles_sha256=$manifest_opensubtitles_sha256" \
        --value "corpus.linux_tree_sha256=$manifest_linux_tree_sha256" \
        --value "threads.generated=$GATE_GENERATED_THREADS" \
        --value "threads.line_regex=1" \
        --value "threads.opensubtitles=$GATE_LARGE_FILE_THREADS" \
        --value "threads.linux_tree=$GATE_TREE_THREADS"

    printf '\n== reproducibility ==\n'
    printf 'host: %s %s (%s) on %s/%s; logical CPUs: %s\n' \
        "$manifest_os" "$manifest_os_version" "$manifest_os_build" \
        "$manifest_hardware_model" "$manifest_arch" "$manifest_cpu_count"
    printf 'runner: %s; image OS=%s; image version=%s\n' \
        "$manifest_runner_name" "$manifest_runner_image_os" "$manifest_runner_image_version"
    printf 'process state: umask=%s; soft nofile=%s; nice=%s\n' \
        "$manifest_process_umask" "$manifest_process_soft_nofile" "$manifest_process_nice"
    printf 'selection: %s\n' "$manifest_selection"
    printf 'rg: %s; sha256: %s; oracle environment: %s\n' \
        "$manifest_rg_version" "$manifest_rg_sha256" "$HOST_ORACLE_ENVIRONMENT"
    printf 'host-tool environment: %s\n' "$HOST_TOOL_ENVIRONMENT"
    printf 'Scout: %s; launcher sha256: %s; payload sha256: %s\n' \
        "$manifest_scout_version" "$manifest_scout_sha256" "$manifest_scout_payload_sha256"
    printf 'Scout source: commit=%s; fingerprint=%s; dirty=%s\n' \
        "$SCOUT_SOURCE_COMMIT" "$SCOUT_SOURCE_FINGERPRINT" "$SCOUT_SOURCE_DIRTY"
    printf 'Scout build: .NET SDK %s; host runtime %s; Native AOT runtime %s; Xcode %s (%s); macOS SDK %s; deployment target %s\n' \
        "$SCOUT_BUILD_DOTNET_SDK" "$SCOUT_BUILD_DOTNET_HOST_RUNTIME" "$SCOUT_BUILD_NATIVEAOT_RUNTIME" \
        "$SCOUT_BUILD_XCODE_VERSION" "$SCOUT_BUILD_XCODE_BUILD" \
        "$SCOUT_BUILD_MACOS_SDK" "$SCOUT_BUILD_MACOS_DEPLOYMENT_TARGET"
    printf 'Scout native tools: compiler=%s [%s]; linker=%s [%s]; provenance sha256=%s\n' \
        "$SCOUT_BUILD_COMPILER" "$SCOUT_BUILD_COMPILER_SHA256" \
        "$SCOUT_BUILD_LINKER" "$SCOUT_BUILD_LINKER_SHA256" \
        "$manifest_scout_provenance_sha256"
    printf 'harness: script sha256=%s; %s sha256=%s\n' \
        "$manifest_script_sha256" "$manifest_hyperfine_version" "$manifest_hyperfine_sha256"
    printf 'performance inputs: commit=%s; fingerprint=%s; dirty=%s\n' \
        "$manifest_harness_commit" "$manifest_harness_fingerprint" "$manifest_harness_dirty"
    printf 'corpus pins: OpenSubtitles sha256=%s; Linux tree sha256=%s\n' \
        "$manifest_opensubtitles_sha256" "$manifest_linux_tree_sha256"
    printf 'generated inputs: %s; manifest sha256=%s\n' \
        "$PERFORMANCE_INPUT_MANIFEST" "$manifest_performance_inputs_sha256"
    printf 'reproducibility manifest: %s\n' "$PERFORMANCE_REPRO_MANIFEST"
    printf 'fixed threads: generated=%s; line regex=1; OpenSubtitles=4; Linux tree=%s\n' \
        "$GATE_GENERATED_THREADS" "$GATE_TREE_THREADS"
    printf 'sampling: OpenSubtitles=%s warm-up/%s measured; Linux tree=%s/%s; cold=%s/%s; generated=%s/%s; line regex=%s/%s\n' \
        "$OPENSUBTITLES_WARMUP" "$OPENSUBTITLES_RUNS" \
        "$TREE_WARMUP" "$TREE_RUNS" \
        "$COLD_WARMUP" "$COLD_RUNS" \
        "$BOUNDED_ASSIGNMENT_WARMUP" "$BOUNDED_ASSIGNMENT_RUNS" \
        "$LINE_REGEX_WARMUP" "$LINE_REGEX_RUNS"
    printf 'repeated input arguments: many-absent=%s; nested-literal-match=%s; nested-literal-no-match=%s\n' \
        "$GATE_MANY_ABSENT_INPUT_COUNT" \
        "$GATE_NESTED_LITERAL_MATCH_INPUT_COUNT" \
        "$GATE_NESTED_LITERAL_NO_MATCH_INPUT_COUNT"
    if [ -n "$WORKLOAD" ]; then
        printf 'scope: focused diagnosis; run --gate without --workload for the release-gate sequence\n'
    else
        printf 'scope: complete release-gate sequence\n'
    fi
    printf 'commands: exact rg and Scout argv are stored in each aggregate JSON\n'
}

performance_inputs_dirty() {
    if [ -n "$(git -c safe.directory="$ROOT" -C "$ROOT" status --porcelain=v1 --untracked-files=normal -- \
        .editorconfig \
        .gitattributes \
        .globalconfig \
        .github/workflows/release-gates.yml \
        bench \
        Directory.Build.props \
        Directory.Build.rsp \
        Directory.Build.targets \
        Directory.Packages.props \
        eng \
        global.json \
        native \
        NuGet.Config \
        Scout.slnx \
        src \
        tests/PREREQS.lock)" ]; then
        printf '1\n'
    else
        printf '0\n'
    fi
}

shell_quote() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
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

read_ripgrep_oracle_value() {
    sh "$ROOT/eng/read-ripgrep-oracle.sh" "$1" "$2"
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
        Linux:x86_64)
            printf 'linux-x64\n'
            ;;
        Linux:aarch64|Linux:arm64)
            printf 'linux-arm64\n'
            ;;
        *)
            fail "Unsupported benchmark host: $os/$arch"
            ;;
    esac
}

require_frozen_value() {
    value="$1"
    label="$2"
    case "$value" in
        resolved@*)
            fail "$label is not frozen in tests/PREREQS.lock: $value"
            ;;
        "")
            fail "$label is empty in tests/PREREQS.lock"
            ;;
    esac
}

check_file_hash() {
    label="$1"
    path="$2"
    expected="$3"
    require_frozen_value "$expected" "$label sha256"
    [ -f "$path" ] || fail "Missing $label: $path"
    actual="$(sha256_file "$path")"
    [ "$actual" = "$expected" ] || fail "$label hash mismatch: expected $expected, got $actual"
}

check_tree_hash() {
    label="$1"
    path="$2"
    expected="$3"
    require_frozen_value "$expected" "$label tree_sha256"
    [ -d "$path" ] || fail "Missing $label tree: $path"
    actual="$(sha256_tree "$path")"
    [ "$actual" = "$expected" ] || fail "$label tree hash mismatch: expected $expected, got $actual"
}

check_tool_version() {
    label="$1"
    path="$2"
    expected="$3"
    actual="$("$path" --version | sed -n '1p')"
    [ "$actual" = "$expected" ] || fail "$label version mismatch: expected $expected, got $actual"
}

resolve_hyperfine() {
    pinned_path="$(read_macos_tool_value "hyperfine" "path")" || pinned_path=""
    pinned_sha256="$(read_macos_tool_value "hyperfine" "sha256")" || pinned_sha256=""
    pinned_version="$(read_macos_tool_value "hyperfine" "version")" || pinned_version=""
    configured_path="${SCOUT_HYPERFINE_BIN:-}"

    if [ -n "$configured_path" ]; then
        case "$configured_path" in
            /*)
                ;;
            *)
                fail "SCOUT_HYPERFINE_BIN must be an absolute path."
                ;;
        esac
        [ -n "$pinned_sha256" ] || fail "Missing pinned hyperfine hash in tests/PREREQS.lock."
        [ -n "$pinned_version" ] || fail "Missing pinned hyperfine version in tests/PREREQS.lock."
        [ -x "$configured_path" ] || fail "SCOUT_HYPERFINE_BIN is not executable: $configured_path"
        check_file_hash "hyperfine" "$configured_path" "$pinned_sha256"
        check_tool_version "hyperfine" "$configured_path" "hyperfine $pinned_version"
        printf '%s\n' "$configured_path"
        return
    fi

    if [ "$MODE" = "gate" ]; then
        [ -n "$pinned_path" ] || fail "Missing pinned hyperfine path in tests/PREREQS.lock."
        [ -n "$pinned_sha256" ] || fail "Missing pinned hyperfine hash in tests/PREREQS.lock."
        [ -n "$pinned_version" ] || fail "Missing pinned hyperfine version in tests/PREREQS.lock."
        check_file_hash "hyperfine" "$pinned_path" "$pinned_sha256"
        check_tool_version "hyperfine" "$pinned_path" "hyperfine $pinned_version"
        printf '%s\n' "$pinned_path"
        return
    fi

    if [ -n "$pinned_path" ] && [ -x "$pinned_path" ]; then
        if [ -n "$pinned_sha256" ]; then
            check_file_hash "hyperfine" "$pinned_path" "$pinned_sha256"
        fi
        if [ -n "$pinned_version" ]; then
            check_tool_version "hyperfine" "$pinned_path" "hyperfine $pinned_version"
        fi
        printf '%s\n' "$pinned_path"
        return
    fi

    command -v hyperfine || true
}

resolve_python() {
    command -v python3 || command -v python || true
}

require_gate_corpus_file() {
    name="$1"
    path_value="$2"
    expected_sha256="$(read_corpus_value "$name" "sha256")" || fail "Missing corpus hash for $name in tests/PREREQS.lock."
    require_frozen_value "$expected_sha256" "corpus $name"
    if [ -z "$path_value" ]; then
        path_value="$(read_corpus_value "$name" "path")" || fail "Missing path for corpus $name."
        path_value="$(resolve_repo_path "$path_value")"
    fi
    check_file_hash "corpus $name" "$path_value" "$expected_sha256"
    printf '%s\n' "$path_value"
}

require_gate_corpus_tree() {
    name="$1"
    path_value="$2"
    expected_sha256="$(read_corpus_value "$name" "tree_sha256")" || fail "Missing tree hash for $name in tests/PREREQS.lock."
    require_frozen_value "$expected_sha256" "corpus $name"
    if [ -z "$path_value" ]; then
        path_value="$(read_corpus_value "$name" "tree_path")" || fail "Missing tree_path for corpus $name."
        path_value="$(resolve_repo_path "$path_value")"
    fi
    check_tree_hash "corpus $name" "$path_value" "$expected_sha256"
    printf '%s\n' "$path_value"
}

make_smoke_corpus() {
    smoke_dir="$OUT_DIR/smoke-corpus"
    single_file="$smoke_dir/large-single.txt"
    tree_dir="$smoke_dir/many-small"
    mkdir -p "$tree_dir"

    if [ ! -f "$single_file" ]; then
        awk 'BEGIN { for (i = 0; i < 50000; i++) print "alpha beta gamma needle delta epsilon" }' > "$single_file"
    fi

    index=0
    while [ "$index" -lt 240 ]; do
        file="$tree_dir/file-$index.txt"
        if [ ! -f "$file" ]; then
            awk -v n="$index" 'BEGIN { for (i = 0; i < 24; i++) print "file " n " has a needle and a haystack" }' > "$file"
        fi
        index=$((index + 1))
    done
}

make_cold_tiny_corpus() {
    tiny_file="$OUT_DIR/cold-tiny.txt"
    printf '%s\n' 'needle' > "$tiny_file"
}

make_bounded_assignment_corpus() {
    bounded_assignment_dir="$OUT_DIR/bounded-assignment"
    bounded_assignment_pattern="$bounded_assignment_dir/pattern.txt"
    bounded_assignment_input="$bounded_assignment_dir/no-match-800.txt"
    mkdir -p "$bounded_assignment_dir"

    cat > "$bounded_assignment_pattern" <<'EOF'
(?i)[\w.-]{0,50}?(?:bitbucket)(?:[ \t\w.-]{0,20})[\s'"]{0,3}(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)[\x60'"\s=]{0,5}([a-z0-9]{32})(?:[\x60'"\s;]|\\[nr]|$)
EOF
    awk 'BEGIN { for (i = 0; i < 800; i++) print "bitbucket repository setting without a credential" }' > "$bounded_assignment_input"
}

make_large_bounded_unicode_class_corpus() {
    large_bounded_unicode_class_dir="$OUT_DIR/large-bounded-unicode-class"
    large_bounded_unicode_class_pattern="$large_bounded_unicode_class_dir/pattern.txt"
    large_bounded_unicode_class_input="$large_bounded_unicode_class_dir/no-match-5000.txt"
    mkdir -p "$large_bounded_unicode_class_dir"

    cat > "$large_bounded_unicode_class_pattern" <<'EOF'
x[\w-]{50,1000}
EOF
    awk 'BEGIN { for (i = 0; i < 5000; i++) printf "x[%cw-]{50,1000}\n", 92 }' > "$large_bounded_unicode_class_input"
}

make_line_regex_corpus() {
    line_regex_dir="$OUT_DIR/line-regex"
    line_regex_input="$line_regex_dir/paladin-like-200000.txt"
    line_regex_absent_patterns="$line_regex_dir/absent-patterns-64.txt"
    mkdir -p "$line_regex_dir"

    awk 'BEGIN {
        for (i = 0; i < 200000; i++) {
            printf "alpha bravo charl delta eagle foxtt and unrelated symbols.\r\n"
            printf "internal sealed class GeneratedRecord\r\n"
            printf "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_abcdefghijklmnopqrstuvwxyz_\r\n"
            printf "internal sealed class OtherRecord { private readonly int _state; }\r\n"
        }

        printf "internal sealed class PaladinRecord\r\n"
        printf "internal sealed class PaladinValue\r\n"
        printf "    public delegate bool ShowMessageBoxHandler(string message, string caption, bool buttons);\r\n"
        printf "    public delegate bool ShowCheckboxMessageBoxHandler(string message, string caption, bool buttons);\r\n"
        printf "    public delegate void SetProgressBarValue(int percentComplete, int currentValue);\r\n"
        printf "    public delegate void UpdateEDIEvent(string eventString);\r\n"
    }' > "$line_regex_input"
    awk 'BEGIN {
        for (i = 0; i < 64; i++) {
            printf "issue44_absent_pattern_%03d\n", i
        }
    }' > "$line_regex_absent_patterns"
}

verify_generated_performance_inputs() {
    PERFORMANCE_INPUT_MANIFEST="$OUT_DIR/generated-performance-inputs.json"
    "$PYTHON" "$ROOT/bench/verify_performance_inputs.py" \
        --lock "$LOCK" \
        --output "$PERFORMANCE_INPUT_MANIFEST" \
        --input "cold-tiny=$OUT_DIR/cold-tiny.txt" \
        --input "bounded-assignment-pattern=$OUT_DIR/bounded-assignment/pattern.txt" \
        --input "bounded-assignment-no-match-800=$OUT_DIR/bounded-assignment/no-match-800.txt" \
        --input "large-bounded-unicode-class-pattern=$OUT_DIR/large-bounded-unicode-class/pattern.txt" \
        --input "large-bounded-unicode-class-no-match-5000=$OUT_DIR/large-bounded-unicode-class/no-match-5000.txt" \
        --input "line-regex-paladin-like-200000=$OUT_DIR/line-regex/paladin-like-200000.txt" \
        --input "line-regex-absent-patterns-64=$OUT_DIR/line-regex/absent-patterns-64.txt"
}

make_absent_regexp_arguments() {
    awk '{ printf " -e %s", $0 }' "$1"
}

repeat_shell_argument() {
    repeated_argument="$1"
    repeated_count="$2"
    repeated_arguments=""
    repeated_index=0
    while [ "$repeated_index" -lt "$repeated_count" ]; do
        repeated_arguments="$repeated_arguments $repeated_argument"
        repeated_index=$((repeated_index + 1))
    done

    printf '%s\n' "$repeated_arguments"
}

expect_no_match_command() {
    no_match_command="$1"
    no_match_label="$2"
    no_match_message="$(shell_quote "$no_match_label unexpectedly matched.")"
    printf '%s; status=$?; case $status in 1) exit 0 ;; 0) printf "%%s\\n" %s >&2; exit 125 ;; *) exit $status ;; esac' \
        "$no_match_command" \
        "$no_match_message"
}

hyperfine_json_samples() {
    json="$1"
    index="$2"
    metric="$3"
    "$PYTHON" - "$json" "$index" "$metric" <<'PY'
import json
import sys

path = sys.argv[1]
index = int(sys.argv[2]) - 1
metric = sys.argv[3]

with open(path, encoding="utf-8") as handle:
    document = json.load(handle)

try:
    value = document["results"][index][metric]
except (IndexError, KeyError):
    sys.exit(1)

if isinstance(value, list):
    for item in value:
        print(item)
else:
    print(value)
PY
}

hyperfine_json_memory_samples() {
    json="$1"
    index="$2"
    hyperfine_json_samples "$json" "$index" "memory_usage_byte"
}

median_numbers() {
    awk '
        BEGIN {
            count = 0
        }
        /^[0-9]+([.][0-9]+)?$/ {
            count++
            values[count] = $1 + 0
        }
        END {
            if (count == 0) {
                exit 1
            }

            for (i = 1; i <= count; i++) {
                for (j = i + 1; j <= count; j++) {
                    if (values[j] < values[i]) {
                        tmp = values[i]
                        values[i] = values[j]
                        values[j] = tmp
                    }
                }
            }

            middle = int(count / 2)
            if (count % 2 == 1) {
                printf "%.12g\n", values[middle + 1]
            } else {
                printf "%.12g\n", (values[middle] + values[middle + 1]) / 2
            }
        }
    '
}

hyperfine_json_median_memory() {
    hyperfine_json_memory_samples "$1" "$2" | median_numbers | awk '{ printf "%.0f\n", $1 }'
}

resolve_scout_rss_baseline_bin() {
    if [ -n "${SCOUT_RSS_BASELINE_BIN:-}" ]; then
        printf '%s\n' "$SCOUT_RSS_BASELINE_BIN"
        return
    fi

    scout_dir="$(dirname -- "$SCOUT_BIN")"
    scout_name="$(basename -- "$SCOUT_BIN")"
    if [ "$scout_name" = "scout" ] && [ -x "$scout_dir/scout-real" ]; then
        printf '%s\n' "$scout_dir/scout-real"
        return
    fi

    printf '%s\n' "$SCOUT_BIN"
}

measure_rss_floor() {
    runs="$1"
    warmup="$2"
    json="$OUT_DIR/rss_floor.json"

    printf '\n== rss_floor ==\n'
    run_hyperfine_interleaved \
        "$json" \
        "rss_floor" \
        "$Q_RG --no-config --mmap -n 'needle' $Q_TINY" \
        "$Q_SCOUT_RSS_BASELINE --no-config --mmap -n 'needle' $Q_TINY" \
        "$runs" \
        "$warmup" \
        "$ROOT" \
        "0" \
        ""

    RG_RSS_FLOOR="$(hyperfine_json_median_memory "$json" 1)" || fail "Could not read rg RSS floor from $json."
    SCOUT_RSS_FLOOR="$(hyperfine_json_median_memory "$json" 2)" || fail "Could not read scout RSS floor from $json."
    [ -n "$RG_RSS_FLOOR" ] || fail "Could not read rg RSS floor from $json."
    [ -n "$SCOUT_RSS_FLOOR" ] || fail "Could not read scout RSS floor from $json."
    printf '\nRSS floor:\n'
    printf '  rg      %d bytes\n' "$RG_RSS_FLOOR"
    printf '  Scout  %d bytes\n' "$SCOUT_RSS_FLOOR"
}

analyze_large_file_segments() {
    name="$1"
    path="$2"
    buffer_length="$3"
    thread_count="$4"
    "$PYTHON" - "$name" "$path" "$buffer_length" "$thread_count" <<'PY'
import statistics
import sys

name = sys.argv[1]
path = sys.argv[2]
buffer_length = int(sys.argv[3])
thread_count = int(sys.argv[4])
terminator = b"\n"
carry = b""
segment_lengths = []
line_counts = []

with open(path, "rb", buffering=0) as handle:
    while True:
        read_length = buffer_length - len(carry)
        if read_length <= 0:
            segment_lengths.append(len(carry))
            line_counts.append(carry.count(terminator))
            carry = b""
            read_length = buffer_length

        chunk = handle.read(read_length)
        if not chunk:
            break

        combined = carry + chunk
        last_terminator = combined.rfind(terminator)
        if last_terminator < 0:
            carry = combined
            continue

        segment = combined[: last_terminator + 1]
        segment_lengths.append(len(segment))
        line_counts.append(segment.count(terminator))
        carry = combined[last_terminator + 1 :]

if carry:
    segment_lengths.append(len(carry))
    line_counts.append(max(1, carry.count(terminator)))

if not segment_lengths:
    print(f"{name} segment balance: no segments")
    sys.exit(1)

balanced_lengths = segment_lengths[:-1] if len(segment_lengths) > 1 else segment_lengths
balanced_lines = line_counts[:-1] if len(line_counts) > 1 else line_counts
median_length = statistics.median(balanced_lengths)
median_lines = statistics.median(balanced_lines)
max_length = max(balanced_lengths)
max_lines = max(balanced_lines)
length_ratio = max_length / median_length if median_length else float("inf")
line_ratio = max_lines / median_lines if median_lines else float("inf")
print(
    f"{name} segment balance: segments={len(segment_lengths)} "
    f"buffer={buffer_length} bytes median={median_length:.0f} max={max_length} "
    f"max/median={length_ratio:.3f} line-max/median={line_ratio:.3f} threads={thread_count}"
)

if len(segment_lengths) < thread_count * 4:
    print(f"{name}: too few segments for stable {thread_count}-thread large-file timing", file=sys.stderr)
    sys.exit(1)

if length_ratio > 1.10:
    print(f"{name}: uneven byte segment split: max/median={length_ratio:.3f}", file=sys.stderr)
    sys.exit(1)

PY
}

gate_opensubtitles_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_OPENSUBTITLES_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_opensubtitles_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_OPENSUBTITLES_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_tree_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_TREE_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_tree_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_TREE_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_cold_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_COLD_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_cold_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_COLD_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_bounded_assignment_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_BOUNDED_ASSIGNMENT_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_bounded_assignment_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_BOUNDED_ASSIGNMENT_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_large_bounded_unicode_class_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LARGE_BOUNDED_UNICODE_CLASS_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_large_bounded_unicode_class_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LARGE_BOUNDED_UNICODE_CLASS_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

gate_line_regex_runs() {
    if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LINE_REGEX_RUNS"
        return
    fi

    printf '%s\n' "$RUNS"
}

gate_line_regex_warmup() {
    if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "0" ]; then
        printf '%s\n' "$GATE_LINE_REGEX_WARMUP"
        return
    fi

    printf '%s\n' "$WARMUP"
}

report_interleaved_gate() {
    report_gate_name="$1"
    report_gate_limit="$2"
    report_gate_json="$3"
    report_gate_status="0"

    "$PYTHON" "$ROOT/bench/hyperfine_gate.py" \
        --input "$report_gate_json" \
        --wall-limit "$report_gate_limit" \
        --scout-rss-floor "$SCOUT_RSS_FLOOR" \
        --workload "$report_gate_name" || report_gate_status="$?"

    case "$report_gate_status" in
        0)
            return 0
            ;;
        10|11|12)
            return "$PERFORMANCE_GATE_FAILED_STATUS"
            ;;
        *)
            fail "Could not evaluate the Hyperfine gate from $report_gate_json."
            ;;
    esac
}

run_hyperfine_pair() {
    no_shell="$1"
    pair_json="$2"
    first_name="$3"
    first_command="$4"
    second_name="$5"
    second_command="$6"
    runs="$7"
    warmup="$8"

    if [ "$no_shell" = "1" ]; then
        "$HYPERFINE" \
            -N \
            --warmup "$warmup" \
            --runs "$runs" \
            --export-json "$pair_json" \
            --command-name "$first_name" "$first_command" \
            --command-name "$second_name" "$second_command"
        return
    fi

    "$HYPERFINE" \
        --warmup "$warmup" \
        --runs "$runs" \
        --export-json "$pair_json" \
        --command-name "$first_name" "$first_command" \
        --command-name "$second_name" "$second_command"
}

run_hyperfine_interleaved() {
    interleaved_json="$1"
    interleaved_name="$2"
    interleaved_rg_command="$3"
    interleaved_scout_command="$4"
    interleaved_runs="$5"
    interleaved_warmup="$6"
    interleaved_working_directory="$7"
    interleaved_expected_exit_code="$8"
    interleaved_environment="${9:-}"

    set -- \
        "$PYTHON" "$ROOT/bench/hyperfine_interleaved.py" \
        --hyperfine "$HYPERFINE" \
        --name "$interleaved_name" \
        --rg-command "$interleaved_rg_command" \
        --scout-command "$interleaved_scout_command" \
        --rounds "$interleaved_runs" \
        --warmup-rounds "$interleaved_warmup" \
        --output "$interleaved_json" \
        --working-directory "$interleaved_working_directory" \
        --expected-exit-code "$interleaved_expected_exit_code" \
        --performance-input-manifest "$PERFORMANCE_INPUT_MANIFEST" \
        --reproducibility-manifest "$PERFORMANCE_REPRO_MANIFEST"
    if [ -n "$interleaved_environment" ]; then
        set -- "$@" --environment "$interleaved_environment"
    fi
    "$@"
}

run_gate_pair_impl() {
    gate_name="$1"
    gate_limit="$2"
    gate_rg_command="$3"
    gate_scout_command="$4"
    gate_runs="$5"
    gate_warmup="$6"
    gate_working_directory="$7"
    gate_expected_exit_code="$8"
    gate_environment="${9:-}"
    gate_output_policy="${10:-equivalent}"
    gate_json="$OUT_DIR/$gate_name.json"

    case "$gate_output_policy" in
        equivalent|independent)
            ;;
        *)
            fail "Unsupported output policy for $gate_name: $gate_output_policy."
            ;;
    esac

    printf '\n== %s ==\n' "$gate_name"
    printf 'Limits: wall %.3fx; RSS 1.500x rg + Native AOT floor\n' "$gate_limit"
    if [ -n "$WORKLOAD" ]; then
        printf 'Commands:\n'
        printf '  working directory: %s\n' "$gate_working_directory"
        if [ -n "$gate_environment" ]; then
            printf '  environment: %s\n' "$gate_environment"
        fi
        printf '  expected exit code: %s\n' "$gate_expected_exit_code"
        printf '  output policy: %s\n' "$gate_output_policy"
        printf '  rg: %s\n' "$gate_rg_command"
        printf '  Scout: %s\n' "$gate_scout_command"
    fi

    printf 'Output equivalence:\n'
    set -- \
        "$PYTHON" "$ROOT/bench/verify_hyperfine_output.py" \
        --workload "$gate_name" \
        --rg-command "$gate_rg_command" \
        --scout-command "$gate_scout_command" \
        --output "$OUT_DIR/$gate_name.output.json" \
        --working-directory "$gate_working_directory" \
        --expected-exit-code "$gate_expected_exit_code" \
        --output-policy "$gate_output_policy" \
        --performance-input-manifest "$PERFORMANCE_INPUT_MANIFEST" \
        --reproducibility-manifest "$PERFORMANCE_REPRO_MANIFEST"
    if [ -n "$gate_environment" ]; then
        set -- "$@" --environment "$gate_environment"
    fi
    if ! "$@"; then
        fail "Output verification failed for $gate_name."
    fi
    if ! run_hyperfine_interleaved \
        "$gate_json" \
        "$gate_name" \
        "$gate_rg_command" \
        "$gate_scout_command" \
        "$gate_runs" \
        "$gate_warmup" \
        "$gate_working_directory" \
        "$gate_expected_exit_code" \
        "$gate_environment"; then
        fail "Hyperfine sampling failed for $gate_name."
    fi

    gate_report_status="0"
    report_interleaved_gate \
        "$gate_name" "$gate_limit" "$gate_json" || gate_report_status="$?"
    case "$gate_report_status" in
        0)
            return 0
            ;;
        "$PERFORMANCE_GATE_FAILED_STATUS")
            return "$PERFORMANCE_GATE_FAILED_STATUS"
            ;;
        *)
            fail "Could not evaluate the Hyperfine gate from $gate_json."
            ;;
    esac
}

run_gate_pair() {
    gate_name="$1"
    if ! workload_selected "$gate_name"; then
        return 0
    fi

    set +e
    run_gate_pair_impl "$@"
    gate_status="$?"
    set -e
    case "$gate_status" in
        0)
            ;;
        "$PERFORMANCE_GATE_FAILED_STATUS")
            FAILED_GATE_COUNT=$((FAILED_GATE_COUNT + 1))
            FAILED_GATE_WORKLOADS="$FAILED_GATE_WORKLOADS $gate_name"
            ;;
        *)
            exit "$gate_status"
            ;;
    esac
}

run_pair_impl() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="$5"
    warmup="$6"
    no_shell="$7"
    json="$OUT_DIR/$name.json"

    [ "$MODE" = "smoke" ] || fail "Internal error: smoke pair used outside smoke mode."

    if ! workload_selected "$name"; then
        return 0
    fi

    printf '\n== %s ==\n' "$name"
    printf 'Limits: wall %.3fx; RSS 1.500x rg + Native AOT floor\n' "$gate"
    if [ -n "$WORKLOAD" ]; then
        printf 'Commands:\n'
        printf '  rg: %s\n' "$rg_command"
        printf '  Scout: %s\n' "$scout_command"
    fi
    run_hyperfine_pair "$no_shell" "$json" "rg:$name" "$rg_command" "scout:$name" "$scout_command" "$runs" "$warmup"
}

run_pair() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="${5:-$RUNS}"
    warmup="${6:-$WARMUP}"

    run_pair_impl "$name" "$gate" "$rg_command" "$scout_command" "$runs" "$warmup" "0"
}

run_pair_no_shell() {
    name="$1"
    gate="$2"
    rg_command="$3"
    scout_command="$4"
    runs="${5:-$RUNS}"
    warmup="${6:-$WARMUP}"

    run_pair_impl "$name" "$gate" "$rg_command" "$scout_command" "$runs" "$warmup" "1"
}

is_gate_workload() {
    case "$1" in
        bounded_assignment_no_match|\
        large_bounded_unicode_class_no_match|\
        line_regex_word_boundary_general|\
        line_regex_word_boundary_line_count_general|\
        line_regex_generated_record_word_boundary_general|\
        line_regex_anchored_general|\
        line_regex_bounded_class_general|\
        line_regex_bounded_class_exact_general|\
        nested_literal_alternation_match_general|\
        nested_literal_alternation_no_match_general|\
        shared_delegate_prefix_general|\
        many_absent_regexp_general|\
        many_absent_pattern_file_general|\
        subtitles_en_literal|\
        subtitles_en_regex|\
        linux_recursive_literal|\
        linux_heldout_regex_general|\
        linux_heldout_capture_general|\
        linux_many_small_parallel|\
        cold_version|\
        cold_tiny_search)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

workload_selected() {
    [ -z "$WORKLOAD" ] || [ "$WORKLOAD" = "$1" ]
}

workload_group_selected() {
    if [ -z "$WORKLOAD" ]; then
        return 0
    fi

    while [ "$#" -gt 0 ]; do
        if [ "$WORKLOAD" = "$1" ]; then
            return 0
        fi
        shift
    done

    return 1
}

list_workloads() {
    printf '%s\n' \
        'smoke_large_literal          generated single file, no release gate' \
        'smoke_many_small             generated many-small-files tree, no release gate' \
        'smoke_cold_version           scout --version vs rg --version, no release gate' \
        'smoke_cold_tiny_search       cold tiny search, no release gate' \
        'bounded_assignment_no_match  generated 800-candidate issue #30 scan, gate <= 1.50x' \
        'large_bounded_unicode_class_no_match generated 5,000-candidate issue #32 scan in general mode, gate <= 1.50x' \
        'line_regex_word_boundary_general generated issue #37 prefilter-free word-boundary match count in general mode, gate <= 1.50x' \
        'line_regex_word_boundary_line_count_general generated issue #37 prefilter-free word-boundary line count in general mode, gate <= 1.50x' \
        'line_regex_generated_record_word_boundary_general generated issue #37 exact GeneratedRecord word-boundary scan in general mode, gate <= 1.50x' \
        'line_regex_anchored_general  generated issue #37 anchored-line scan in general mode, gate <= 1.50x' \
        'line_regex_bounded_class_general generated issue #37 bounded-class scan in general mode, gate <= 1.50x' \
        'line_regex_bounded_class_exact_general generated issue #37 exact bounded-class scan in general mode, gate <= 1.50x' \
        'nested_literal_alternation_match_general generated issue #46 nested finite-language match scan in general mode, gate <= 1.50x' \
        'nested_literal_alternation_no_match_general generated issue #46 nested finite-language no-match scan in general mode, gate <= 1.50x' \
        'shared_delegate_prefix_general generated issue #36 shared-prefix alternation in general mode, gate <= 1.50x' \
        'many_absent_regexp_general  generated issue #44 64-pattern -e scan in general mode, gate <= 1.50x' \
        'many_absent_pattern_file_general generated issue #44 64-pattern -f scan in general mode, gate <= 1.50x' \
        'subtitles_en_literal         OpenSubtitles literal scan, gate <= 1.20x' \
        'subtitles_en_regex           OpenSubtitles regex scan, gate <= 1.20x' \
        'linux_recursive_literal      Linux tree recursive walk, gate <= 1.25x' \
        'linux_heldout_regex_general  Linux tree held-out regex scan with Scout general-only mode, gate <= 1.50x' \
        'linux_heldout_capture_general Linux tree held-out replacement/capture scan with Scout general-only mode, gate <= 1.75x' \
        'linux_many_small_parallel    Linux tree many-small-files search, gate <= 1.30x' \
        'cold_version                 cold start, gate <= 1.00x' \
        'cold_tiny_search             cold tiny search, gate <= 1.00x' \
        'Peak RSS allows the measured Native AOT fixed RSS floor from docs/PARITY.md'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --smoke)
            MODE="smoke"
            shift
            ;;
        --gate)
            MODE="gate"
            shift
            ;;
        --list)
            MODE="list"
            shift
            ;;
        --workload)
            [ "$#" -ge 2 ] || fail "Missing value for --workload."
            [ -z "$WORKLOAD" ] || fail "--workload may be specified only once."
            WORKLOAD="$2"
            shift 2
            ;;
        --runs)
            [ "$#" -ge 2 ] || fail "Missing value for --runs."
            RUNS="$2"
            RUNS_SPECIFIED="1"
            shift 2
            ;;
        --warmup)
            [ "$#" -ge 2 ] || fail "Missing value for --warmup."
            WARMUP="$2"
            WARMUP_SPECIFIED="1"
            shift 2
            ;;
        --output-dir)
            [ "$#" -ge 2 ] || fail "Missing value for --output-dir."
            OUT_DIR="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: $1"
            ;;
    esac
done

if [ -n "$WORKLOAD" ]; then
    [ "$MODE" = "gate" ] || fail "--workload requires --gate."
    is_gate_workload "$WORKLOAD" || fail "Unknown release-gate workload: $WORKLOAD. Run --list for valid names."
fi

case "$RUNS" in
    ''|*[!0-9]*|0*)
        fail "--runs must be a positive integer."
        ;;
esac

case "$WARMUP" in
    ''|*[!0-9]*|0[0-9]*)
        fail "--warmup must be a non-negative integer."
        ;;
esac

if [ "$MODE" = "gate" ] && [ "$RUNS_SPECIFIED" = "1" ] && [ $((RUNS % 2)) -ne 0 ]; then
    fail "--gate requires an even --runs value for complete ABBA/BAAB cycles."
fi

if [ "$MODE" = "gate" ] && [ "$WARMUP_SPECIFIED" = "1" ] && [ $((WARMUP % 2)) -ne 0 ]; then
    fail "--gate requires an even --warmup value for complete ABBA/BAAB cycles."
fi

if [ "$MODE" = "list" ]; then
    list_workloads
    exit 0
fi

if [ "$MODE" = "gate" ] && [ -z "$WORKLOAD" ] && [ "$(performance_inputs_dirty)" != "0" ]; then
    fail "The complete release-equivalent gate requires committed and clean performance inputs. Use --workload for dirty-tree diagnosis."
fi

RID="$(host_rid)"
HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"
HOST_TOOL_ENVIRONMENT="$(tool_environment)"
export SCOUT_HOST_RID="$RID"
export SCOUT_ORACLE_ENVIRONMENT="$HOST_ORACLE_ENVIRONMENT"
export SCOUT_TOOL_ENVIRONMENT="$HOST_TOOL_ENVIRONMENT"
DEFAULT_SCOUT_BIN="$ROOT/artifacts/bin/$RID/scout"
SCOUT_BIN="${SCOUT_BIN:-$DEFAULT_SCOUT_BIN}"
RG_VALUE="$(read_ripgrep_oracle_value "path" "ripgrep_rg_path")" || fail "Missing ripgrep oracle path in tests/PREREQS.lock."
RG_BIN="$(resolve_repo_path "$RG_VALUE")"
RG_SHA256="$(read_ripgrep_oracle_value "sha256" "ripgrep_rg_sha256")" || fail "Missing ripgrep oracle sha256 in tests/PREREQS.lock."
HYPERFINE="$(resolve_hyperfine)"
unset SCOUT_HYPERFINE_BIN
PYTHON=""
if [ "$MODE" = "gate" ]; then
    PYTHON="$(resolve_python)"
fi

[ -x "$SCOUT_BIN" ] || fail "Missing executable Native AOT scout binary: $SCOUT_BIN"
[ -x "$RG_BIN" ] || fail "Missing executable reference rg binary: $RG_BIN"
[ -x "$HYPERFINE" ] || fail "Missing executable hyperfine binary."
if [ "$MODE" = "gate" ]; then
    [ -x "$PYTHON" ] || fail "python3 or python is required to run benchmark gates."
fi
check_file_hash "reference rg" "$RG_BIN" "$RG_SHA256"

mkdir -p "$OUT_DIR"
Q_SCOUT="$(shell_quote "$SCOUT_BIN")"
Q_RG="$(shell_quote "$RG_BIN")"
SCOUT_RSS_BASELINE_BIN="$(resolve_scout_rss_baseline_bin)"
[ -x "$SCOUT_RSS_BASELINE_BIN" ] || fail "Missing executable Native AOT RSS baseline binary: $SCOUT_RSS_BASELINE_BIN"
Q_SCOUT_RSS_BASELINE="$(shell_quote "$SCOUT_RSS_BASELINE_BIN")"
if [ "$MODE" = "gate" ]; then
    validate_scout_build_provenance
fi
RG_RSS_FLOOR="0"
SCOUT_RSS_FLOOR="0"

if [ "$MODE" = "smoke" ]; then
    make_smoke_corpus
    make_cold_tiny_corpus
    make_bounded_assignment_corpus
    make_large_bounded_unicode_class_corpus
    Q_SINGLE="$(shell_quote "$OUT_DIR/smoke-corpus/large-single.txt")"
    Q_TREE="$(shell_quote "$OUT_DIR/smoke-corpus/many-small")"
    Q_TINY="$(shell_quote "$OUT_DIR/cold-tiny.txt")"
    Q_BOUNDED_ASSIGNMENT_PATTERN="$(shell_quote "$OUT_DIR/bounded-assignment/pattern.txt")"
    Q_BOUNDED_ASSIGNMENT_INPUT="$(shell_quote "$OUT_DIR/bounded-assignment/no-match-800.txt")"
    Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/pattern.txt")"
    Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/no-match-5000.txt")"
    RG_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "rg bounded-assignment search")"
    SCOUT_BOUNDED_ASSIGNMENT_COMMAND="$(expect_no_match_command "$Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT" "Scout bounded-assignment search")"
    RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "$Q_RG --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "rg large bounded Unicode-class search")"
    SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$(expect_no_match_command "SCOUT_REGEX_SPECIALIZATION_MODE=general $Q_SCOUT --no-config -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT" "Scout large bounded Unicode-class search")"

    run_pair \
        "smoke_large_literal" \
        "1.20" \
        "$Q_RG --no-config --mmap -n 'needle' $Q_SINGLE" \
        "$Q_SCOUT --no-config --mmap -n 'needle' $Q_SINGLE"
    run_pair \
        "smoke_many_small" \
        "1.30" \
        "$Q_RG --no-config -n 'needle' $Q_TREE" \
        "$Q_SCOUT --no-config -n 'needle' $Q_TREE"
    run_pair_no_shell \
        "smoke_cold_version" \
        "1.00" \
        "$Q_RG --no-config --version" \
        "$Q_SCOUT --no-config --version"
    run_pair_no_shell \
        "smoke_cold_tiny_search" \
        "1.00" \
        "$Q_RG --no-config 'needle' $Q_TINY" \
        "$Q_SCOUT --no-config 'needle' $Q_TINY"
    run_pair \
        "bounded_assignment_no_match" \
        "1.50" \
        "$RG_BOUNDED_ASSIGNMENT_COMMAND" \
        "$SCOUT_BOUNDED_ASSIGNMENT_COMMAND"
    run_pair \
        "large_bounded_unicode_class_no_match" \
        "1.50" \
        "$RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
        "$SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND"
    exit 0
fi

make_cold_tiny_corpus
make_bounded_assignment_corpus
make_large_bounded_unicode_class_corpus
make_line_regex_corpus
verify_generated_performance_inputs

Q_TINY="$(shell_quote "$OUT_DIR/cold-tiny.txt")"
OPEN_DIRECTORY="$ROOT"
Q_OPEN_NAME=""
LINUX_TREE="$ROOT"
Q_BOUNDED_ASSIGNMENT_PATTERN="$(shell_quote "$OUT_DIR/bounded-assignment/pattern.txt")"
Q_BOUNDED_ASSIGNMENT_INPUT="$(shell_quote "$OUT_DIR/bounded-assignment/no-match-800.txt")"
Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/pattern.txt")"
Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT="$(shell_quote "$OUT_DIR/large-bounded-unicode-class/no-match-5000.txt")"
Q_LINE_REGEX_INPUT="$(shell_quote "$OUT_DIR/line-regex/paladin-like-200000.txt")"
SHARED_DELEGATE_INPUTS="$Q_LINE_REGEX_INPUT $Q_LINE_REGEX_INPUT $Q_LINE_REGEX_INPUT $Q_LINE_REGEX_INPUT"
Q_LINE_REGEX_ABSENT_PATTERNS="$(shell_quote "$OUT_DIR/line-regex/absent-patterns-64.txt")"
LINE_REGEX_ABSENT_REGEXP_ARGUMENTS="$(make_absent_regexp_arguments "$OUT_DIR/line-regex/absent-patterns-64.txt")"
MANY_ABSENT_INPUTS="$(repeat_shell_argument "$Q_LINE_REGEX_INPUT" "$GATE_MANY_ABSENT_INPUT_COUNT")"
NESTED_LITERAL_MATCH_INPUTS="$(repeat_shell_argument "$Q_LINE_REGEX_INPUT" "$GATE_NESTED_LITERAL_MATCH_INPUT_COUNT")"
NESTED_LITERAL_NO_MATCH_INPUTS="$(repeat_shell_argument "$Q_LINE_REGEX_INPUT" "$GATE_NESTED_LITERAL_NO_MATCH_INPUT_COUNT")"
GENERAL_REGEX_ENVIRONMENT="SCOUT_REGEX_SPECIALIZATION_MODE=general"

if workload_group_selected subtitles_en_literal subtitles_en_regex; then
    OPENSUBTITLES_EN="${SCOUT_BENCH_OPENSUBTITLES_EN:-}"
    OPENSUBTITLES_EN="$(require_gate_corpus_file "opensubtitles-en" "$OPENSUBTITLES_EN")"
    OPEN_DIRECTORY="$(dirname -- "$OPENSUBTITLES_EN")"
    Q_OPEN_NAME="$(shell_quote "$(basename -- "$OPENSUBTITLES_EN")")"
fi

if workload_group_selected \
    linux_recursive_literal \
    linux_heldout_regex_general \
    linux_heldout_capture_general \
    linux_many_small_parallel; then
    LINUX_TREE="${SCOUT_BENCH_LINUX_TREE:-}"
    LINUX_TREE="$(require_gate_corpus_tree "linux-kernel" "$LINUX_TREE")"
fi

RG_LINE_REGEX_PREFIX="$Q_RG --no-config --threads 1 --mmap --count-matches --no-messages"
SCOUT_LINE_REGEX_PREFIX="$Q_SCOUT --no-config --threads 1 --mmap --count-matches --no-messages"
RG_LINE_REGEX_LINE_COUNT_PREFIX="$Q_RG --no-config --threads 1 --mmap --count --no-messages"
SCOUT_LINE_REGEX_LINE_COUNT_PREFIX="$Q_SCOUT --no-config --threads 1 --mmap --count --no-messages"
RG_BOUNDED_ASSIGNMENT_COMMAND="$Q_RG --no-config --threads $GATE_GENERATED_THREADS -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT"
SCOUT_BOUNDED_ASSIGNMENT_COMMAND="$Q_SCOUT --no-config --threads $GATE_GENERATED_THREADS -U --count-matches --no-messages -f $Q_BOUNDED_ASSIGNMENT_PATTERN $Q_BOUNDED_ASSIGNMENT_INPUT"
RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$Q_RG --no-config --threads $GATE_GENERATED_THREADS -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT"
SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND="$Q_SCOUT --no-config --threads $GATE_GENERATED_THREADS -U --count-matches --no-messages -f $Q_LARGE_BOUNDED_UNICODE_CLASS_PATTERN $Q_LARGE_BOUNDED_UNICODE_CLASS_INPUT"
RG_MANY_ABSENT_REGEXP_COMMAND="$RG_LINE_REGEX_PREFIX $LINE_REGEX_ABSENT_REGEXP_ARGUMENTS $MANY_ABSENT_INPUTS"
SCOUT_MANY_ABSENT_REGEXP_COMMAND="$SCOUT_LINE_REGEX_PREFIX $LINE_REGEX_ABSENT_REGEXP_ARGUMENTS $MANY_ABSENT_INPUTS"
RG_MANY_ABSENT_PATTERN_FILE_COMMAND="$RG_LINE_REGEX_PREFIX -f $Q_LINE_REGEX_ABSENT_PATTERNS $MANY_ABSENT_INPUTS"
SCOUT_MANY_ABSENT_PATTERN_FILE_COMMAND="$SCOUT_LINE_REGEX_PREFIX -f $Q_LINE_REGEX_ABSENT_PATTERNS $MANY_ABSENT_INPUTS"
RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND="$RG_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}$' $Q_LINE_REGEX_INPUT"
SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND="$SCOUT_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}$' $Q_LINE_REGEX_INPUT"
OPENSUBTITLES_RUNS="$(gate_opensubtitles_runs)"
OPENSUBTITLES_WARMUP="$(gate_opensubtitles_warmup)"
TREE_RUNS="$(gate_tree_runs)"
TREE_WARMUP="$(gate_tree_warmup)"
COLD_RUNS="$(gate_cold_runs)"
COLD_WARMUP="$(gate_cold_warmup)"
BOUNDED_ASSIGNMENT_RUNS="$(gate_bounded_assignment_runs)"
BOUNDED_ASSIGNMENT_WARMUP="$(gate_bounded_assignment_warmup)"
LARGE_BOUNDED_UNICODE_CLASS_RUNS="$(gate_large_bounded_unicode_class_runs)"
LARGE_BOUNDED_UNICODE_CLASS_WARMUP="$(gate_large_bounded_unicode_class_warmup)"
LINE_REGEX_RUNS="$(gate_line_regex_runs)"
LINE_REGEX_WARMUP="$(gate_line_regex_warmup)"

print_repro_manifest
if workload_selected subtitles_en_regex; then
    printf '\n== corpus balance ==\n'
    analyze_large_file_segments "subtitles_en_regex" "$OPENSUBTITLES_EN" "$GATE_LARGE_FILE_SEGMENT_BUFFER_LENGTH" "$GATE_LARGE_FILE_THREADS"
fi
measure_rss_floor "$OPENSUBTITLES_RUNS" "$OPENSUBTITLES_WARMUP"

run_gate_pair \
    "bounded_assignment_no_match" \
    "1.50" \
    "$RG_BOUNDED_ASSIGNMENT_COMMAND" \
    "$SCOUT_BOUNDED_ASSIGNMENT_COMMAND" \
    "$BOUNDED_ASSIGNMENT_RUNS" \
    "$BOUNDED_ASSIGNMENT_WARMUP" \
    "$ROOT" \
    "1" \
    ""
run_gate_pair \
    "large_bounded_unicode_class_no_match" \
    "1.50" \
    "$RG_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
    "$SCOUT_LARGE_BOUNDED_UNICODE_CLASS_COMMAND" \
    "$LARGE_BOUNDED_UNICODE_CLASS_RUNS" \
    "$LARGE_BOUNDED_UNICODE_CLASS_WARMUP" \
    "$ROOT" \
    "1" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_word_boundary_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_word_boundary_line_count_general" \
    "1.50" \
    "$RG_LINE_REGEX_LINE_COUNT_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_LINE_COUNT_PREFIX '\\b\\w{5}\\s+\\w{5}\\s+\\w{5}\\b' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_generated_record_word_boundary_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '\\bGeneratedRecord\\b' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '\\bGeneratedRecord\\b' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_anchored_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '^internal sealed class GeneratedRecord\\r?$' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '^internal sealed class GeneratedRecord\\r?$' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_bounded_class_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}\\r?$' $Q_LINE_REGEX_INPUT" \
    "$SCOUT_LINE_REGEX_PREFIX '^[A-Za-z_]{70,90}\\r?$' $Q_LINE_REGEX_INPUT" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "line_regex_bounded_class_exact_general" \
    "1.50" \
    "$RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND" \
    "$SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "1" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "nested_literal_alternation_match_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '(?:Generated|Paladin(?:Record|Value))' $NESTED_LITERAL_MATCH_INPUTS" \
    "$SCOUT_LINE_REGEX_PREFIX '(?:Generated|Paladin(?:Record|Value))' $NESTED_LITERAL_MATCH_INPUTS" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "nested_literal_alternation_no_match_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX '(?:Absent|Missing(?:Two|Three))' $NESTED_LITERAL_NO_MATCH_INPUTS" \
    "$SCOUT_LINE_REGEX_PREFIX '(?:Absent|Missing(?:Two|Three))' $NESTED_LITERAL_NO_MATCH_INPUTS" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "1" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "shared_delegate_prefix_general" \
    "1.50" \
    "$RG_LINE_REGEX_PREFIX 'delegate .*ShowMessageBoxHandler|delegate .*UpdateEDIEvent|delegate .*SetProgressBarValue|delegate .*ShowCheckboxMessageBoxHandler' $SHARED_DELEGATE_INPUTS" \
    "$SCOUT_LINE_REGEX_PREFIX 'delegate .*ShowMessageBoxHandler|delegate .*UpdateEDIEvent|delegate .*SetProgressBarValue|delegate .*ShowCheckboxMessageBoxHandler' $SHARED_DELEGATE_INPUTS" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "many_absent_regexp_general" \
    "1.50" \
    "$RG_MANY_ABSENT_REGEXP_COMMAND" \
    "$SCOUT_MANY_ABSENT_REGEXP_COMMAND" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "1" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "many_absent_pattern_file_general" \
    "1.50" \
    "$RG_MANY_ABSENT_PATTERN_FILE_COMMAND" \
    "$SCOUT_MANY_ABSENT_PATTERN_FILE_COMMAND" \
    "$LINE_REGEX_RUNS" \
    "$LINE_REGEX_WARMUP" \
    "$ROOT" \
    "1" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "subtitles_en_literal" \
    "1.20" \
    "$Q_RG --no-config --threads $GATE_LARGE_FILE_THREADS --mmap -n 'Sherlock Holmes' $Q_OPEN_NAME" \
    "$Q_SCOUT --no-config --threads $GATE_LARGE_FILE_THREADS --mmap -n 'Sherlock Holmes' $Q_OPEN_NAME" \
    "$OPENSUBTITLES_RUNS" \
    "$OPENSUBTITLES_WARMUP" \
    "$OPEN_DIRECTORY" \
    "0" \
    ""
run_gate_pair \
    "subtitles_en_regex" \
    "1.20" \
    "$Q_RG --no-config --threads $GATE_LARGE_FILE_THREADS -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN_NAME" \
    "$Q_SCOUT --no-config --threads $GATE_LARGE_FILE_THREADS -n '\\w{5}\\s+\\w{5}\\s+\\w{5}' $Q_OPEN_NAME" \
    "$OPENSUBTITLES_RUNS" \
    "$OPENSUBTITLES_WARMUP" \
    "$OPEN_DIRECTORY" \
    "0" \
    ""
run_gate_pair \
    "linux_recursive_literal" \
    "1.25" \
    "$Q_RG --no-config --threads $GATE_TREE_THREADS -n 'PM_RESUME' ." \
    "$Q_SCOUT --no-config --threads $GATE_TREE_THREADS -n 'PM_RESUME' ." \
    "$TREE_RUNS" \
    "$TREE_WARMUP" \
    "$LINUX_TREE" \
    "0" \
    ""
run_gate_pair \
    "linux_heldout_regex_general" \
    "1.50" \
    "$Q_RG --no-config --threads $GATE_TREE_THREADS -n '\\b(?:struct|enum|union)\\s+[A-Za-z_][A-Za-z0-9_]*' ." \
    "$Q_SCOUT --no-config --threads $GATE_TREE_THREADS -n '\\b(?:struct|enum|union)\\s+[A-Za-z_][A-Za-z0-9_]*' ." \
    "$TREE_RUNS" \
    "$TREE_WARMUP" \
    "$LINUX_TREE" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "linux_heldout_capture_general" \
    "1.75" \
    "$Q_RG --no-config --threads $GATE_TREE_THREADS -n --replace '\$1 \$2' '\\b(struct|enum|union)\\s+([A-Za-z_][A-Za-z0-9_]*)' ." \
    "$Q_SCOUT --no-config --threads $GATE_TREE_THREADS -n --replace '\$1 \$2' '\\b(struct|enum|union)\\s+([A-Za-z_][A-Za-z0-9_]*)' ." \
    "$TREE_RUNS" \
    "$TREE_WARMUP" \
    "$LINUX_TREE" \
    "0" \
    "$GENERAL_REGEX_ENVIRONMENT"
run_gate_pair \
    "linux_many_small_parallel" \
    "1.30" \
    "$Q_RG --no-config --threads $GATE_TREE_THREADS -n 'struct' ." \
    "$Q_SCOUT --no-config --threads $GATE_TREE_THREADS -n 'struct' ." \
    "$TREE_RUNS" \
    "$TREE_WARMUP" \
    "$LINUX_TREE" \
    "0" \
    ""
run_gate_pair \
    "cold_version" \
    "1.00" \
    "$Q_RG --no-config --version" \
    "$Q_SCOUT --no-config --version" \
    "$COLD_RUNS" \
    "$COLD_WARMUP" \
    "$ROOT" \
    "0" \
    "" \
    "independent"
run_gate_pair \
    "cold_tiny_search" \
    "1.00" \
    "$Q_RG --no-config 'needle' $Q_TINY" \
    "$Q_SCOUT --no-config 'needle' $Q_TINY" \
    "$COLD_RUNS" \
    "$COLD_WARMUP" \
    "$ROOT" \
    "0" \
    ""

printf '\n== release gate summary ==\n'
if [ "$FAILED_GATE_COUNT" -ne 0 ]; then
    printf 'Result: FAIL (%s workload performance limit%s exceeded)\n' \
        "$FAILED_GATE_COUNT" \
        "$(if [ "$FAILED_GATE_COUNT" -eq 1 ]; then printf ''; else printf 's'; fi)"
    for failed_gate_workload in $FAILED_GATE_WORKLOADS; do
        printf '  %s\n' "$failed_gate_workload"
    done
    exit 1
fi
printf 'Result: PASS (all selected workload performance limits were met)\n'
