"""Tests for untimed Hyperfine workload output verification."""

from __future__ import annotations

import io
import json
import os
import shlex
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmark_environment import create_benchmark_environment
from verify_hyperfine_output import (
    _SHELL,
    _SORT,
    _windows_posix_tool,
    main,
    run_and_digest,
    verify_outputs,
)


class VerifyHyperfineOutputTests(unittest.TestCase):
    """Verify normalized direct-output comparison and failure handling."""

    def test_equal_line_multisets_match_across_ordering(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            output = directory / "verification.json"
            matches = verify_outputs(
                "sample",
                self._python_command("print('beta'); print('alpha')"),
                self._python_command("print('alpha'); print('beta')"),
                output,
                performance_input_manifest=self._manifest(directory),
                reproducibility_manifest=self._repro_manifest(directory),
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertTrue(matches)
        self.assertTrue(document["matches"])
        self.assertEqual(
            document["rg"]["normalized_sha256"],
            document["scout"]["normalized_sha256"],
        )
        self.assertEqual(2, document["rg"]["lines"])
        self.assertEqual("direct", document["execution_mode"])
        self.assertEqual(
            "fixture",
            document["reproducibility"]["values"]["host.os"],
        )

    def test_different_output_is_recorded_as_a_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            output = directory / "verification.json"
            matches = verify_outputs(
                "sample",
                self._python_command("print('alpha')"),
                self._python_command("print('beta')"),
                output,
                performance_input_manifest=self._manifest(directory),
                reproducibility_manifest=self._repro_manifest(directory),
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertFalse(matches)
        self.assertFalse(document["matches"])
        self.assertEqual("equivalent", document["output_policy"])

    def test_independent_output_policy_records_intentional_difference(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            output = directory / "verification.json"
            accepted = verify_outputs(
                "cold-version",
                self._python_command("print('rg version')"),
                self._python_command("print('scout version')"),
                output,
                performance_input_manifest=self._manifest(directory),
                reproducibility_manifest=self._repro_manifest(directory),
                output_policy="independent",
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertTrue(accepted)
        self.assertFalse(document["matches"])
        self.assertEqual("independent", document["output_policy"])

    def test_unexpected_nonzero_command_fails_verification(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            with self.assertRaisesRegex(
                RuntimeError, "expected exit 0, got 7"
            ):
                run_and_digest(
                    self._python_command("import sys; sys.exit(7)"),
                    directory,
                )

    def test_declared_no_match_exit_is_accepted_exactly(self) -> None:
        command = self._python_command("import sys; sys.exit(1)")
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            output = directory / "verification.json"
            self.assertTrue(
                verify_outputs(
                    "sample",
                    command,
                    command,
                    output,
                    expected_exit_code=1,
                    performance_input_manifest=self._manifest(directory),
                    reproducibility_manifest=self._repro_manifest(directory),
                )
            )

            with self.assertRaisesRegex(
                RuntimeError, "expected exit 0, got 1"
            ):
                run_and_digest(command, directory)

    def test_direct_execution_uses_explicit_cwd_and_environment(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            (directory / "marker.txt").write_text("alpha\n", encoding="utf-8")
            output = directory / "verification.json"
            command = self._python_command(
                "import os,pathlib; "
                "assert os.environ['SCOUT_REGEX_SPECIALIZATION_MODE']=='general'; "
                "print(pathlib.Path('marker.txt').read_text().strip())"
            )
            matches = verify_outputs(
                "sample",
                command,
                command,
                output,
                working_directory=directory,
                environment_assignments=(
                    "SCOUT_REGEX_SPECIALIZATION_MODE=general",
                ),
                performance_input_manifest=self._manifest(directory),
                reproducibility_manifest=self._repro_manifest(directory),
            )
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertTrue(matches)
        self.assertEqual(str(directory.resolve()), document["working_directory"])
        self.assertEqual(
            "general",
            document["environment"]["SCOUT_REGEX_SPECIALIZATION_MODE"],
        )
        self.assertEqual(shlex.split(command), document["command_argv"]["rg"])

    @unittest.skipUnless(os.name == "nt", "requires Windows")
    def test_windows_normalization_uses_the_posix_sort_next_to_the_shell(self) -> None:
        self.assertIsNotNone(_SHELL)
        self.assertIsNotNone(_SORT)
        self.assertEqual(Path(_SHELL).parent, Path(_SORT).parent)
        self.assertEqual("sort", Path(_SORT).stem.lower())

    def test_windows_posix_tool_does_not_fall_back_to_native_windows_sort(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            shell = directory / "sh.exe"
            sort = directory / "sort.exe"
            shell.touch()

            self.assertIsNone(_windows_posix_tool(str(shell), "sort"))
            sort.touch()
            self.assertEqual(str(sort), _windows_posix_tool(str(shell), "sort"))

    def test_malformed_direct_command_has_a_concise_cli_failure(self) -> None:
        standard_error = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            with redirect_stderr(standard_error):
                exit_code = main(
                    [
                        "--workload",
                        "sample",
                        "--rg-command",
                        "'unterminated",
                        "--scout-command",
                        "unused",
                        "--output",
                        str(directory / "result.json"),
                        "--working-directory",
                        str(directory),
                        "--performance-input-manifest",
                        str(self._manifest(directory)),
                        "--reproducibility-manifest",
                        str(self._repro_manifest(directory)),
                        "--expected-exit-code",
                        "0",
                        "--output-policy",
                        "equivalent",
                    ]
                )

        self.assertEqual(2, exit_code)
        self.assertIn(
            "Output verification failed: No closing quotation",
            standard_error.getvalue(),
        )

    def test_command_and_sort_receive_the_same_allowlisted_environment(self) -> None:
        injected = {
            "HOME": "injected-home",
            "XDG_CONFIG_HOME": "injected-xdg",
            "DOTNET_PROCESSOR_COUNT": "99",
            "COMPlus_GCHeapCount": "99",
            "MallocNanoZone": "1",
            "GIT_CONFIG_GLOBAL": "injected-git-config",
            "GITHUB_ACTIONS": "true",
        }
        forbidden = tuple(
            name for name in injected if name not in ("HOME", "XDG_CONFIG_HOME")
        )

        with tempfile.TemporaryDirectory() as temporary_directory, patch.dict(
            os.environ, injected
        ):
            directory = Path(temporary_directory)
            environment = create_benchmark_environment(
                directory / "result.output.json"
            )
            expected = {
                "HOME": environment["HOME"],
                "XDG_CONFIG_HOME": environment["XDG_CONFIG_HOME"],
            }
            source = (
                "import os,sys; "
                f"forbidden={forbidden!r}; expected={expected!r}; "
                "invalid=any(name in os.environ for name in forbidden) or "
                "any(os.environ.get(name) != value "
                "for name,value in expected.items()); "
                "sys.exit(1) if invalid else print('isolated')"
            )
            command = self._python_command(source)
            with patch(
                "verify_hyperfine_output.subprocess.run",
                wraps=subprocess.run,
            ) as run, patch(
                "verify_hyperfine_output.subprocess.Popen",
                wraps=subprocess.Popen,
            ) as popen:
                digest = run_and_digest(
                    command,
                    directory,
                    environment=environment,
                )

        self.assertEqual(1, digest["lines"])
        self.assertIs(environment, run.call_args.kwargs["env"])
        self.assertEqual(
            Path(__file__).resolve().parents[2], run.call_args.kwargs["cwd"]
        )
        self.assertGreaterEqual(popen.call_count, 2)
        for call in popen.call_args_list:
            self.assertIs(environment, call.kwargs["env"])

    def test_output_pair_uses_one_shared_environment_helper_result(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            output = directory / "result.output.json"
            environment = {"HOME": "isolated"}
            digest = {
                "normalized_sha256": "digest",
                "bytes": 0,
                "lines": 0,
            }
            with patch(
                "verify_hyperfine_output.create_benchmark_environment",
                return_value=environment,
            ) as create_environment, patch(
                "verify_hyperfine_output.run_and_digest",
                return_value=digest,
            ) as run:
                self.assertTrue(
                    verify_outputs(
                        "sample",
                        "rg",
                        "scout",
                        output,
                        performance_input_manifest=self._manifest(directory),
                        reproducibility_manifest=self._repro_manifest(directory),
                    )
                )

        create_environment.assert_called_once_with(output)
        self.assertEqual(2, run.call_count)
        pair_environment = run.call_args_list[0].kwargs["environment"]
        self.assertEqual(environment, pair_environment)
        for call in run.call_args_list:
            self.assertIs(pair_environment, call.kwargs["environment"])

    @staticmethod
    def _python_command(source: str) -> str:
        return shlex.join([sys.executable, "-c", source])

    @staticmethod
    def _manifest(directory: Path) -> Path:
        manifest = directory / "performance-inputs.json"
        manifest.write_text(
            '{"inputs":[{"name":"fixture","bytes":1,"sha256":"x"}]}\n',
            encoding="utf-8",
        )
        return manifest

    @staticmethod
    def _repro_manifest(directory: Path) -> Path:
        manifest = directory / "reproducibility.json"
        manifest.write_text(
            '{"schema_version":1,"values":{"host.os":"fixture"}}\n',
            encoding="utf-8",
        )
        return manifest


if __name__ == "__main__":
    unittest.main()
