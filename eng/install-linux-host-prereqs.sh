#!/usr/bin/env sh
set -eu

if command -v sudo >/dev/null 2>&1; then
    SUDO=sudo
else
    SUDO=
fi

export DEBIAN_FRONTEND=noninteractive
$SUDO apt-get update
$SUDO apt-get install -y --no-install-recommends \
    bash \
    binutils \
    brotli \
    build-essential \
    bzip2 \
    ca-certificates \
    clang \
    curl \
    git \
    gzip \
    lz4 \
    ncompress \
    tar \
    xz-utils \
    zlib1g \
    zlib1g-dev \
    zstd
