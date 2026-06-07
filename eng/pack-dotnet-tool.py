#!/usr/bin/env python3
"""Pack Scout's custom native binaries as .NET 10 RID-specific tool packages."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile
from xml.sax.saxutils import escape


RIDS = (
    "linux-x64",
    "linux-arm64",
    "osx-x64",
    "osx-arm64",
    "win-x64",
    "win-arm64",
)


DESCRIPTION = "Feature-complete port of ripgrep to .NET Native AOT."
PACKAGE_ID = "Scout"
PROJECT_URL = "https://github.com/willibrandon/scout"
AUTHORS = "willibrandon"
TAGS = "ripgrep;grep;search;regex;cli;dotnet;nativeaot;pcre2"


def run(command: list[str], cwd: Path) -> str:
    result = subprocess.run(
        command,
        cwd=cwd,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    return result.stdout.strip()


def read_version(root: Path) -> str:
    import xml.etree.ElementTree as ET

    props = ET.parse(root / "Directory.Build.props")
    for group in props.getroot().findall("PropertyGroup"):
        version = group.findtext("VersionPrefix")
        if version:
            return version
    raise RuntimeError("Directory.Build.props does not define VersionPrefix.")


def xml(value: str) -> str:
    return escape(value, {'"': "&quot;"})


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def executable_name(rid: str) -> str:
    return "scout.exe" if rid.startswith("win-") else "scout"


def validate_binary(root: Path, rid: str) -> Path:
    binary = root / "artifacts" / "bin" / rid / executable_name(rid)
    if not binary.is_file():
        raise RuntimeError(f"Missing native binary for {rid}: {binary}")
    return binary


def package_metadata(package_id: str, version: str, package_type: str, commit: str) -> str:
    repository_commit = f' commit="{xml(commit)}"' if commit else ""
    return f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>{xml(package_id)}</id>
    <version>{xml(version)}</version>
    <authors>{xml(AUTHORS)}</authors>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <projectUrl>{xml(PROJECT_URL)}</projectUrl>
    <description>{xml(DESCRIPTION)}</description>
    <tags>{xml(TAGS)}</tags>
    <packageTypes>
      <packageType name="{xml(package_type)}" />
    </packageTypes>
    <repository type="git" url="{xml(PROJECT_URL)}.git"{repository_commit} />
  </metadata>
</package>
"""


def pointer_settings(version: str) -> str:
    rid_entries = "\n".join(
        f'    <RuntimeIdentifierPackage RuntimeIdentifier="{rid}" Id="{PACKAGE_ID}.{rid}" />'
        for rid in RIDS
    )
    return f"""<?xml version="1.0" encoding="utf-8"?>
<DotNetCliTool Version="2">
  <Commands>
    <Command Name="scout" />
  </Commands>
  <RuntimeIdentifierPackages>
{rid_entries}
  </RuntimeIdentifierPackages>
</DotNetCliTool>
"""


def rid_settings(rid: str) -> str:
    entrypoint = executable_name(rid)
    return f"""<?xml version="1.0" encoding="utf-8"?>
<DotNetCliTool Version="2">
  <Commands>
    <Command Name="scout" EntryPoint="{entrypoint}" Runner="executable" />
  </Commands>
</DotNetCliTool>
"""


def rels(nuspec_name: str, metadata_path: str) -> str:
    return f"""<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{xml(nuspec_name)}" Id="R{uuid.uuid4().hex.upper()}" />
  <Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/{xml(metadata_path)}" Id="R{uuid.uuid4().hex.upper()}" />
</Relationships>
"""


def content_types(nuspec_name: str) -> str:
    return f"""<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="md" ContentType="application/octet-stream" />
  <Default Extension="txt" ContentType="application/octet-stream" />
  <Default Extension="exe" ContentType="application/octet-stream" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="json" ContentType="application/octet-stream" />
  <Override PartName="/{xml(nuspec_name)}" ContentType="application/octet-stream" />
</Types>
"""


def core_properties(package_id: str, version: str) -> str:
    timestamp = dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    title = f"{package_id}.{version}"
    return f"""<?xml version="1.0" encoding="utf-8"?>
<coreProperties xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:creator>{xml(AUTHORS)}</dc:creator>
  <dc:description>{xml(DESCRIPTION)}</dc:description>
  <dc:identifier>{xml(package_id)}</dc:identifier>
  <version>{xml(version)}</version>
  <keywords>{xml(TAGS)}</keywords>
  <lastModifiedBy>Scout release packaging</lastModifiedBy>
  <title>{xml(title)}</title>
  <dcterms:created xsi:type="dcterms:W3CDTF">{timestamp}</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">{timestamp}</dcterms:modified>
</coreProperties>
"""


