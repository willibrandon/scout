"""Tests for the isolated benchmark subprocess environment."""

from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmark_environment import (
    _add_posix_environment,
    _add_windows_environment,
    create_benchmark_environment,
)


class BenchmarkEnvironmentTests(unittest.TestCase):
    """Verify benchmark children receive only deterministic state."""

    def test_environment_is_allowlisted_and_recreated_for_each_output(self) -> None:
        injected = {
            "HOME": "injected-home",
            "XDG_CONFIG_HOME": "injected-xdg",
            "DOTNET_PROCESSOR_COUNT": "99",
            "COMPlus_GCHeapCount": "99",
            "MallocNanoZone": "1",
            "GIT_CONFIG_GLOBAL": "injected-git-config",
            "GITHUB_ACTIONS": "true",
            "SCOUT_HYPERFINE_BIN": "/injected/hyperfine",
        }
        with tempfile.TemporaryDirectory() as temporary_directory, patch.dict(
            os.environ, injected
        ):
            output = Path(temporary_directory) / "result.json"
            environment = create_benchmark_environment(output)
            Path(environment["HOME"], "stale").write_text(
                "stale", encoding="utf-8"
            )
            recreated = create_benchmark_environment(output)
            verification = create_benchmark_environment(
                Path(temporary_directory) / "result.output.json"
            )

            self.assertNotEqual(injected["HOME"], recreated["HOME"])
            self.assertNotEqual(
                injected["XDG_CONFIG_HOME"], recreated["XDG_CONFIG_HOME"]
            )
            self.assertTrue(
                {
                    "DOTNET_PROCESSOR_COUNT",
                    "COMPlus_GCHeapCount",
                    "MallocNanoZone",
                    "GIT_CONFIG_GLOBAL",
                    "GITHUB_ACTIONS",
                    "SCOUT_HYPERFINE_BIN",
                }.isdisjoint(recreated)
            )
            self.assertEqual("C", recreated["LANG"])
            self.assertEqual("C", recreated["LC_ALL"])
            self.assertEqual("UTC", recreated["TZ"])
            self.assertFalse(Path(recreated["HOME"], "stale").exists())
            for name in (
                "HOME",
                "TMPDIR",
                "XDG_CACHE_HOME",
                "XDG_CONFIG_HOME",
                "XDG_DATA_HOME",
                "XDG_STATE_HOME",
            ):
                self.assertTrue(Path(recreated[name]).is_dir())
                self.assertEqual(recreated[name], verification[name])

            if os.name == "posix":
                self.assertEqual(
                    "/usr/bin:/bin:/usr/sbin:/sbin", recreated["PATH"]
                )
                self.assertEqual(
                    {
                        "HOME",
                        "LANG",
                        "LC_ALL",
                        "PATH",
                        "TMPDIR",
                        "TZ",
                        "XDG_CACHE_HOME",
                        "XDG_CONFIG_HOME",
                        "XDG_DATA_HOME",
                        "XDG_STATE_HOME",
                    },
                    set(recreated),
                )
            else:
                self.assertEqual(recreated["TMPDIR"], recreated["TEMP"])
                self.assertEqual(recreated["TMPDIR"], recreated["TMP"])
                self.assertEqual(recreated["HOME"], recreated["USERPROFILE"])
                self.assertEqual(
                    {
                        "ComSpec",
                        "HOME",
                        "LANG",
                        "LC_ALL",
                        "PATH",
                        "PATHEXT",
                        "SystemDrive",
                        "SystemRoot",
                        "TEMP",
                        "TMP",
                        "TMPDIR",
                        "TZ",
                        "USERPROFILE",
                        "WINDIR",
                        "XDG_CACHE_HOME",
                        "XDG_CONFIG_HOME",
                        "XDG_DATA_HOME",
                        "XDG_STATE_HOME",
                    },
                    set(recreated),
                )

    def test_windows_essentials_are_derived_only_from_system_root(self) -> None:
        environment: dict[str, str] = {}
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            _add_windows_environment(
                environment,
                root / "home",
                root / "tmp",
                {
                    "SystemRoot": r"D:\Windows",
                    "ComSpec": "poison-comspec",
                    "PATH": "poison-path",
                    "SystemDrive": "Z:",
                    "WINDIR": "poison-windir",
                },
            )

        self.assertEqual(r"D:\Windows", environment["SystemRoot"])
        self.assertEqual(r"D:\Windows", environment["WINDIR"])
        self.assertEqual("D:", environment["SystemDrive"])
        self.assertEqual(
            r"D:\Windows\System32;D:\Windows", environment["PATH"]
        )
        self.assertEqual(
            r"D:\Windows\System32\cmd.exe", environment["ComSpec"]
        )

    def test_posix_path_uses_only_operating_system_tools(self) -> None:
        environment = {"PATH": "poison-path"}

        _add_posix_environment(environment)

        self.assertEqual("/usr/bin:/bin:/usr/sbin:/sbin", environment["PATH"])

    def test_windows_environment_rejects_a_missing_or_relative_system_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for host_environment in ({}, {"SystemRoot": r"Windows"}):
                with self.subTest(host_environment=host_environment):
                    with self.assertRaises(RuntimeError):
                        _add_windows_environment(
                            {},
                            root / "home",
                            root / "tmp",
                            host_environment,
                        )


if __name__ == "__main__":
    unittest.main()
