#!/bin/bash
set -Eeuo pipefail

ROOT="$(CDPATH= cd -- "$(/usr/bin/dirname -- "$0")/.." && pwd -P)"
cd "$ROOT"
PERFORMANCE_WORKTREE=""
PERFORMANCE_WORKTREE_PARENT=""
PERFORMANCE_WORKTREE_PARENT_PREFIX=""
PERFORMANCE_STATE_PARENT=""
PERFORMANCE_STATE_PARENT_PREFIX=""
GATE_AGGREGATE_DIR="$ROOT/artifacts/bench/hyperfine"

initialize_performance_state() {
    local temporary_root="$1"

    temporary_root="$(CDPATH= cd -- "$temporary_root" && pwd -P)"
    PERFORMANCE_STATE_PARENT_PREFIX="${temporary_root%/}/scout-performance-state."
    PERFORMANCE_STATE_PARENT="$(mktemp -d "${PERFORMANCE_STATE_PARENT_PREFIX}XXXXXX")"
    mkdir -p \
        "$PERFORMANCE_STATE_PARENT/home" \
        "$PERFORMANCE_STATE_PARENT/tmp" \
        "$PERFORMANCE_STATE_PARENT/xdg/cache" \
        "$PERFORMANCE_STATE_PARENT/xdg/config" \
        "$PERFORMANCE_STATE_PARENT/xdg/data" \
        "$PERFORMANCE_STATE_PARENT/xdg/state"

    export HOME="$PERFORMANCE_STATE_PARENT/home"
    export TMPDIR="$PERFORMANCE_STATE_PARENT/tmp"
    export XDG_CACHE_HOME="$PERFORMANCE_STATE_PARENT/xdg/cache"
    export XDG_CONFIG_HOME="$PERFORMANCE_STATE_PARENT/xdg/config"
    export XDG_DATA_HOME="$PERFORMANCE_STATE_PARENT/xdg/data"
    export XDG_STATE_HOME="$PERFORMANCE_STATE_PARENT/xdg/state"
    export DOTNET_CLI_HOME="$HOME"
    export PYTHONNOUSERSITE="1"
}

remove_generated_directory() {
    local directory="$1"
    local prefix="$2"
    local description="$3"

    if [ -z "$prefix" ]; then
        printf 'Refusing to remove %s without an expected path prefix.\n' "$description" >&2
        return 1
    fi

    case "$directory" in
        "$prefix"*)
            if ! rm -rf -- "$directory"; then
                printf 'Could not remove %s %s.\n' "$description" "$directory" >&2
                return 1
            fi
            ;;
        *)
            printf 'Refusing to remove unexpected %s %s.\n' "$description" "$directory" >&2
            return 1
            ;;
    esac
}

