#!/usr/bin/env sh
set -eu

SNAPSHOT_DATE="2026-05-31"
SNAPSHOT_STAMP="$(printf '%s' "$SNAPSHOT_DATE" | tr -d '-')T000000Z"
SNAPSHOT_URL="http://snapshot.debian.org/archive/debian/${SNAPSHOT_STAMP}"
AMD64_DIGEST="sha256:b29f74a267526ae6ea104eed6c46133b0ca70ce812525df8cd5817698f0a624a"
ARM64_DIGEST="sha256:f1433d3ee18e12f45682b29d91b6356e54e40d6b47f5f8ac81e80f35cca8cfe7"
LIBC_VERSION="2.36-9+deb12u14"

run_for_rid() {
    rid="$1"
    platform="$2"
    digest="$3"

    docker run --rm --platform "$platform" --env "SCOUT_RID=$rid" "docker.io/library/debian:bookworm-slim@$digest" sh -ceu '
export DEBIAN_FRONTEND=noninteractive
rm -f /etc/apt/sources.list.d/*.sources /etc/apt/sources.list.d/*.list
cat >/etc/apt/sources.list <<EOF
deb '"$SNAPSHOT_URL"' bookworm main
EOF
printf "Acquire::Check-Valid-Until false;\nAcquire::Retries 3;\n" >/etc/apt/apt.conf.d/99snapshot
apt-get update >/dev/null
apt-get install -y --no-install-recommends libc6='"$LIBC_VERSION"' libc-bin='"$LIBC_VERSION"' >/dev/null
apt-get install -y --no-install-recommends gzip bzip2 xz-utils lz4 brotli zstd ncompress ca-certificates >/dev/null
for spec in gzip:gzip bzip2:bzip2 xz:xz-utils lz4:lz4 brotli:brotli zstd:zstd uncompress:ncompress; do
    name=${spec%%:*}
    package=${spec#*:}
    binary=$(command -v "$name")
    version=$(dpkg-query -W "$package" | cut -f2)
    sha256=$(sha256sum "$binary" | cut -d " " -f 1)
    printf "[[tool.linux]]\n"
    printf "rid = \"%s\"\n" "$SCOUT_RID"
    printf "name = \"%s\"\n" "$name"
    printf "package = \"%s\"\n" "$package"
    printf "binary = \"%s\"\n" "$name"
    printf "path = \"%s\"\n" "$binary"
    printf "version = \"%s\"\n" "$version"
    printf "sha256 = \"%s\"\n\n" "$sha256"
done
'
}

cat <<EOF
[linux_container]
base_image = "debian:bookworm-slim"
index_digest = "sha256:0104b334637a5f19aa9c983a91b54c89887c0984081f2068983107a6f6c21eeb"
amd64_digest = "$AMD64_DIGEST"
arm64_digest = "$ARM64_DIGEST"
snapshot_url = "$SNAPSHOT_URL"
libc6_version = "$LIBC_VERSION"
libc_bin_version = "$LIBC_VERSION"

EOF

run_for_rid "linux-x64" "linux/amd64" "$AMD64_DIGEST"
run_for_rid "linux-arm64" "linux/arm64" "$ARM64_DIGEST"
