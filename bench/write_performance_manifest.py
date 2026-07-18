#!/usr/bin/env python3
"""Write deterministic performance-gate metadata manifests."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tempfile
from collections.abc import Sequence
from pathlib import Path


_NAME_PATTERN = re.compile(
    r"[a-z_][a-z0-9_]*(?:\.[a-z_][a-z0-9_]*)*"
)


def parse_value_specs(specifications: Sequence[str]) -> dict[str, str]:
    """Parse and validate unique NAME=VALUE metadata specifications."""
    if not specifications:
        raise ValueError("at least one --value is required")

    values: dict[str, str] = {}
    for specification in specifications:
        if "\n" in specification or "\r" in specification:
            raise ValueError(f"--value contains a newline: {specification!r}")
        if "=" not in specification:
            raise ValueError(
                f"--value must use NAME=VALUE syntax: {specification!r}"
            )

        name, value = specification.split("=", 1)
        if not _NAME_PATTERN.fullmatch(name):
            raise ValueError(
                f"invalid --value name {name!r}; use dotted "
                "lowercase identifier segments"
            )
        if name in values:
            raise ValueError(f"duplicate --value name: {name!r}")
        values[name] = value

    return values


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


def write_performance_manifest(
    output_path: Path,
    specifications: Sequence[str],
) -> dict[str, object]:
    """Validate metadata and atomically write its deterministic manifest."""
    values = parse_value_specs(specifications)
    document: dict[str, object] = {
        "schema_version": 1,
        "values": dict(sorted(values.items())),
    }
    _write_atomic_json(output_path, document)
    return document


def read_performance_manifest(path: Path) -> dict[str, object]:
    """Read and validate a deterministic performance-gate manifest."""
    with path.resolve(strict=True).open(encoding="utf-8") as stream:
        document = json.load(stream)
    if (
        not isinstance(document, dict)
        or set(document) != {"schema_version", "values"}
        or document.get("schema_version") != 1
    ):
        raise ValueError("performance manifest has an unsupported schema")

    values = document.get("values")
    if not isinstance(values, dict) or not values:
        raise ValueError("performance manifest values must be a nonempty object")
    for name, value in values.items():
        if (
            not isinstance(name, str)
            or not _NAME_PATTERN.fullmatch(name)
            or not isinstance(value, str)
            or "\n" in value
            or "\r" in value
        ):
            raise ValueError("performance manifest contains an invalid value")

    return document


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Write a deterministic performance-gate manifest."
    )
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--value",
        action="append",
        default=[],
        dest="values",
        metavar="NAME=VALUE",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Write a performance-gate metadata manifest from the command line."""
    arguments = _parse_args(sys.argv[1:] if argv is None else argv)
    try:
        write_performance_manifest(arguments.output, arguments.values)
    except (OSError, ValueError) as error:
        print(f"Performance manifest failed: {error}", file=sys.stderr)
        return 1

    print(f"Wrote performance manifest: {arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
