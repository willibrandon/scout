#!/usr/bin/env python3
"""Install the existing validation SDK and runtimes from verified archive records."""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile


def extract_archive(archive: Path, destination: Path) -> None:
    """Extract validation files without allowing tar paths or links outside the destination."""
    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as source:
            source.extractall(destination)
    else:
        with tarfile.open(archive) as source:
            if callable(getattr(tarfile, "data_filter", None)):
                source.extractall(destination, filter="data")
            else:
                extract_tar_files(source, destination)


def extract_tar_files(source: tarfile.TarFile, destination: Path) -> None:
    """Copy only files and directories when the system Python lacks extraction filters."""
    root = destination.resolve()
    for member in source:
        target = (root / member.name).resolve()
        if Path(member.name).is_absolute() or not target.is_relative_to(root):
            raise ValueError(f"Tar member escapes the destination: {member.name}")
        if member.isdir():
            target.mkdir(parents=True, exist_ok=True)
        elif member.isfile():
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.extractfile(member) as contents, target.open("wb") as output:
                shutil.copyfileobj(contents, output)
            # Match data_filter's file permissions without restoring ownership or special bits.
            mode = member.mode & 0o755
            if not mode & 0o100:
                mode &= ~0o111
            target.chmod(mode | 0o600)
            os.utime(target, (member.mtime, member.mtime))
        else:
            raise ValueError(f"Tar links and special files require extraction filters: {member.name}")


def main() -> None:
    import tomllib

    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    args = parser.parse_args()
    lock = tomllib.loads((root / "tests/PREREQS.lock").read_text())
    destination = Path(os.environ.get("RUNNER_TEMP", tempfile.gettempdir())) / "scout-validation-dotnet" / args.rid
    destination.mkdir(parents=True, exist_ok=True)
    downloads = root / "artifacts/prereqs" / args.rid / "dotnet"
    downloads.mkdir(parents=True, exist_ok=True)
    for kind in ("dotnet_sdk_archive", "dotnet_library_runtime_archive", "dotnet_runtime_archive"):
        record = next(row for row in lock[kind] if row["rid"] == args.rid)
        archive = downloads / record["url"].rsplit("/", 1)[1]
        if not archive.is_file() or hashlib.sha512(archive.read_bytes()).hexdigest() != record["sha512"]:
            print(f"Downloading {archive.name}", flush=True)
            urllib.request.urlretrieve(record["url"], archive)
        if hashlib.sha512(archive.read_bytes()).hexdigest() != record["sha512"]:
            raise RuntimeError(f"Archive checksum mismatch: {archive.name}")
        extract_archive(archive, destination)
        if kind == "dotnet_sdk_archive":
            # The SDK's bundled runtime is replaced by the existing validation runtime.
            shutil.rmtree(destination / "host/fxr")
            shutil.rmtree(destination / "shared")
    executable = destination / ("dotnet.exe" if os.name == "nt" else "dotnet")
    environment = os.environ | {"DOTNET_ROOT": str(destination), "DOTNET_MULTILEVEL_LOOKUP": "0"}
    subprocess.run([str(executable), "--info"], cwd=root, env=environment, check=True)
    sdk = subprocess.check_output([str(executable), "--version"], cwd=root, env=environment, text=True).strip()
    if sdk != lock["dotnet_sdk"]:
        raise RuntimeError("Installed SDK differs from the validation record")
    if "GITHUB_PATH" in os.environ:
        with open(os.environ["GITHUB_PATH"], "a") as stream:
            stream.write(str(destination) + "\n")
        with open(os.environ["GITHUB_ENV"], "a") as stream:
            stream.write(f"DOTNET_ROOT={destination}\nDOTNET_MULTILEVEL_LOOKUP=0\n")
    print(f"Validation SDK ready: {destination}", flush=True)


if __name__ == "__main__":
    main()
