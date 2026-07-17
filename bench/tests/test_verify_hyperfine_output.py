"""Tests for untimed Hyperfine workload output verification."""

from __future__ import annotations

import json
import shlex
import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from verify_hyperfine_output import run_and_digest, verify_outputs


class VerifyHyperfineOutputTests(unittest.TestCase):
    """Verify normalized output comparison and failure handling."""

    def test_equal_line_multisets_match_across_ordering(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "verification.json"

            matches = verify_outputs(
                "sample",
                "printf 'beta\\nalpha\\n'",
                "printf 'alpha\\nbeta\\n'",
                output,
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertTrue(matches)
        self.assertTrue(document["matches"])
        self.assertEqual(
            document["rg"]["normalized_sha256"],
            document["scout"]["normalized_sha256"],
        )
        self.assertEqual(2, document["rg"]["lines"])

    def test_different_output_is_recorded_as_a_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "verification.json"

            matches = verify_outputs(
                "sample",
                "printf 'alpha\\n'",
                "printf 'beta\\n'",
                output,
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertFalse(matches)
        self.assertFalse(document["matches"])

    def test_nonzero_command_fails_verification(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)

            with self.assertRaisesRegex(RuntimeError, "exited with 7"):
                run_and_digest("printf 'failure' >&2; exit 7", directory)

    def test_direct_execution_uses_the_command_argv_without_a_shell(self) -> None:
        command = shlex.join([sys.executable, "-c", "print('alpha')"])
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "verification.json"

            matches = verify_outputs(
                "sample",
                command,
                command,
                output,
                no_shell=True,
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertTrue(matches)
        self.assertEqual("direct", document["execution_mode"])
        self.assertEqual(1, document["rg"]["lines"])


if __name__ == "__main__":
    unittest.main()
