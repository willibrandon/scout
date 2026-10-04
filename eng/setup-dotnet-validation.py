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
import tomllib
import urllib.request
import zipfile


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    args = parser.parse_args()
    lock = tomllib.loads((root / "tests/PREREQS.lock").read_text())
    destination = root / "artifacts/validation-dotnet" / args.rid
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
        if archive.suffix == ".zip":
            with zipfile.ZipFile(archive) as source:
                source.extractall(destination)
        else:
            with tarfile.open(archive) as source:
                source.extractall(destination, filter="data")
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
