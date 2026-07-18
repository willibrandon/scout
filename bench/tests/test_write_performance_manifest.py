"""Tests for deterministic performance-gate metadata manifests."""

from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from write_performance_manifest import (
    main,
    parse_value_specs,
    read_performance_manifest,
    write_performance_manifest,
)


class WritePerformanceManifestTests(unittest.TestCase):
    """Verify metadata validation and atomic manifest creation."""

    def test_manifest_has_sorted_flat_values_and_schema_version(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "nested" / "manifest.json"

            document = write_performance_manifest(
                output,
                (
                    "toolchain.xcode_version=26.3",
                    "runner.name=GitHub Actions 42",
                    "process.umask=0022",
                ),
            )

            self.assertEqual(
                {
                    "schema_version": 1,
                    "values": {
                        "process.umask": "0022",
                        "runner.name": "GitHub Actions 42",
                        "toolchain.xcode_version": "26.3",
                    },
                },
                document,
            )
            self.assertEqual(
                document,
                json.loads(output.read_text(encoding="utf-8")),
            )
            self.assertTrue(output.read_bytes().endswith(b"\n"))

    def test_manifest_is_deterministic_for_reordered_values(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = root / "first.json"
            second = root / "second.json"

            write_performance_manifest(first, ("zeta=last", "alpha=first"))
            write_performance_manifest(second, ("alpha=first", "zeta=last"))

            self.assertEqual(first.read_bytes(), second.read_bytes())
            self.assertEqual([], list(root.glob(".*.json.*.tmp")))

    def test_reader_accepts_the_written_schema(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "manifest.json"
            expected = write_performance_manifest(output, ("host.os=Darwin",))

            self.assertEqual(expected, read_performance_manifest(output))

    def test_reader_rejects_malformed_or_unsupported_documents(self) -> None:
        invalid_documents = (
            {},
            {"schema_version": 2, "values": {"host.os": "Darwin"}},
            {"schema_version": 1, "values": {}},
            {"schema_version": 1, "values": {"Bad": "value"}},
            {"schema_version": 1, "values": {"host.os": 26}},
            {
                "schema_version": 1,
                "values": {"host.os": "line\nfeed"},
            },
        )
        for document in invalid_documents:
            with self.subTest(document=document):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    path = Path(temporary_directory) / "manifest.json"
                    path.write_text(
                        json.dumps(document) + "\n", encoding="utf-8"
                    )
                    with self.assertRaises(ValueError):
                        read_performance_manifest(path)

    def test_empty_values_and_dotted_underscore_names_are_supported(self) -> None:
        values = parse_value_specs(
            ("runner.image_os=", "toolchain._build=value=with=equals")
        )

        self.assertEqual(
            {
                "runner.image_os": "",
                "toolchain._build": "value=with=equals",
            },
            values,
        )

    def test_invalid_names_are_rejected(self) -> None:
        invalid_names = (
            "",
            ".leading",
            "trailing.",
            "two..dots",
            "Uppercase",
            "with-hyphen",
            "2leading",
            "with space",
        )
        for name in invalid_names:
            with self.subTest(name=name):
                with self.assertRaisesRegex(ValueError, "invalid --value name"):
                    parse_value_specs((f"{name}=value",))

    def test_missing_separator_duplicate_and_empty_set_are_rejected(self) -> None:
        cases = (
            ((), "at least one --value is required"),
            (("missing-separator",), "NAME=VALUE syntax"),
            (("same=first", "same=second"), "duplicate --value name"),
        )
        for specifications, message in cases:
            with self.subTest(specifications=specifications):
                with self.assertRaisesRegex(ValueError, message):
                    parse_value_specs(specifications)

    def test_newlines_in_names_or_values_are_rejected(self) -> None:
        for specification in (
            "bad\nname=value",
            "name=bad\nvalue",
            "name=bad\rvalue",
        ):
            with self.subTest(specification=specification):
                with self.assertRaisesRegex(ValueError, "contains a newline"):
                    parse_value_specs((specification,))

    def test_validation_error_preserves_existing_output(self) -> None:
        invalid_sets = (
            (),
            ("missing-separator",),
            ("Uppercase=value",),
            ("same=first", "same=second"),
            ("name=line\nfeed",),
        )
        for specifications in invalid_sets:
            with self.subTest(specifications=specifications):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    output = Path(temporary_directory) / "manifest.json"
                    output.write_text("existing\n", encoding="utf-8")

                    with self.assertRaises(ValueError):
                        write_performance_manifest(output, specifications)

                    self.assertEqual(
                        "existing\n", output.read_text(encoding="utf-8")
                    )

    def test_cli_reports_validation_errors_without_replacing_output(self) -> None:
        standard_error = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "manifest.json"
            output.write_text("existing\n", encoding="utf-8")

            with redirect_stderr(standard_error):
                exit_code = main(
                    ["--output", str(output), "--value", "Invalid=value"]
                )

            self.assertEqual("existing\n", output.read_text(encoding="utf-8"))

        self.assertEqual(1, exit_code)
        self.assertEqual(
            "Performance manifest failed: invalid --value name 'Invalid'; "
            "use dotted lowercase identifier segments\n",
            standard_error.getvalue(),
        )
        self.assertNotIn("Traceback", standard_error.getvalue())

    def test_successful_cli_writes_and_reports_manifest(self) -> None:
        standard_output = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "manifest.json"

            with redirect_stdout(standard_output):
                exit_code = main(
                    [
                        "--output",
                        str(output),
                        "--value",
                        "runner.name=local",
                    ]
                )

            self.assertEqual(
                {
                    "schema_version": 1,
                    "values": {"runner.name": "local"},
                },
                json.loads(output.read_text(encoding="utf-8")),
            )

        self.assertEqual(0, exit_code)
        self.assertEqual(
            f"Wrote performance manifest: {output}\n",
            standard_output.getvalue(),
        )


if __name__ == "__main__":
    unittest.main()
