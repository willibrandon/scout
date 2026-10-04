#!/usr/bin/env sh
set -eu

[ -e /lib/ld-musl-aarch64.so.1 ] || { printf 'This gate requires Alpine ARM64.\n' >&2; exit 1; }
apk add --no-cache bash binutils brotli build-base bzip2 clang curl fish git gzip \
    icu-libs lz4 openssl-dev python3 tar unzip xz zlib-dev zsh zstd

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
mkdir -p "$ROOT/artifacts/prereqs/linux-musl-arm64"
source_record="$(python3 -c 'import sys,tomllib; r=tomllib.load(open(sys.argv[1], "rb"))["musl_compress_source"]; print(r["url"]); print(r["sha256"])' "$ROOT/tests/PREREQS.lock")"
source_url="$(printf '%s\n' "$source_record" | sed -n '1p')"
source_sha256="$(printf '%s\n' "$source_record" | sed -n '2p')"
source_archive="$ROOT/artifacts/prereqs/linux-musl-arm64/ncompress.tar.gz"
source_directory="$ROOT/artifacts/prereqs/linux-musl-arm64/ncompress"
curl --fail --silent --show-error --location "$source_url" --output "$source_archive"
printf '%s  %s\n' "$source_sha256" "$source_archive" | sha256sum -c
mkdir -p "$source_directory"
tar -xzf "$source_archive" --strip-components=1 -C "$source_directory"
make -C "$source_directory" compress
install -m 755 "$source_directory/compress" /usr/local/bin/compress
apk info -v | sort > "$ROOT/artifacts/prereqs/linux-musl-arm64/packages.txt"
for tool in cc clang ld ar ranlib strip nm gzip bzip2 xz lz4 brotli zstd compress uncompress unzip python3; do
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
