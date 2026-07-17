#!/usr/bin/env python3
"""Verify that one rg/Scout benchmark pair produces the same output multiset."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile
from collections.abc import Sequence
from pathlib import Path
from typing import Any


_SHELL = "/bin/sh" if os.name == "posix" else shutil.which("sh")


def run_and_digest(
    command: str, directory: Path, *, no_shell: bool = False
) -> dict[str, Any]:
    """Run one benchmark command and return its normalized output digest."""
    temporary_path: Path | None = None
    try:
        if not no_shell and _SHELL is None:
            raise RuntimeError("a POSIX shell is required")

        arguments = shlex.split(command) if no_shell else [_SHELL, "-c", command]

        with tempfile.NamedTemporaryFile(
            dir=directory, prefix="output-", suffix=".tmp", delete=False
        ) as stream:
            temporary_path = Path(stream.name)
            completed = subprocess.run(
                arguments,
                stdout=stream,
                stderr=subprocess.PIPE,
                check=False,
            )

        if completed.returncode != 0:
            detail = completed.stderr.decode("utf-8", errors="replace").strip()
            raise RuntimeError(
                f"command exited with {completed.returncode}: {detail}"
            )

        return _digest_sorted_lines(temporary_path)
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def verify_outputs(
    workload: str,
    rg_command: str,
    scout_command: str,
    output_path: Path,
    *,
    no_shell: bool = False,
) -> bool:
    """Verify and record normalized output digests for one workload."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    rg = run_and_digest(rg_command, output_path.parent, no_shell=no_shell)
    scout = run_and_digest(scout_command, output_path.parent, no_shell=no_shell)
    matches = rg["normalized_sha256"] == scout["normalized_sha256"]
    document = {
        "workload": workload,
        "normalization": "C-locale sorted lines",
        "execution_mode": "direct" if no_shell else "shell",
        "matches": matches,
        "rg": rg,
        "scout": scout,
    }
    temporary_output = output_path.with_suffix(output_path.suffix + ".tmp")
    temporary_output.write_text(
        json.dumps(document, indent=2) + "\n", encoding="utf-8"
    )
    temporary_output.replace(output_path)
    return matches


def _digest_sorted_lines(path: Path) -> dict[str, Any]:
    digest = hashlib.sha256()
    environment = dict(os.environ)
    environment["LC_ALL"] = "C"
    process = subprocess.Popen(
        ["sort", str(path)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        env=environment,
    )
    assert process.stdout is not None
    for chunk in iter(lambda: process.stdout.read(1024 * 1024), b""):
        digest.update(chunk)
    stderr = process.communicate()[1]
    if process.returncode != 0:
        detail = stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(f"sort exited with {process.returncode}: {detail}")

    byte_count = 0
    line_count = 0
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            byte_count += len(chunk)
            line_count += chunk.count(b"\n")

    return {
        "normalized_sha256": digest.hexdigest(),
        "bytes": byte_count,
        "lines": line_count,
    }


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Verify one Scout Hyperfine workload against rg."
    )
    parser.add_argument("--workload", required=True)
    parser.add_argument("--rg-command", required=True)
    parser.add_argument("--scout-command", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--no-shell", action="store_true")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run output verification from the command line."""
    arguments = _parse_args(sys.argv[1:] if argv is None else argv)
    try:
        matches = verify_outputs(
            arguments.workload,
            arguments.rg_command,
            arguments.scout_command,
            arguments.output,
            no_shell=arguments.no_shell,
        )
    except (OSError, RuntimeError) as error:
        print(f"Output verification failed: {error}", file=sys.stderr)
        return 2

    if not matches:
        print(
            f"Output verification failed: rg and Scout differ "
            f"({arguments.workload}).",
            file=sys.stderr,
        )
        return 1

    with arguments.output.open(encoding="utf-8") as stream:
        document = json.load(stream)
    digest = document["rg"]["normalized_sha256"]
    lines = document["rg"]["lines"]
    print(f"Output verified: {lines} lines; normalized sha256={digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
