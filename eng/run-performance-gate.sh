#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"

if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
    printf 'The release performance gate requires macOS arm64.\n' >&2
    exit 1
fi

case "$#" in
    0)
        ;;
    2)
        if [ "$1" != "--workload" ]; then
            printf 'The shared driver accepts only an optional --workload NAME diagnostic.\n' >&2
            exit 2
        fi
        ;;
    *)
        printf 'The shared driver accepts only an optional --workload NAME diagnostic.\n' >&2
        exit 2
        ;;
esac

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

unset \
    SCOUT_BENCH_LINUX_TREE \
    SCOUT_BENCH_OPENSUBTITLES_EN \
    SCOUT_BIN \
    SCOUT_BUILD_PROVENANCE \
    SCOUT_CORPORA_DIR \
    SCOUT_RELEASE_VERSION \
    SCOUT_RIPGREP_REFERENCE \
    SCOUT_RSS_BASELINE_BIN

"$ROOT/eng/restore-ripgrep-oracle.sh"
dotnet restore "$ROOT/Scout.slnx"
"$ROOT/eng/setup-hyperfine.sh"
"$ROOT/eng/fetch-corpora.sh" --all
"$ROOT/eng/preflight.sh"
"$ROOT/native/build-app-unix.sh" osx-arm64 --with-differentials
"$ROOT/bench/run-hyperfine.sh" --gate "$@"
