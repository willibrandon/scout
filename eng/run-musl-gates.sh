#!/usr/bin/env bash
set -euo pipefail

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"
export SCOUT_HOST_RID=linux-musl-arm64
export SCOUT_ORACLE_ENVIRONMENT=github-actions

eng/install-musl-prereqs.sh
eng/restore-ripgrep-oracle.sh
eng/fetch-corpora.sh --all
dotnet restore Scout.slnx
dotnet build Scout.slnx --no-restore
eng/check-msbuild-warning-gates.sh
dotnet test Scout.slnx --no-restore
spike/build-unix.sh linux-musl-arm64
native/build-app-unix.sh linux-musl-arm64 --with-differentials
SCOUT_TEST_EXECUTABLE_PATH="$ROOT/artifacts/bin/linux-musl-arm64/scout" dotnet test tests/Scout.Differential.Tests/Scout.Differential.Tests.csproj --no-restore
python3 eng/verify-completions.py --scout artifacts/bin/linux-musl-arm64/scout --shell bash --shell zsh --shell fish
readelf -l artifacts/bin/linux-musl-arm64/scout-real > artifacts/prereqs/linux-musl-arm64/elf.txt
rg_path="/lib/ld-musl-aarch64.so.1"
grep -F "$rg_path" artifacts/prereqs/linux-musl-arm64/elf.txt
readelf -d artifacts/bin/linux-musl-arm64/scout-real > artifacts/prereqs/linux-musl-arm64/elf-dependencies.txt
if grep -i pcre artifacts/prereqs/linux-musl-arm64/elf-dependencies.txt; then
    printf 'PCRE2 must be bundled in the executable.\n' >&2
    exit 1
fi
eng/package-release.sh linux-musl-arm64
mkdir -p artifacts/package-extract/linux-musl-arm64
tar -xzf artifacts/packages/scout-linux-musl-arm64.tar.gz -C artifacts/package-extract/linux-musl-arm64
artifacts/package-extract/linux-musl-arm64/scout-linux-musl-arm64/scout --version
python3 eng/pack-dotnet-tool.py --rid linux-musl-arm64 --rid-only ${SCOUT_RELEASE_VERSION:+--version "$SCOUT_RELEASE_VERSION"}
python3 eng/pack-dotnet-tool.py --pointer-only --output artifacts/tool-test ${SCOUT_RELEASE_VERSION:+--version "$SCOUT_RELEASE_VERSION"}
mkdir -p artifacts/tool-test
cat > artifacts/tool-test/NuGet.Config <<EOF
<configuration><packageSources><clear />
<add key="rid" value="$ROOT/artifacts/tool-packages" />
<add key="pointer" value="$ROOT/artifacts/tool-test" />
</packageSources></configuration>
EOF
export NUGET_PACKAGES="$ROOT/artifacts/tool-test/nuget-packages"
dotnet tool install Scout --tool-path artifacts/tool-test/install --configfile artifacts/tool-test/NuGet.Config
printf 'needle\n' > artifacts/tool-test/search.txt
artifacts/tool-test/install/scout --no-config -n needle artifacts/tool-test/search.txt > artifacts/tool-test/search.out
printf '1:needle\n' | cmp -s - artifacts/tool-test/search.out
python3 eng/verify-library-packages.py --rid linux-musl-arm64 ${SCOUT_RELEASE_VERSION:+--version "$SCOUT_RELEASE_VERSION"}
