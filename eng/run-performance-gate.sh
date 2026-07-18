#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"
PERFORMANCE_WORKTREE=""
PERFORMANCE_WORKTREE_PARENT=""
GATE_AGGREGATE_DIR="$ROOT/artifacts/bench/hyperfine"

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

    if [ -n "$PERFORMANCE_WORKTREE_PARENT" ] && [ -d "$PERFORMANCE_WORKTREE_PARENT" ]; then
        rmdir "$PERFORMANCE_WORKTREE_PARENT" 2>/dev/null || true
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

if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
    printf 'The release performance gate requires macOS arm64.\n' >&2
    exit 1
fi

export SCOUT_HOST_RID="osx-arm64"
export SCOUT_ORACLE_ENVIRONMENT="github-actions"
if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
    export SCOUT_TOOL_ENVIRONMENT="github-actions"
else
    export SCOUT_TOOL_ENVIRONMENT="local"
fi

performance_inputs="$(git -c safe.directory="$ROOT" -C "$ROOT" status --porcelain=v1 --untracked-files=normal -- \
    .github/workflows/release-gates.yml \
    bench \
    Directory.Build.props \
    Directory.Build.rsp \
    Directory.Build.targets \
    Directory.Packages.props \
    eng \
    global.json \
    native \
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

unset \
    CC \
    CFLAGS \
    CPPFLAGS \
    CXX \
    CXXFLAGS \
    DYLD_INSERT_LIBRARIES \
    DYLD_LIBRARY_PATH \
    DYLD_PRINT_STATISTICS \
    LDFLAGS \
    MACOSX_DEPLOYMENT_TARGET \
    MallocCheckHeapEach \
    MallocCheckHeapStart \
    MallocGuardEdges \
    MallocPreScribble \
    MallocScribble \
    MallocStackLogging \
    MallocStackLoggingNoCompact \
    RIPGREP_CONFIG_PATH \
    SCOUT_BENCH_LINUX_TREE \
    SCOUT_BENCH_OPENSUBTITLES_EN \
    SCOUT_BIN \
    SCOUT_BUILD_PROVENANCE \
    SCOUT_CONFIG_PATH \
    SCOUT_CORPORA_DIR \
    SCOUT_RELEASE_VERSION \
    SCOUT_RIPGREP_REFERENCE \
    SCOUT_RSS_BASELINE_BIN

"$ROOT/eng/restore-ripgrep-oracle.sh"
"$ROOT/eng/setup-hyperfine.sh"
"$ROOT/eng/fetch-corpora.sh" --all

PERFORMANCE_WORKTREE_PARENT="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/scout-performance-gate.XXXXXX")"
PERFORMANCE_WORKTREE="$PERFORMANCE_WORKTREE_PARENT/source"
git -c safe.directory="$ROOT" -C "$ROOT" worktree add --detach "$PERFORMANCE_WORKTREE" HEAD
mkdir -p "$PERFORMANCE_WORKTREE/artifacts"
ln -s "$ROOT/artifacts/corpora" "$PERFORMANCE_WORKTREE/artifacts/corpora"
ln -s "$ROOT/artifacts/ripgrep-oracle" "$PERFORMANCE_WORKTREE/artifacts/ripgrep-oracle"

dotnet restore "$PERFORMANCE_WORKTREE/Scout.slnx"
"$PERFORMANCE_WORKTREE/eng/preflight.sh"
"$PERFORMANCE_WORKTREE/native/build-app-unix.sh" osx-arm64 --with-differentials
SCOUT_PERFORMANCE_GATE_INNER=1 "$PERFORMANCE_WORKTREE/bench/run-hyperfine.sh" --gate "$@"
