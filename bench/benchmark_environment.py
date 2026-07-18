"""Create an isolated, reproducible environment for benchmark children."""

from __future__ import annotations

import ntpath
import os
import shutil
from collections.abc import Mapping, Sequence
from pathlib import Path


_POSIX_PATH = "/usr/bin:/bin:/usr/sbin:/sbin"


def create_benchmark_environment(output_path: Path) -> dict[str, str]:
    """Return an allowlisted environment with fresh state for one output."""
    output_name = output_path.stem
    if output_name.endswith(".output"):
        output_name = output_name.removesuffix(".output")
    root = output_path.parent / f".{output_name}.environment"
    _recreate_directory(root)

    home = root / "home"
    temporary = root / "tmp"
    xdg = root / "xdg"
    xdg_config = xdg / "config"
    xdg_cache = xdg / "cache"
    xdg_data = xdg / "data"
    xdg_state = xdg / "state"
    for directory in (
        home,
        temporary,
        xdg_config,
        xdg_cache,
        xdg_data,
        xdg_state,
    ):
        directory.mkdir(parents=True)

    environment = {
        "HOME": str(home.resolve()),
        "TMPDIR": str(temporary.resolve()),
        "XDG_CACHE_HOME": str(xdg_cache.resolve()),
        "XDG_CONFIG_HOME": str(xdg_config.resolve()),
        "XDG_DATA_HOME": str(xdg_data.resolve()),
        "XDG_STATE_HOME": str(xdg_state.resolve()),
        "LANG": "C",
        "LC_ALL": "C",
        "TZ": "UTC",
    }
    if os.name == "nt":
        _add_windows_environment(environment, home, temporary, os.environ)
    else:
        _add_posix_environment(environment)

    return environment


def add_benchmark_environment_assignments(
    environment: Mapping[str, str], assignments: Sequence[str]
) -> dict[str, str]:
    """Return the benchmark environment with validated explicit assignments."""
    result = dict(environment)
    assigned_names: set[str] = set()
    for assignment in assignments:
        name, separator, value = assignment.partition("=")
        if (
            not separator
            or not name
            or not (name[0].isalpha() or name[0] == "_")
            or any(
                not (character.isalnum() or character == "_")
                for character in name
            )
        ):
            raise ValueError(
                f"invalid benchmark environment assignment: {assignment!r}"
            )
        if name in assigned_names:
            raise ValueError(f"duplicate benchmark environment variable: {name}")
        assigned_names.add(name)
        result[name] = value

    return result


def _recreate_directory(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)


def _add_posix_environment(environment: dict[str, str]) -> None:
    environment["PATH"] = _POSIX_PATH


def _add_windows_environment(
    environment: dict[str, str],
    home: Path,
    temporary: Path,
    host_environment: Mapping[str, str],
) -> None:
    system_root = host_environment.get("SystemRoot") or host_environment.get(
        "WINDIR"
    )
    if not system_root:
        raise RuntimeError("Windows benchmark children require SystemRoot")

    system_drive = ntpath.splitdrive(system_root)[0]
    if not system_drive:
        raise RuntimeError("Windows SystemRoot must include a drive")

    system_directory = ntpath.join(system_root, "System32")
    environment["SystemRoot"] = system_root
    environment["SystemDrive"] = system_drive
    environment["WINDIR"] = system_root
    environment["PATH"] = ";".join((system_directory, system_root))
    environment["ComSpec"] = ntpath.join(system_directory, "cmd.exe")

    environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD"
    environment["TEMP"] = str(temporary.resolve())
    environment["TMP"] = str(temporary.resolve())
    environment["USERPROFILE"] = str(home.resolve())
