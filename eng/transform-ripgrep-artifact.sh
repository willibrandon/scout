#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
    printf 'usage: %s KIND < input > output\n' "$0" >&2
    exit 2
fi

KIND="$1"
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
PROPS="$ROOT/Directory.Build.props"

read_msbuild_property() {
    awk -v name="$1" '
        $0 ~ "<" name ">" {
            value = $0
            sub(".*<" name ">", "", value)
            sub("</" name ">.*", "", value)
            print value
            found = 1
            exit 0
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$PROPS"
}

SCOUT_VERSION="$(read_msbuild_property VersionPrefix)"
SCOUT_RIPGREP_VERSION="$(read_msbuild_property ScoutRipgrepVersion)"
SCOUT_RIPGREP_REVISION_SHORT="$(read_msbuild_property ScoutRipgrepRevisionShort)"
SCOUT_REPOSITORY_URL="$(read_msbuild_property RepositoryUrl)"

export KIND
export SCOUT_VERSION
export SCOUT_RIPGREP_VERSION
export SCOUT_RIPGREP_REVISION_SHORT
export SCOUT_REPOSITORY_URL

exec perl "$ROOT/eng/transform-ripgrep-artifact.pl"
