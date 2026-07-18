#!/usr/bin/env python3
"""Verify generated performance-gate inputs against their locked digests."""

from __future__ import annotations

import argparse
import ast
import hashlib
import json
import os
import re
import sys
import tempfile
from collections.abc import Sequence
from pathlib import Path


_REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
_SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
_REQUIRED_FIELDS = ("name", "bytes", "sha256")


def _strip_toml_comment(line: str) -> str:
    quote = ""
    escaped = False
    for index, character in enumerate(line):
        if escaped:
            escaped = False
            continue
        if quote == '"' and character == "\\":
            escaped = True
            continue
        if character in ("'", '"'):
            if quote == character:
                quote = ""
            elif not quote:
                quote = character
            continue
        if character == "#" and not quote:
            return line[:index]
    return line


def _parse_scalar(value: str, field: str, line_number: int) -> object:
    try:
        return ast.literal_eval(value)
    except (SyntaxError, ValueError) as error:
        raise ValueError(
            f"invalid {field!r} value on lock line {line_number}: {value}"
        ) from error


def _complete_lock_record(
    record: dict[str, tuple[object, int]],
    start_line: int,
) -> tuple[str, int, str]:
    missing = [field for field in _REQUIRED_FIELDS if field not in record]
    if missing:
        raise ValueError(
            f"performance_input record on lock line {start_line} is missing "
            f"{', '.join(missing)}"
        )

    name_value, name_line = record["name"]
    byte_value, byte_line = record["bytes"]
    sha256_value, sha256_line = record["sha256"]
    if not isinstance(name_value, str) or not name_value:
        raise ValueError(
            f"performance_input name on lock line {name_line} must be a "
            "nonempty string"
        )

    if isinstance(byte_value, str) and byte_value.isdecimal():
        byte_count = int(byte_value)
    elif isinstance(byte_value, int) and not isinstance(byte_value, bool):
        byte_count = byte_value
    else:
        raise ValueError(
            f"performance_input bytes on lock line {byte_line} must be a "
            "nonnegative integer"
        )
    if byte_count < 0:
        raise ValueError(
            f"performance_input bytes on lock line {byte_line} must be a "
            "nonnegative integer"
        )

    if not isinstance(sha256_value, str) or not _SHA256_PATTERN.fullmatch(
        sha256_value
    ):
        raise ValueError(
            f"performance_input sha256 on lock line {sha256_line} must be "
            "exactly 64 lowercase hexadecimal characters"
        )

    return name_value, byte_count, sha256_value


def load_performance_input_lock(lock_path: Path) -> dict[str, tuple[int, str]]:
    """Load and validate every performance_input record from a lock file."""
    records: dict[str, tuple[int, str]] = {}
    current: dict[str, tuple[object, int]] | None = None
    current_start = 0

    def finish_current() -> None:
        nonlocal current
        if current is None:
            return
        name, byte_count, sha256 = _complete_lock_record(
            current, current_start
        )
        if name in records:
            raise ValueError(
                f"duplicate performance_input lock record for {name!r}"
            )
        records[name] = (byte_count, sha256)
        current = None

    with lock_path.open(encoding="utf-8") as stream:
        for line_number, raw_line in enumerate(stream, start=1):
            line = _strip_toml_comment(raw_line).strip()
            if not line:
                continue

            if line.startswith("["):
                finish_current()
                if line == "[[performance_input]]":
                    current = {}
                    current_start = line_number
                continue

            if current is None:
                continue
            if "=" not in line:
                raise ValueError(
                    f"invalid performance_input field on lock line "
                    f"{line_number}: {line}"
                )
            field, raw_value = line.split("=", 1)
            field = field.strip()
            raw_value = raw_value.strip()
            if field not in _REQUIRED_FIELDS:
                continue
            if field in current:
                raise ValueError(
                    f"duplicate {field!r} field in performance_input "
                    f"record on lock line {line_number}"
                )
            current[field] = (
                _parse_scalar(raw_value, field, line_number),
                line_number,
            )

    finish_current()
    if not records:
        raise ValueError(
            f"lock contains no performance_input records: {lock_path}"
        )
    return records


