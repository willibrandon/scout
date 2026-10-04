#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
CHECKOUT="$1"
RELEASE="$2"
SOURCE="${SCOUT_RIPGREP_REFERENCE:-/Users/brandon/src/ripgrep}"

case "$CHECKOUT" in
    "$ROOT"/artifacts/*) ;;
    *) printf 'Reference builds require a checkout under artifacts: %s\n' "$CHECKOUT" >&2; exit 1 ;;
esac

if [ ! -d "$CHECKOUT/.git" ]; then
    git init "$CHECKOUT"
    git -C "$CHECKOUT" remote add origin https://github.com/BurntSushi/ripgrep.git
fi

if [ "$(git -C "$CHECKOUT" rev-parse HEAD 2>/dev/null || true)" = "$RELEASE" ]; then
    exit 0
fi

if git -C "$SOURCE" cat-file -e "$RELEASE^{commit}" 2>/dev/null; then
    git -C "$CHECKOUT" fetch --no-tags "$SOURCE" "$RELEASE"
else
    git -C "$CHECKOUT" fetch --depth 1 origin "$RELEASE"
fi
git -C "$CHECKOUT" checkout --detach "$RELEASE"