copy_gate_aggregates() {
    if [ -z "$PERFORMANCE_WORKTREE" ]; then
        return 0
    fi

    source_directory="$PERFORMANCE_WORKTREE/artifacts/bench/hyperfine"
    if [ ! -d "$source_directory" ]; then
        return 0
    fi

    if ! mkdir -p "$GATE_AGGREGATE_DIR"; then
        printf 'Could not create performance aggregate directory %s.\n' "$GATE_AGGREGATE_DIR" >&2
        return 1
    fi
    for aggregate in "$source_directory"/*.json; do
        [ -f "$aggregate" ] || continue
        if ! cp "$aggregate" "$GATE_AGGREGATE_DIR/"; then
            printf 'Could not copy performance aggregate %s.\n' "$aggregate" >&2
            return 1
        fi
    done
    for samples in "$source_directory"/*.samples; do
        [ -d "$samples" ] || continue
        if ! cp -R "$samples" "$GATE_AGGREGATE_DIR/"; then
            printf 'Could not copy performance samples %s.\n' "$samples" >&2
            return 1
        fi
    done
}

cleanup() {
    status=$?
    trap - EXIT
    if ! copy_gate_aggregates && [ "$status" -eq 0 ]; then
        status=1
    fi

    if [ -n "$PERFORMANCE_WORKTREE" ] && [ -e "$PERFORMANCE_WORKTREE/.git" ]; then
        if ! git -c safe.directory="$ROOT" -C "$ROOT" worktree remove --force "$PERFORMANCE_WORKTREE"; then
            printf 'Could not remove performance worktree %s.\n' "$PERFORMANCE_WORKTREE" >&2
            if [ "$status" -eq 0 ]; then
                status=1
            fi
        fi
    fi

    if [ -n "$PERFORMANCE_WORKTREE_PARENT" ] &&
        [ -d "$PERFORMANCE_WORKTREE_PARENT" ] &&
        { [ -z "$PERFORMANCE_WORKTREE" ] || [ ! -e "$PERFORMANCE_WORKTREE/.git" ]; }; then
        if ! remove_generated_directory \
            "$PERFORMANCE_WORKTREE_PARENT" \
            "$PERFORMANCE_WORKTREE_PARENT_PREFIX" \
            "performance worktree parent" && [ "$status" -eq 0 ]; then
            status=1
        fi
    fi

    if [ -n "$PERFORMANCE_STATE_PARENT" ] &&
        [ -d "$PERFORMANCE_STATE_PARENT" ] &&
        { [ -z "$PERFORMANCE_WORKTREE" ] || [ ! -e "$PERFORMANCE_WORKTREE/.git" ]; }; then
        if ! remove_generated_directory \
            "$PERFORMANCE_STATE_PARENT" \
            "$PERFORMANCE_STATE_PARENT_PREFIX" \
            "performance state directory" && [ "$status" -eq 0 ]; then
            status=1
        fi
    fi

    exit "$status"
}

trap cleanup EXIT

case "$#:${1:-}" in
    0:)
        set --
        ;;
    1:--gate)
        set --
        ;;
    2:--workload)
        ;;
    3:--gate)
        if [ "$2" != "--workload" ]; then
            printf 'The shared driver accepts only --gate and an optional --workload NAME diagnostic.\n' >&2
            exit 2
        fi
        set -- "$2" "$3"
        ;;
    *)
        printf 'The shared driver accepts only --gate and an optional --workload NAME diagnostic.\n' >&2
        exit 2
        ;;
esac

if [ "$(/usr/bin/uname -s)" != "Darwin" ] || [ "$(/usr/bin/uname -m)" != "arm64" ]; then
    printf 'The release performance gate requires macOS arm64.\n' >&2
    exit 1
fi

. "$ROOT/eng/performance-environment.sh"
if [ "${SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED:-}" != "1" ]; then
    if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
        PERFORMANCE_TOOL_ENVIRONMENT="github-actions"
    else
        PERFORMANCE_TOOL_ENVIRONMENT="local"
    fi

    exec_clean_performance_gate \
        "$PERFORMANCE_TOOL_ENVIRONMENT" \
        "$0" \
        "$@"
fi

. "$ROOT/eng/performance-process-state.sh"
if ! configure_performance_process_state; then
    printf 'The clean performance gate could not establish canonical process state.\n' >&2
    exit 1
fi

PERFORMANCE_TOOL_ENVIRONMENT="${SCOUT_PERFORMANCE_GATE_TOOL_ENVIRONMENT:-}"
unset \
    SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED \
    SCOUT_PERFORMANCE_GATE_TOOL_ENVIRONMENT
case "$PERFORMANCE_TOOL_ENVIRONMENT" in
    github-actions|local)
        ;;
    *)
        printf 'The clean performance environment requires a pinned tool environment.\n' >&2
        exit 1
        ;;
esac

initialize_performance_state "/tmp"
DOTNET_ROOT="$PERFORMANCE_STATE_PARENT/dotnet"
"$ROOT/eng/setup-dotnet-performance-sdk.sh" "$DOTNET_ROOT"
sanitize_performance_environment "$DOTNET_ROOT"
export SCOUT_HOST_RID="osx-arm64"
export SCOUT_ORACLE_ENVIRONMENT="github-actions"
export SCOUT_TOOL_ENVIRONMENT="$PERFORMANCE_TOOL_ENVIRONMENT"

performance_inputs="$(git -c safe.directory="$ROOT" -C "$ROOT" status --porcelain=v1 --untracked-files=normal -- \
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
    tests/PREREQS.lock)"
if [ -n "$performance_inputs" ]; then
    printf 'Release-equivalent performance inputs must be committed and clean:\n%s\n' \
        "$performance_inputs" >&2
    exit 1
fi

mkdir -p "$GATE_AGGREGATE_DIR"
for previous_aggregate in "$GATE_AGGREGATE_DIR"/*.json; do
    [ -f "$previous_aggregate" ] || continue
    rm -f "$previous_aggregate"
done
for previous_samples in "$GATE_AGGREGATE_DIR"/*.samples; do
    [ -d "$previous_samples" ] || continue
    rm -rf "$previous_samples"
done

"$ROOT/eng/restore-ripgrep-oracle.sh"
SCOUT_HYPERFINE_BIN="$(
    "$ROOT/eng/setup-hyperfine.sh" "$PERFORMANCE_STATE_PARENT/hyperfine"
)"
case "$SCOUT_HYPERFINE_BIN" in
    /*)
        ;;
    *)
        printf 'Pinned Hyperfine setup returned a non-absolute path: %s\n' \
            "$SCOUT_HYPERFINE_BIN" >&2
        exit 1
        ;;
esac
[ -x "$SCOUT_HYPERFINE_BIN" ] || {
    printf 'Pinned Hyperfine setup did not return an executable: %s\n' \
        "$SCOUT_HYPERFINE_BIN" >&2
    exit 1
}
export SCOUT_HYPERFINE_BIN
"$ROOT/eng/fetch-corpora.sh" --all --verify-lock

PERFORMANCE_TEMP_ROOT="$(CDPATH= cd -- "$TMPDIR" && pwd -P)"
PERFORMANCE_WORKTREE_PARENT_PREFIX="${PERFORMANCE_TEMP_ROOT%/}/scout-performance-gate."
PERFORMANCE_WORKTREE_PARENT="$(mktemp -d "${PERFORMANCE_WORKTREE_PARENT_PREFIX}XXXXXX")"
PERFORMANCE_WORKTREE="$PERFORMANCE_WORKTREE_PARENT/source"
git -c safe.directory="$ROOT" -c core.autocrlf=false -c core.eol=lf \
    -C "$ROOT" worktree add --detach "$PERFORMANCE_WORKTREE" HEAD
mkdir -p \
    "$PERFORMANCE_WORKTREE/artifacts/corpora/opensubtitles" \
    "$PERFORMANCE_WORKTREE/artifacts/corpora/linux"
for corpus_archive in "$ROOT/artifacts/corpora/opensubtitles/"*.gz; do
    [ -f "$corpus_archive" ] || continue
    ln -s "$corpus_archive" \
        "$PERFORMANCE_WORKTREE/artifacts/corpora/opensubtitles/$(basename -- "$corpus_archive")"
done
for corpus_archive in "$ROOT/artifacts/corpora/linux/"*.tar.gz; do
    [ -f "$corpus_archive" ] || continue
    ln -s "$corpus_archive" \
        "$PERFORMANCE_WORKTREE/artifacts/corpora/linux/$(basename -- "$corpus_archive")"
done
ln -s "$ROOT/artifacts/ripgrep-oracle" "$PERFORMANCE_WORKTREE/artifacts/ripgrep-oracle"
export NUGET_PACKAGES="$PERFORMANCE_STATE_PARENT/nuget/packages"
cd "$PERFORMANCE_WORKTREE"

"$PERFORMANCE_WORKTREE/eng/fetch-corpora.sh" --all --verify-lock

dotnet restore "$PERFORMANCE_WORKTREE/Scout.slnx" --disable-build-servers
"$PERFORMANCE_WORKTREE/eng/preflight.sh"
"$PERFORMANCE_WORKTREE/native/build-app-unix.sh" osx-arm64 --with-differentials
if ! dotnet build-server shutdown; then
    printf 'Could not shut down .NET build servers before benchmark sampling.\n' >&2
    exit 1
fi
SCOUT_PERFORMANCE_GATE_INNER=1 "$PERFORMANCE_WORKTREE/bench/run-hyperfine.sh" --gate "$@"