def parse_input_specs(specifications: Sequence[str]) -> dict[str, tuple[Path, str]]:
    """Parse unique NAME=PATH input specifications."""
    inputs: dict[str, tuple[Path, str]] = {}
    for specification in specifications:
        if "=" not in specification:
            raise ValueError(
                f"input must use NAME=PATH syntax: {specification!r}"
            )
        name, supplied_path = specification.split("=", 1)
        name = name.strip()
        if not name:
            raise ValueError(
                f"input name must not be empty: {specification!r}"
            )
        if not supplied_path:
            raise ValueError(f"input path for {name!r} must not be empty")
        if name in inputs:
            raise ValueError(f"duplicate --input name: {name!r}")
        inputs[name] = (Path(supplied_path), supplied_path)
    return inputs


def _digest_file(path: Path) -> tuple[int, str]:
    digest = hashlib.sha256()
    byte_count = 0
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                byte_count += len(chunk)
                digest.update(chunk)
    except FileNotFoundError as error:
        raise ValueError(f"input file not found: {path}") from error
    return byte_count, digest.hexdigest()


def _display_path(
    path: Path, supplied_path: str, repository_root: Path
) -> str:
    resolved_path = path.resolve(strict=True)
    try:
        return resolved_path.relative_to(repository_root.resolve()).as_posix()
    except ValueError:
        return supplied_path


def _write_atomic_json(output_path: Path, document: object) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            dir=output_path.parent,
            prefix=f".{output_path.name}.",
            suffix=".tmp",
            delete=False,
        ) as stream:
            temporary_path = Path(stream.name)
            json.dump(document, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, output_path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def verify_performance_inputs(
    lock_path: Path,
    output_path: Path,
    specifications: Sequence[str],
    *,
    repository_root: Path | None = None,
) -> dict[str, object]:
    """Verify a complete generated input set and atomically record its identity."""
    locked_inputs = load_performance_input_lock(lock_path)
    supplied_inputs = parse_input_specs(specifications)
    missing = sorted(locked_inputs.keys() - supplied_inputs.keys())
    unexpected = sorted(supplied_inputs.keys() - locked_inputs.keys())
    if missing or unexpected:
        details = []
        if missing:
            details.append(f"missing inputs: {', '.join(missing)}")
        if unexpected:
            details.append(f"unexpected inputs: {', '.join(unexpected)}")
        raise ValueError("; ".join(details))

    root = _REPOSITORY_ROOT if repository_root is None else repository_root
    verified = []
    for name in sorted(locked_inputs):
        path, supplied_path = supplied_inputs[name]
        expected_bytes, expected_sha256 = locked_inputs[name]
        actual_bytes, actual_sha256 = _digest_file(path)
        if actual_bytes != expected_bytes:
            raise ValueError(
                f"{name!r} byte count mismatch for {supplied_path}: "
                f"expected {expected_bytes}, got {actual_bytes}"
            )
        if actual_sha256 != expected_sha256:
            raise ValueError(
                f"{name!r} sha256 mismatch for {supplied_path}: "
                f"expected {expected_sha256}, got {actual_sha256}"
            )
        verified.append(
            {
                "name": name,
                "path": _display_path(path, supplied_path, root),
                "bytes": actual_bytes,
                "sha256": actual_sha256,
            }
        )

    document: dict[str, object] = {"inputs": verified}
    _write_atomic_json(output_path, document)
    return document


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Verify generated performance-gate inputs against PREREQS.lock."
        )
    )
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--input",
        action="append",
        required=True,
        dest="inputs",
        metavar="NAME=PATH",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run generated performance-input verification from the command line."""
    arguments = _parse_args(sys.argv[1:] if argv is None else argv)
    try:
        document = verify_performance_inputs(
            arguments.lock,
            arguments.output,
            arguments.inputs,
        )
    except (OSError, ValueError) as error:
        print(
            f"Performance input verification failed: {error}",
            file=sys.stderr,
        )
        return 1

    print(
        f"Verified {len(document['inputs'])} generated performance inputs: "
        f"{arguments.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
