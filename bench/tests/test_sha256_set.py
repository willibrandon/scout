"""Shell-level tests for strict pinned SHA-256 sets."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")
_HASH_A = "a" * 64
_HASH_B = "b" * 64
_HASH_C = "c" * 64
_HASH_WITH_NON_HEX_CHARACTER = "b" * 63 + "g"


@unittest.skipUnless(_SH, "requires a POSIX shell")
class Sha256SetTests(unittest.TestCase):
    """Exercise the sourceable SHA-256 set helper through a POSIX shell."""

    def test_scalar_decodes_to_one_hash(self) -> None:
        result = self._run("decode_sha256_set", _HASH_A)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(f"{_HASH_A}\n", result.stdout)
        self.assertEqual("", result.stderr)

    def test_array_decodes_in_declared_order(self) -> None:
        result = self._run(
            "decode_sha256_set", f'  [ "{_HASH_A}" ,\t"{_HASH_B}" ]  '
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(f"{_HASH_A}\n{_HASH_B}\n", result.stdout)
        self.assertEqual("", result.stderr)

    def test_single_element_array_is_nonempty_and_valid(self) -> None:
        result = self._run("decode_sha256_set", f'["{_HASH_A}"]')

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(f"{_HASH_A}\n", result.stdout)
        self.assertEqual("", result.stderr)

    def test_membership_accepts_each_exact_approved_hash(self) -> None:
        value = f'["{_HASH_A}", "{_HASH_B}"]'

        for approved_hash in (_HASH_A, _HASH_B):
            with self.subTest(approved_hash=approved_hash):
                result = self._run(
                    "sha256_set_contains", value, approved_hash
                )

                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual("", result.stdout)
                self.assertEqual("", result.stderr)

        scalar_result = self._run("sha256_set_contains", _HASH_A, _HASH_A)
        self.assertEqual(0, scalar_result.returncode, scalar_result.stderr)
        self.assertEqual("", scalar_result.stderr)

    def test_membership_rejects_an_exact_unapproved_hash(self) -> None:
        value = f'["{_HASH_A}", "{_HASH_B}"]'

        result = self._run("sha256_set_contains", value, _HASH_C)

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertEqual("", result.stdout)
        self.assertEqual("", result.stderr)

    def test_membership_does_not_accept_prefixes_or_surrounding_text(self) -> None:
        for candidate in (_HASH_A[:-1], f"0{_HASH_A}", f"{_HASH_A}0"):
            with self.subTest(candidate=candidate):
                result = self._run(
                    "sha256_set_contains", _HASH_A, candidate
                )

                self.assertEqual(2, result.returncode, result.stderr)
                self.assertEqual("", result.stdout)
                self.assertEqual("", result.stderr)

    def test_malformed_values_are_rejected_without_partial_output(self) -> None:
        malformed_values = (
            "",
            "[]",
            "[ ]",
            f'"{_HASH_A}"',
            _HASH_A.upper(),
            _HASH_A[:-1],
            f"{_HASH_A}a",
            f"{'a' * 63}g",
            f'["{_HASH_A}"',
            f'"{_HASH_A}"]',
            f'[{_HASH_A}]',
            f'["{_HASH_A}",]',
            f'[,"{_HASH_A}"]',
            f'["{_HASH_A}",,"{_HASH_B}"]',
            f'["{_HASH_A}" "{_HASH_B}"]',
            f'["{_HASH_A}", ""]',
            f'["{_HASH_A}", "{_HASH_A}"]',
            f'["{_HASH_A}", "{_HASH_B.upper()}"]',
            f'["{_HASH_A}", "{_HASH_B[:-1]}"]',
            f'["{_HASH_A}", "{_HASH_WITH_NON_HEX_CHARACTER}"]',
            f'["{_HASH_A}",\n"{_HASH_B}"]',
            f'["{_HASH_A}",\r"{_HASH_B}"]',
            "-v",
            "--",
        )

        for value in malformed_values:
            with self.subTest(value=value):
                result = self._run("decode_sha256_set", value)

                self.assertEqual(2, result.returncode, result.stderr)
                self.assertEqual("", result.stdout)
                self.assertEqual("", result.stderr)

    def test_malformed_candidate_is_distinct_from_an_unapproved_hash(self) -> None:
        for candidate in ("", _HASH_A.upper(), f'["{_HASH_A}"]'):
            with self.subTest(candidate=candidate):
                result = self._run(
                    "sha256_set_contains", _HASH_A, candidate
                )

                self.assertEqual(2, result.returncode, result.stderr)
                self.assertEqual("", result.stderr)

    def test_membership_rejects_a_malformed_approved_set(self) -> None:
        result = self._run(
            "sha256_set_contains", f'["{_HASH_A}", "{_HASH_A}"]', _HASH_A
        )

        self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("", result.stdout)
        self.assertEqual("", result.stderr)

    def test_preflight_selects_hashes_from_the_exact_tool_scope(self) -> None:
        cases = (
            (
                "osx-arm64",
                "github-actions",
                "xz",
                "16b9994cca884ed2a66ba63736f1450049cbc6fd1d93076c51e5f0e7f7a71381\n"
                "995c8e2f72446f0d0e3a29f6c3d52286cfecedfc4ffb2b42d25c3ce1ad77034c\n",
            ),
            (
                "osx-arm64",
                "github-actions",
                "zstd",
                "9b5676aae3cb048cf68e2b40c543d9523db3b4cb911b31861bd5f4fcb050c4b6\n"
                "aff8169fb421bb925fb16c44a7e0143fa2c7a941dc45cce76b15062a2ce54917\n",
            ),
            (
                "osx-x64",
                "github-actions",
                "xz",
                "2ce7374ab7c6426659e3662a6a759df41e03e30bfd90898073bab1d77f7c51b2\n",
            ),
            (
                "osx-arm64",
                "local",
                "xz",
                "b7926ea19abf39913ee064329261d03ec66271cf5ee4759e5a1a928a3e165540\n",
            ),
        )

        for rid, environment, name, expected in cases:
            with self.subTest(rid=rid, environment=environment, name=name):
                result = self._run_preflight_hash_selection(
                    rid, environment, name
                )

                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, result.stdout)
                self.assertEqual("", result.stderr)

    def test_preflight_does_not_inherit_from_an_incomplete_exact_row(self) -> None:
        fixture = f"""[[tool.macos]]
