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
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any

from benchmark_environment import (
    add_benchmark_environment_assignments,
    create_benchmark_environment,
)
from write_performance_manifest import read_performance_manifest


_REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
_SHELL = "/bin/sh" if os.name == "posix" else shutil.which("sh")


def _windows_posix_tool(shell: str | None, name: str) -> str | None:
    if shell is None:
        return None

    shell_path = Path(shell)
    candidate = shell_path.with_name(name + shell_path.suffix)
    return str(candidate) if candidate.is_file() else None


_SORT = (
    "/usr/bin/sort"
    if os.name == "posix"
    else _windows_posix_tool(_SHELL, "sort")
)


def run_and_digest(
    command: str,
    directory: Path,
    *,
    working_directory: Path = _REPOSITORY_ROOT,
    expected_exit_code: int = 0,
    environment: Mapping[str, str] | None = None,
) -> dict[str, Any]:
    """Run one benchmark command and return its normalized output digest."""
    temporary_path: Path | None = None
    try:
        if _SORT is None:
            raise RuntimeError("sort is required")

        if environment is None:
            environment = create_benchmark_environment(
                directory / "standalone-output"
            )

        arguments = shlex.split(command)
        if not arguments:
            raise ValueError("benchmark command must not be empty")

        with tempfile.NamedTemporaryFile(
            dir=directory, prefix="output-", suffix=".tmp", delete=False
        ) as stream:
            temporary_path = Path(stream.name)
            completed = subprocess.run(
                arguments,
                stdout=stream,
                stderr=subprocess.PIPE,
                check=False,
                cwd=working_directory,
                env=environment,
            )

        if completed.returncode != expected_exit_code:
            detail = completed.stderr.decode("utf-8", errors="replace").strip()
            raise RuntimeError(
                f"command expected exit {expected_exit_code}, got "
                f"{completed.returncode}: {detail}"
            )

        return _digest_sorted_lines(temporary_path, environment)
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def verify_outputs(
    workload: str,
    rg_command: str,
    scout_command: str,
    output_path: Path,
    *,
    working_directory: Path = _REPOSITORY_ROOT,
    expected_exit_code: int = 0,
    environment_assignments: Sequence[str] = (),
    performance_input_manifest: Path | None = None,
    reproducibility_manifest: Path | None = None,
    output_policy: str = "equivalent",
) -> bool:
    """Verify and record normalized output digests for one workload."""
    if output_policy not in ("equivalent", "independent"):
        raise ValueError(f"unsupported output policy: {output_policy}")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    working_directory = working_directory.resolve(strict=True)
    if not working_directory.is_dir():
        raise ValueError(
            f"working directory is not a directory: {working_directory}"
        )
    environment = add_benchmark_environment_assignments(
        create_benchmark_environment(output_path), environment_assignments
    )
    if performance_input_manifest is None:
        raise ValueError("generated performance-input manifest is required")
    with performance_input_manifest.resolve(strict=True).open(
        encoding="utf-8"
    ) as stream:
        performance_input_document = json.load(stream)
    performance_inputs = performance_input_document.get("inputs")
    if not isinstance(performance_inputs, list) or not performance_inputs:
        raise ValueError(
            "generated performance-input manifest must contain a nonempty "
            "inputs array"
        )
    if reproducibility_manifest is None:
        raise ValueError("reproducibility manifest is required")
    reproducibility = read_performance_manifest(reproducibility_manifest)
    rg = run_and_digest(
        rg_command,
        output_path.parent,
        working_directory=working_directory,
        expected_exit_code=expected_exit_code,
        environment=environment,
    )
    scout = run_and_digest(
        scout_command,
        output_path.parent,
        working_directory=working_directory,
        expected_exit_code=expected_exit_code,
        environment=environment,
    )
    matches = rg["normalized_sha256"] == scout["normalized_sha256"]
    document = {
        "workload": workload,
        "normalization": "C-locale sorted lines",
        "output_policy": output_policy,
        "execution_mode": "direct",
        "expected_exit_code": expected_exit_code,
        "performance_inputs": performance_inputs,
        "reproducibility": reproducibility,
        "environment": dict(environment),
        "working_directory": str(working_directory),
        "matches": matches,
        "command_argv": {
            "rg": shlex.split(rg_command),
            "scout": shlex.split(scout_command),
        },
        "rg": rg,
        "scout": scout,
    }
    temporary_output = output_path.with_suffix(output_path.suffix + ".tmp")
    temporary_output.write_text(
        json.dumps(document, indent=2) + "\n", encoding="utf-8"
    )
    temporary_output.replace(output_path)
    return matches or output_policy == "independent"


def _digest_sorted_lines(
    path: Path, environment: Mapping[str, str]
) -> dict[str, Any]:
    digest = hashlib.sha256()
    process = subprocess.Popen(
        [_SORT, str(path)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=_REPOSITORY_ROOT,
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
    parser.add_argument("--working-directory", required=True, type=Path)
    parser.add_argument(
        "--performance-input-manifest", required=True, type=Path
    )
    parser.add_argument(
        "--reproducibility-manifest", required=True, type=Path
    )
    parser.add_argument(
        "--expected-exit-code", required=True, type=_exit_code
    )
    parser.add_argument("--environment", action="append", default=[])
    parser.add_argument(
        "--output-policy",
        required=True,
        choices=("equivalent", "independent"),
    )
    return parser.parse_args(argv)


def _exit_code(value: str) -> int:
    number = int(value)
    if number < 0 or number > 255:
        raise argparse.ArgumentTypeError("must be between 0 and 255")
    return number


def main(argv: Sequence[str] | None = None) -> int:
    """Run output verification from the command line."""
    arguments = _parse_args(sys.argv[1:] if argv is None else argv)
    try:
        matches = verify_outputs(
            arguments.workload,
            arguments.rg_command,
            arguments.scout_command,
            arguments.output,
            working_directory=arguments.working_directory,
            expected_exit_code=arguments.expected_exit_code,
            environment_assignments=arguments.environment,
            performance_input_manifest=arguments.performance_input_manifest,
            reproducibility_manifest=arguments.reproducibility_manifest,
            output_policy=arguments.output_policy,
        )
    except (OSError, RuntimeError, ValueError) as error:
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
    if arguments.output_policy == "equivalent":
        print(f"Output verified: {lines} lines; normalized sha256={digest}")
    else:
        print("Outputs recorded independently; both commands exited as expected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
