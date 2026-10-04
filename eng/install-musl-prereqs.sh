#!/usr/bin/env sh
set -eu

[ -e /lib/ld-musl-aarch64.so.1 ] || { printf 'This gate requires Alpine ARM64.\n' >&2; exit 1; }
apk add --no-cache bash binutils brotli build-base bzip2 clang curl fish git gzip \
    icu-libs lz4 ncompress openssl-dev python3 tar unzip xz zlib-dev zsh zstd

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
mkdir -p "$ROOT/artifacts/prereqs/linux-musl-arm64"
apk info -v | sort > "$ROOT/artifacts/prereqs/linux-musl-arm64/packages.txt"
for tool in cc clang ld ar ranlib strip nm gzip bzip2 xz lz4 brotli zstd uncompress unzip python3; do
    path="$(command -v "$tool")"
    sha256sum "$path"
done > "$ROOT/artifacts/prereqs/linux-musl-arm64/tools.sha256"
cc "$ROOT/native/entry/verify-linux-arm64-abi.c" -o "$ROOT/artifacts/prereqs/linux-musl-arm64/verify-abi"
"$ROOT/artifacts/prereqs/linux-musl-arm64/verify-abi" > "$ROOT/artifacts/prereqs/linux-musl-arm64/abi.txt"

# The SDK image supplies .NET 10; the library tests also execute on .NET 9.
curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh \
    --output "$ROOT/artifacts/prereqs/linux-musl-arm64/dotnet-install.sh"
bash "$ROOT/artifacts/prereqs/linux-musl-arm64/dotnet-install.sh" \
    --channel 9.0 --runtime dotnet --install-dir /usr/share/dotnet
