"""Tests for generated performance-gate input verification."""

from __future__ import annotations

import hashlib
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path


import sys


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from verify_performance_inputs import (
    load_performance_input_lock,
    main,
    parse_input_specs,
    verify_performance_inputs,
)


class VerifyPerformanceInputsTests(unittest.TestCase):
    """Verify locked input parsing, hashing, and manifest creation."""

    def test_verification_sorts_records_and_uses_useful_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary = Path(temporary_directory)
            repository = temporary / "repository"
            internal = repository / "artifacts" / "generated.txt"
            external = temporary / "external.txt"
            internal.parent.mkdir(parents=True)
            internal.write_bytes(b"internal\n")
            external.write_bytes(b"external\n")
            lock = repository / "PREREQS.lock"
            self._write_lock(
                lock,
                (("zeta", external), ("alpha", internal)),
            )
            output = temporary / "nested" / "manifest.json"

            document = verify_performance_inputs(
                lock,
                output,
                (
                    f"zeta={external}",
                    f"alpha={internal}",
                ),
                repository_root=repository,
            )

            self.assertEqual(["alpha", "zeta"], self._names(document))
            self.assertEqual(
                "artifacts/generated.txt",
                document["inputs"][0]["path"],
            )
            self.assertEqual(str(external), document["inputs"][1]["path"])
            self.assertEqual(
                document,
                json.loads(output.read_text(encoding="utf-8")),
            )
            self.assertTrue(output.read_bytes().endswith(b"\n"))

    def test_manifest_is_deterministic_for_reordered_arguments(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = root / "first"
            second = root / "second"
            first.write_bytes(b"first")
            second.write_bytes(b"second")
            lock = root / "PREREQS.lock"
            self._write_lock(lock, (("second", second), ("first", first)))
            output = root / "manifest.json"

            verify_performance_inputs(
                lock,
                output,
                (f"second={second}", f"first={first}"),
                repository_root=root,
            )
            initial = output.read_bytes()
            verify_performance_inputs(
                lock,
                output,
                (f"first={first}", f"second={second}"),
                repository_root=root,
            )

            self.assertEqual(initial, output.read_bytes())
            self.assertEqual([], list(root.glob(".manifest.json.*.tmp")))

    def test_input_names_must_exactly_match_the_lock(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            locked = root / "locked"
            unexpected = root / "unexpected"
            locked.write_bytes(b"locked")
            unexpected.write_bytes(b"unexpected")
            lock = root / "PREREQS.lock"
            self._write_lock(lock, (("locked", locked),))

            with self.assertRaisesRegex(
                ValueError,
                "missing inputs: locked; unexpected inputs: unexpected",
            ):
                verify_performance_inputs(
                    lock,
                    root / "manifest.json",
                    (f"unexpected={unexpected}",),
                )

    def test_duplicate_and_malformed_input_specs_are_rejected(self) -> None:
        cases = (
            (("missing-separator",), "NAME=PATH syntax"),
            (("=path",), "input name must not be empty"),
            (("name=",), "input path for 'name' must not be empty"),
            (("name=one", "name=two"), "duplicate --input name: 'name'"),
        )
        for specifications, message in cases:
            with self.subTest(specifications=specifications):
                with self.assertRaisesRegex(ValueError, message):
                    parse_input_specs(specifications)

    def test_duplicate_lock_record_is_rejected(self) -> None:
        digest = "a" * 64
        fixture = (
            "[[performance_input]]\n"
            'name = "same"\n'
            'bytes = "1"\n'
            f'sha256 = "{digest}"\n\n'
        ) * 2
        with tempfile.TemporaryDirectory() as temporary_directory:
            lock = Path(temporary_directory) / "PREREQS.lock"
            lock.write_text(fixture, encoding="utf-8")

            with self.assertRaisesRegex(
                ValueError, "duplicate performance_input lock record for 'same'"
            ):
                load_performance_input_lock(lock)

    def test_duplicate_required_field_is_rejected(self) -> None:
        digest = "a" * 64
        fixture = (
            "[[performance_input]]\n"
            'name = "first"\n'
            'name = "second"\n'
            'bytes = "1"\n'
            f'sha256 = "{digest}"\n'
        )
        with tempfile.TemporaryDirectory() as temporary_directory:
            lock = Path(temporary_directory) / "PREREQS.lock"
            lock.write_text(fixture, encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "duplicate 'name' field"):
                load_performance_input_lock(lock)

    def test_incomplete_lock_records_are_rejected(self) -> None:
        fields = {
            "name": 'name = "sample"',
            "bytes": 'bytes = "1"',
            "sha256": f'sha256 = "{"a" * 64}"',
        }
        for missing_field in fields:
            with self.subTest(missing_field=missing_field):
                fixture = "[[performance_input]]\n" + "\n".join(
                    value
                    for field, value in fields.items()
                    if field != missing_field
                )
                with tempfile.TemporaryDirectory() as temporary_directory:
                    lock = Path(temporary_directory) / "PREREQS.lock"
                    lock.write_text(fixture, encoding="utf-8")

                    with self.assertRaisesRegex(
                        ValueError, f"is missing {missing_field}"
                    ):
                        load_performance_input_lock(lock)

    def test_invalid_locked_bytes_and_sha256_are_rejected(self) -> None:
        cases = (
            ('bytes = "-1"', f'sha256 = "{"a" * 64}"', "bytes"),
            ('bytes = "abc"', f'sha256 = "{"a" * 64}"', "bytes"),
            ('bytes = "1"', f'sha256 = "{"A" * 64}"', "sha256"),
            ('bytes = "1"', 'sha256 = "short"', "sha256"),
        )
        for byte_field, sha256_field, expected in cases:
            with self.subTest(
                byte_field=byte_field, sha256_field=sha256_field
            ):
                fixture = (
                    "[[performance_input]]\n"
                    'name = "sample"\n'
                    f"{byte_field}\n"
                    f"{sha256_field}\n"
                )
                with tempfile.TemporaryDirectory() as temporary_directory:
                    lock = Path(temporary_directory) / "PREREQS.lock"
                    lock.write_text(fixture, encoding="utf-8")

                    with self.assertRaisesRegex(ValueError, expected):
                        load_performance_input_lock(lock)

    def test_comments_and_integer_byte_counts_are_supported(self) -> None:
        digest = "a" * 64
        fixture = (
            "[[performance_input]] # generated gate input\n"
            'name = "hash#name" # comment\n'
            "bytes = 1\n"
            f'sha256 = "{digest}"\n'
        )
        with tempfile.TemporaryDirectory() as temporary_directory:
            lock = Path(temporary_directory) / "PREREQS.lock"
            lock.write_text(fixture, encoding="utf-8")

            records = load_performance_input_lock(lock)

        self.assertEqual({"hash#name": (1, digest)}, records)

    def test_byte_count_mismatch_preserves_existing_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            input_path = root / "input"
            input_path.write_bytes(b"actual")
            digest = hashlib.sha256(b"actual").hexdigest()
            lock = root / "PREREQS.lock"
            lock.write_text(
                "[[performance_input]]\n"
                'name = "sample"\n'
                'bytes = "99"\n'
                f'sha256 = "{digest}"\n',
                encoding="utf-8",
            )
            output = root / "manifest.json"
            output.write_text("existing\n", encoding="utf-8")

            with self.assertRaisesRegex(
                ValueError, "byte count mismatch.*expected 99, got 6"
            ):
                verify_performance_inputs(
                    lock, output, (f"sample={input_path}",)
                )

            self.assertEqual("existing\n", output.read_text(encoding="utf-8"))

    def test_sha256_mismatch_is_reported_with_expected_and_actual_values(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            input_path = root / "input"
            input_path.write_bytes(b"actual")
            lock = root / "PREREQS.lock"
            lock.write_text(
                "[[performance_input]]\n"
                'name = "sample"\n'
                'bytes = "6"\n'
                f'sha256 = "{"a" * 64}"\n',
                encoding="utf-8",
            )
            actual = hashlib.sha256(b"actual").hexdigest()

            with self.assertRaisesRegex(
                ValueError, f"expected {'a' * 64}, got {actual}"
            ):
                verify_performance_inputs(
                    lock, root / "manifest.json", (f"sample={input_path}",)
                )

    def test_missing_input_file_is_reported_by_the_cli(self) -> None:
        standard_error = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            missing = root / "missing"
            lock = root / "PREREQS.lock"
            lock.write_text(
                "[[performance_input]]\n"
                'name = "sample"\n'
                'bytes = "0"\n'
                f'sha256 = "{hashlib.sha256().hexdigest()}"\n',
                encoding="utf-8",
            )

            with redirect_stderr(standard_error):
                exit_code = main(
                    [
                        "--lock",
                        str(lock),
                        "--output",
                        str(root / "manifest.json"),
                        "--input",
                        f"sample={missing}",
                    ]
                )

        self.assertEqual(1, exit_code)
        self.assertIn(
            "Performance input verification failed:",
            standard_error.getvalue(),
        )
        self.assertIn(str(missing), standard_error.getvalue())

    def test_successful_cli_reports_the_manifest(self) -> None:
        standard_output = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            input_path = root / "input"
            input_path.write_bytes(b"sample")
            lock = root / "PREREQS.lock"
            self._write_lock(lock, (("sample", input_path),))
            output = root / "manifest.json"

            with redirect_stdout(standard_output):
                exit_code = main(
                    [
                        "--lock",
                        str(lock),
                        "--output",
                        str(output),
                        "--input",
                        f"sample={input_path}",
                    ]
                )

            self.assertTrue(output.is_file())

        self.assertEqual(0, exit_code)
        self.assertIn(
            "Verified 1 generated performance inputs:",
            standard_output.getvalue(),
        )

    def test_repository_lock_exposes_seven_unique_complete_inputs(self) -> None:
        root = Path(__file__).resolve().parents[2]

        records = load_performance_input_lock(root / "tests" / "PREREQS.lock")

        self.assertEqual(
            {
                "bounded-assignment-no-match-800",
                "bounded-assignment-pattern",
                "cold-tiny",
                "large-bounded-unicode-class-no-match-5000",
                "large-bounded-unicode-class-pattern",
                "line-regex-absent-patterns-64",
                "line-regex-paladin-like-200000",
            },
            set(records),
        )

    @staticmethod
    def _write_lock(
        lock: Path, records: tuple[tuple[str, Path], ...]
    ) -> None:
        lock.parent.mkdir(parents=True, exist_ok=True)
        sections = []
        for name, path in records:
            content = path.read_bytes()
            sections.append(
                "[[performance_input]]\n"
                f'name = "{name}"\n'
                f'bytes = "{len(content)}"\n'
                f'sha256 = "{hashlib.sha256(content).hexdigest()}"\n'
            )
        lock.write_text("\n".join(sections), encoding="utf-8")

    @staticmethod
    def _names(document: dict[str, object]) -> list[str]:
        return [record["name"] for record in document["inputs"]]


if __name__ == "__main__":
    unittest.main()