def add_directory(zip_file: zipfile.ZipFile, directory: Path) -> None:
    for path in sorted(directory.rglob("*")):
        if path.is_dir():
            continue
        arcname = path.relative_to(directory).as_posix()
        info = zipfile.ZipInfo(arcname)
        info.date_time = (2026, 1, 1, 0, 0, 0)
        mode = 0o755 if os.access(path, os.X_OK) else 0o644
        info.external_attr = (mode & 0xFFFF) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        zip_file.writestr(info, path.read_bytes())


def pack(package_root: Path, output_dir: Path, package_id: str, version: str) -> Path:
    output_dir.mkdir(parents=True, exist_ok=True)
    nupkg = output_dir / f"{package_id}.{version}.nupkg"
    if nupkg.exists():
        nupkg.unlink()
    with zipfile.ZipFile(nupkg, "w", compression=zipfile.ZIP_DEFLATED) as zip_file:
        add_directory(zip_file, package_root)
    digest = hashlib.sha256(nupkg.read_bytes()).hexdigest()
    (nupkg.with_suffix(nupkg.suffix + ".sha256")).write_text(f"{digest}  {nupkg.name}\n", encoding="ascii")
    return nupkg


def copy_common_files(root: Path, package_root: Path) -> None:
    for relative in ("README.md", "LICENSE", "docs/PARITY.md", "docs/THIRD-PARTY-NOTICES.md"):
        source = root / relative
        if not source.is_file():
            raise RuntimeError(f"Missing package metadata file: {source}")
        destination = package_root / Path(relative).name
        shutil.copyfile(source, destination)


def build_pointer(root: Path, output_dir: Path, version: str, commit: str) -> Path:
    with tempfile.TemporaryDirectory(prefix="scout-tool-pointer-") as temp:
        package_root = Path(temp)
        package_id = PACKAGE_ID
        nuspec_name = f"{package_id}.nuspec"
        metadata_path = f"package/services/metadata/core-properties/{uuid.uuid4().hex}.psmdcp"
        write(package_root / "_rels/.rels", rels(nuspec_name, metadata_path))
        write(package_root / "[Content_Types].xml", content_types(nuspec_name))
        write(package_root / metadata_path, core_properties(package_id, version))
        write(package_root / nuspec_name, package_metadata(package_id, version, "DotnetTool", commit))
        write(package_root / "tools/net10.0/any/DotnetToolSettings.xml", pointer_settings(version))
        copy_common_files(root, package_root)
        return pack(package_root, output_dir, package_id, version)


def build_rid(root: Path, output_dir: Path, rid: str, version: str, commit: str) -> Path:
    binary = validate_binary(root, rid)
    with tempfile.TemporaryDirectory(prefix=f"scout-tool-{rid}-") as temp:
        package_root = Path(temp)
        package_id = f"{PACKAGE_ID}.{rid}"
        nuspec_name = f"{package_id}.nuspec"
        metadata_path = f"package/services/metadata/core-properties/{uuid.uuid4().hex}.psmdcp"
        tools_dir = package_root / "tools" / "any" / rid
        write(package_root / "_rels/.rels", rels(nuspec_name, metadata_path))
        write(package_root / "[Content_Types].xml", content_types(nuspec_name))
        write(package_root / metadata_path, core_properties(package_id, version))
        write(package_root / nuspec_name, package_metadata(package_id, version, "DotnetToolRidPackage", commit))
        write(tools_dir / "DotnetToolSettings.xml", rid_settings(rid))
        shutil.copyfile(binary, tools_dir / executable_name(rid))
        if not rid.startswith("win-"):
            os.chmod(tools_dir / executable_name(rid), 0o755)
            scout_real = root / "artifacts" / "bin" / rid / "scout-real"
            if not scout_real.is_file():
                raise RuntimeError(f"Missing native companion binary for {rid}: {scout_real}")
            shutil.copyfile(scout_real, tools_dir / "scout-real")
            os.chmod(tools_dir / "scout-real", 0o755)
        copy_common_files(root, package_root)
        return pack(package_root, output_dir, package_id, version)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", help="Package version. Defaults to Directory.Build.props VersionPrefix.")
    parser.add_argument("--output", default="artifacts/tool-packages", help="Output directory for .nupkg files.")
    parser.add_argument("--rid", action="append", choices=RIDS, help="RID package to build. Repeatable. Defaults to all RIDs.")
    parser.add_argument("--pointer-only", action="store_true", help="Build only the top-level Scout tool package.")
    parser.add_argument("--rid-only", action="store_true", help="Build only RID-specific tool packages.")
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    version = args.version or read_version(root)
    output_dir = (root / args.output).resolve()
    commit = run(["git", "rev-parse", "HEAD"], root)

    selected_rids = tuple(args.rid) if args.rid else RIDS
    built: list[Path] = []
    if not args.pointer_only:
        for rid in selected_rids:
            built.append(build_rid(root, output_dir, rid, version, commit))
    if not args.rid_only:
        built.append(build_pointer(root, output_dir, version, commit))

    for package in built:
        print(package)
    return 0


if __name__ == "__main__":
    sys.exit(main())
