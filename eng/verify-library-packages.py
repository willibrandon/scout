#!/usr/bin/env python3
"""Build and execute actual package consumers for both supported frameworks."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--version")
    parser.add_argument("--feed", type=Path)
    args = parser.parse_args()
    version = args.version or ET.parse(root / "Directory.Build.props").findtext(".//VersionPrefix")
    if not version:
        raise RuntimeError("Missing package version")
    output = root / "artifacts" / "package-consumers" / args.rid
    output.mkdir(parents=True, exist_ok=True)
    feed = (args.feed or output / "feed").resolve()
    feed.mkdir(parents=True, exist_ok=True)
    run_environment = os.environ.copy()

    def run(command: list[str], name: str, cwd: Path) -> None:
        print(f"{args.rid}: {name}", flush=True)
        log = output / f"{name}.log"
        with log.open("w") as stream:
            result = subprocess.run(command, cwd=cwd, env=run_environment, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError(f"{name} failed:\n{log.read_text()}")

    if args.feed is None:
        for project in ("Globbing", "Ignore", "Regex"):
            run(["dotnet", "pack", str(root / f"src/Scout.{project}/Scout.{project}.csproj"),
                 "-c", "Release", "-o", str(feed), f"-p:PackageVersion={version}"], f"pack-{project}", root)

    receipts = []
    with tempfile.TemporaryDirectory(prefix="scout-package-consumer-") as directory:
        consumer = Path(directory)
        run_environment["NUGET_PACKAGES"] = str(consumer / "packages")
        shutil.copyfile(root / "global.json", consumer / "global.json")
        template = root / "tests" / "Scout.Package.Consumer"
        for filename in ("Scout.Package.Consumer.csproj", "Program.cs", "Directory.Build.props"):
            shutil.copyfile(template / filename, consumer / filename)
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="scout-local", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        local = ET.SubElement(mapping, "packageSource", key="scout-local")
        ET.SubElement(local, "package", pattern="Scout.*")
        framework_source = ET.SubElement(mapping, "packageSource", key="nuget.org")
        ET.SubElement(framework_source, "package", pattern="Microsoft.*")
        ET.SubElement(framework_source, "package", pattern="runtime.*")
        ET.SubElement(framework_source, "package", pattern="System.*")
        ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        project = str(consumer / "Scout.Package.Consumer.csproj")
        common = ["-c", "Release", f"-p:ScoutPackageVersion={version}"]
        for framework in ("net9.0", "net10.0"):
            run(["dotnet", "run", "--project", project, "-f", framework, *common], f"{framework}-jit", consumer)
            receipts.append({"framework": framework, "mode": "jit", "executed": True})
            for mode in ("trimmed", "aot"):
                published = output / framework / mode
                property_value = "-p:PublishAot=true" if mode == "aot" else "-p:PublishTrimmed=true"
                run(["dotnet", "publish", project, "-f", framework, "-r", args.rid, *common,
                     "--self-contained", "true", property_value, "-o", str(published)], f"{framework}-{mode}-publish", consumer)
                executable = published / ("ScoutLibraryConsumer.exe" if os.name == "nt" else "ScoutLibraryConsumer")
                run([str(executable)], f"{framework}-{mode}-execute", consumer)
                receipts.append({"framework": framework, "mode": mode, "executed": True,
                                 "sha256": hashlib.sha256(executable.read_bytes()).hexdigest()})
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    packages = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(feed.glob("*.nupkg"))}
    (output / "results.json").write_text(json.dumps({"commit": commit, "rid": args.rid,
        "packages": packages, "consumers": receipts}, indent=2) + "\n")


if __name__ == "__main__":
    main()