name = "xz"
sha256 = ["{_HASH_A}", "{_HASH_B}"]

[[tool.macos]]
name = "xz"
rid = "osx-arm64"
environment = "github-actions"
version = "5.8.3"
"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            lock_path = Path(temporary_directory) / "PREREQS.lock"
            lock_path.write_text(fixture, encoding="utf-8")
            result = self._run_preflight_hash_selection(
                "osx-arm64", "github-actions", "xz", lock_path
            )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", result.stdout)
        self.assertEqual("", result.stderr)

    def test_wrong_argument_counts_are_rejected(self) -> None:
        cases = (
            ("decode_sha256_set",),
            ("decode_sha256_set", _HASH_A, _HASH_B),
            ("sha256_set_contains", _HASH_A),
            ("sha256_set_contains", _HASH_A, _HASH_B, _HASH_C),
        )

        for arguments in cases:
            with self.subTest(arguments=arguments):
                result = self._run(*arguments)

                self.assertEqual(2, result.returncode, result.stderr)
                self.assertEqual("", result.stderr)

    @staticmethod
    def _run(*arguments: str) -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        environment = os.environ.copy()
        environment["SCOUT_SHA256_TEST_COMMAND"] = arguments[0]
        environment["SCOUT_SHA256_TEST_ARGUMENT_COUNT"] = str(len(arguments) - 1)
        for index, argument in enumerate(arguments[1:], start=1):
            environment[f"SCOUT_SHA256_TEST_ARGUMENT_{index}"] = argument

        return subprocess.run(
            [
                _SH,
                "-c",
                """. ./eng/sha256-set.sh
case "$SCOUT_SHA256_TEST_ARGUMENT_COUNT" in
    0) "$SCOUT_SHA256_TEST_COMMAND" ;;
    1) "$SCOUT_SHA256_TEST_COMMAND" "$SCOUT_SHA256_TEST_ARGUMENT_1" ;;
    2) "$SCOUT_SHA256_TEST_COMMAND" "$SCOUT_SHA256_TEST_ARGUMENT_1" "$SCOUT_SHA256_TEST_ARGUMENT_2" ;;
    3) "$SCOUT_SHA256_TEST_COMMAND" "$SCOUT_SHA256_TEST_ARGUMENT_1" "$SCOUT_SHA256_TEST_ARGUMENT_2" "$SCOUT_SHA256_TEST_ARGUMENT_3" ;;
    *) exit 99 ;;
esac
""",
                "sha256-set-test",
            ],
            check=False,
            capture_output=True,
            cwd=root,
            env=environment,
            text=True,
        )

    @staticmethod
    def _run_preflight_hash_selection(
        rid: str,
        environment_name: str,
        tool_name: str,
        lock_path: Path | None = None,
    ) -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        source = (root / "eng" / "preflight.sh").read_text(encoding="utf-8")
        start = source.index("strip_toml_value='")
        end = source.index("\nhost_rid() {", start)
        selection_functions = source[start:end]
        harness = f"""#!/bin/sh
set -eu
LOCK="$SCOUT_SHA256_TEST_LOCK"
HOST_RID="$SCOUT_SHA256_TEST_RID"
HOST_ORACLE_ENVIRONMENT="$SCOUT_SHA256_TEST_ENVIRONMENT"
{selection_functions}
. ./eng/sha256-set.sh
sha256_set="$(read_lock_macos_tool_value "$SCOUT_SHA256_TEST_TOOL" sha256)"
decode_sha256_set "$sha256_set"
"""
        process_environment = os.environ.copy()
        process_environment["SCOUT_SHA256_TEST_RID"] = rid
        process_environment["SCOUT_SHA256_TEST_ENVIRONMENT"] = environment_name
        process_environment["SCOUT_SHA256_TEST_TOOL"] = tool_name
        process_environment["SCOUT_SHA256_TEST_LOCK"] = (
            lock_path or root / "tests" / "PREREQS.lock"
        ).resolve().as_posix()
        return subprocess.run(
            [_SH, "-c", harness, "preflight-sha256-selection-test"],
            check=False,
            capture_output=True,
            cwd=root,
            env=process_environment,
            text=True,
        )


if __name__ == "__main__":
    unittest.main()
