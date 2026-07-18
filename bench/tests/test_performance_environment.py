"""Focused tests for the authoritative performance-gate environment."""

from __future__ import annotations

import os
import shutil
import subprocess
import unittest
from pathlib import Path


_SH = shutil.which("sh")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class PerformanceEnvironmentTests(unittest.TestCase):
    """Verify clean re-exec metadata and canonical build settings."""

    def test_clean_reexec_preserves_only_validated_ci_metadata(self) -> None:
        environment = dict(os.environ)
        environment.update(
            {
                "RUNNER_NAME": "GitHub Actions 42",
                "ImageOS": "macos26",
                "ImageVersion": "20260715.1",
                "RUNNER_TRACKING_ID": "discard-runner-state",
                "GITHUB_ACTIONS": "true",
                "DOTNET_PROCESSOR_COUNT": "99",
                "UNRELATED_POISON": "discard-unrelated-state",
            }
        )

        result = self._clean_reexec_environment(environment)

        self.assertEqual(0, result.returncode, result.stderr)
        clean = self._parse_environment(result.stdout)
        self.assertEqual(
            "GitHub Actions 42", clean["SCOUT_PERFORMANCE_GATE_RUNNER_NAME"]
        )
        self.assertEqual("macos26", clean["SCOUT_PERFORMANCE_GATE_IMAGE_OS"])
        self.assertEqual(
            "20260715.1", clean["SCOUT_PERFORMANCE_GATE_IMAGE_VERSION"]
        )
        self.assertNotIn("RUNNER_NAME", clean)
        self.assertNotIn("ImageOS", clean)
        self.assertNotIn("ImageVersion", clean)
        self.assertNotIn("RUNNER_TRACKING_ID", clean)
        self.assertNotIn("GITHUB_ACTIONS", clean)
        self.assertNotIn("DOTNET_PROCESSOR_COUNT", clean)
        self.assertNotIn("UNRELATED_POISON", clean)

    def test_clean_reexec_uses_explicit_local_metadata_defaults(self) -> None:
        environment = dict(os.environ)
        for variable in ("RUNNER_NAME", "ImageOS", "ImageVersion"):
            environment.pop(variable, None)

        result = self._clean_reexec_environment(environment)

        self.assertEqual(0, result.returncode, result.stderr)
        clean = self._parse_environment(result.stdout)
        self.assertEqual("local", clean["SCOUT_PERFORMANCE_GATE_RUNNER_NAME"])
        self.assertEqual("local", clean["SCOUT_PERFORMANCE_GATE_IMAGE_OS"])
        self.assertEqual("local", clean["SCOUT_PERFORMANCE_GATE_IMAGE_VERSION"])

    def test_clean_reexec_rejects_multiline_metadata_before_exec(self) -> None:
        for variable in ("RUNNER_NAME", "ImageOS", "ImageVersion"):
            for line_break in ("\n", "\r"):
                with self.subTest(variable=variable, line_break=repr(line_break)):
                    environment = dict(os.environ)
                    environment[variable] = (
                        "trusted" + line_break + "injected=value"
                    )

                    result = self._clean_reexec_environment(environment)

                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(variable, result.stderr)
                    self.assertIn("must be a single line", result.stderr)
                    self.assertEqual("", result.stdout)

    def test_sanitize_overwrites_poisoned_dotnet_and_nuget_settings(self) -> None:
        environment = dict(os.environ)
        for variable in (
            "SCOUT_PERFORMANCE_GATE_RUNNER_NAME",
            "SCOUT_PERFORMANCE_GATE_IMAGE_OS",
            "SCOUT_PERFORMANCE_GATE_IMAGE_VERSION",
        ):
            environment.pop(variable, None)
        environment.update(
            {
                "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "0",
                "DOTNET_MULTILEVEL_LOOKUP": "1",
                "DOTNET_PROCESSOR_COUNT": "99",
                "NuGetAudit": "true",
                "NUGET_AUDIT_MODE": "all",
                "RUNNER_NAME": "discard-original-runner",
                "ImageOS": "discard-original-image",
                "ImageVersion": "discard-original-version",
            }
        )

        result = self._sanitize_environment(environment)

        self.assertEqual(0, result.returncode, result.stderr)
        sanitized = self._parse_environment(result.stdout)
        self.assertEqual(
            "1", sanitized["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"]
        )
        self.assertEqual("0", sanitized["DOTNET_MULTILEVEL_LOOKUP"])
        self.assertEqual("false", sanitized["NuGetAudit"])
        self.assertNotIn("DOTNET_PROCESSOR_COUNT", sanitized)
        self.assertNotIn("NUGET_AUDIT_MODE", sanitized)
        self.assertNotIn("RUNNER_NAME", sanitized)
        self.assertNotIn("ImageOS", sanitized)
        self.assertNotIn("ImageVersion", sanitized)
        self.assertEqual("local", sanitized["SCOUT_PERFORMANCE_GATE_RUNNER_NAME"])
        self.assertEqual("local", sanitized["SCOUT_PERFORMANCE_GATE_IMAGE_OS"])
        self.assertEqual("local", sanitized["SCOUT_PERFORMANCE_GATE_IMAGE_VERSION"])

    def test_sanitize_retains_validated_internal_metadata(self) -> None:
        environment = dict(os.environ)
        environment.update(
            {
                "SCOUT_PERFORMANCE_GATE_RUNNER_NAME": "GitHub Actions 42",
                "SCOUT_PERFORMANCE_GATE_IMAGE_OS": "macos26",
                "SCOUT_PERFORMANCE_GATE_IMAGE_VERSION": "20260715.1",
            }
        )

        result = self._sanitize_environment(environment)

        self.assertEqual(0, result.returncode, result.stderr)
        sanitized = self._parse_environment(result.stdout)
        self.assertEqual(
            "GitHub Actions 42", sanitized["SCOUT_PERFORMANCE_GATE_RUNNER_NAME"]
        )
        self.assertEqual("macos26", sanitized["SCOUT_PERFORMANCE_GATE_IMAGE_OS"])
        self.assertEqual(
            "20260715.1", sanitized["SCOUT_PERFORMANCE_GATE_IMAGE_VERSION"]
        )

    @staticmethod
    def _helper() -> Path:
        return (
            Path(__file__).resolve().parents[2]
            / "eng"
            / "performance-environment.sh"
        )

    def _clean_reexec_environment(
        self, environment: dict[str, str]
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                _SH,
                "-c",
                '. "$1"; exec_clean_performance_gate "github-actions" "$2" -c env',
                "sh",
                str(self._helper()),
                _SH,
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )

    def _sanitize_environment(
        self, environment: dict[str, str]
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                _SH,
                "-c",
                '. "$1"; sanitize_performance_environment "/isolated/dotnet"; env',
                "sh",
                str(self._helper()),
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )

    @staticmethod
    def _parse_environment(output: str) -> dict[str, str]:
        return {
            line.split("=", 1)[0]: line.split("=", 1)[1]
            for line in output.splitlines()
            if "=" in line
        }


if __name__ == "__main__":
    unittest.main()
