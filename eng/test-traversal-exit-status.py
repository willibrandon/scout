#!/usr/bin/env python3
"""Exercise real traversal failures against the native CLI and release oracle."""

from __future__ import annotations

import itertools
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def normalize_output(output: bytes, *, json_mode: bool) -> list[bytes]:
    if json_mode:
        messages = []
        for line in output.splitlines():
            message = json.loads(line)
            data = message["data"]
            if "elapsed_total" in data:
                data["elapsed_total"] = 0
            if "stats" in data:
                data["stats"]["elapsed"] = 0
            messages.append(json.dumps(message, sort_keys=True).encode())
        return sorted(messages)
    output = re.sub(
        rb"[0-9]+\.[0-9]+ seconds (spent searching|total)",
        rb"<elapsed> seconds \1", output,
    )
    return sorted(output.splitlines())


def drop_root() -> None:
    """Run the child with ordinary permissions when the Linux container is root."""
    os.setgroups([])
    os.setgid(65534)
    os.setuid(65534)


def main() -> None:
    scout, oracle = map(Path, sys.argv[1:3])
    count = 0
    with tempfile.TemporaryDirectory(prefix="scout-traversal-") as temporary:
        root = Path(temporary)
        root.chmod(0o755)
        # Make the executables accessible to an ordinary child in root containers.
        scout_copy = Path(shutil.copy2(scout, root / scout.name))
        oracle_copy = Path(shutil.copy2(oracle, root / oracle.name))
        scout_copy.chmod(0o755)
        oracle_copy.chmod(0o755)
        real_binary = scout.with_name("scout-real")
        if real_binary.is_file() and real_binary != scout:
            Path(shutil.copy2(real_binary, root / real_binary.name)).chmod(0o755)
        fixture = root / "fixture"
        fixture.mkdir(mode=0o755)
        blocked = fixture / "blocked"
        blocked.mkdir(mode=0o755)
        if os.name == "nt":
            broken = blocked / "broken"
            broken.symlink_to(broken)
        else:
            blocked.chmod(0)
        ordinary_child = (
            drop_root if os.name == "posix" and os.geteuid() == 0 else None
        )
        modes = [
            [], ["--files"], ["--json"], ["--stats"], ["--pcre2"],
            ["--pcre2", "--json"], ["--pcre2", "--stats"],
        ]
        environment = os.environ.copy()
        environment.pop("SCOUT_CONFIG_PATH", None)
        environment.pop("RIPGREP_CONFIG_PATH", None)
        try:
            for mode, traversal, messages, quiet, matching in itertools.product(
                modes, ["-j1", "-j4", "--sort=path"],
                [False, True], [False, True], [False, True],
            ):
                if mode == ["--files"] and not matching:
                    continue
                (fixture / "matched.txt").write_text(
                    "needle\n" if matching else "haystack\n", encoding="utf-8"
                )
                arguments = [
                    "--no-config", "--no-ignore", "--follow", "--color=never",
                    "--path-separator=/", traversal, *mode,
                ]
                arguments.append("--messages" if messages else "--no-messages")
                if quiet:
                    arguments.append("--quiet")
                if "--files" not in mode:
                    arguments.append("needle")
                arguments.append(str(fixture))
                results = [
                    subprocess.run(
                        [str(executable), *arguments], env=environment,
                        capture_output=True, check=False, timeout=30,
                        preexec_fn=ordinary_child,
                    )
                    for executable in (scout_copy, oracle_copy)
                ]
                expected = 0 if quiet and matching else 2
                for result in results:
                    if result.returncode != expected:
                        raise AssertionError(
                            f"{result.args}: expected {expected}, "
                            f"got {result.returncode}: {result.stderr!r}"
                        )
                    if not messages and result.stderr:
                        raise AssertionError(
                            f"Suppressed diagnostics: {result.args}: {result.stderr!r}"
                        )
                    if messages and (not quiet or traversal == "--sort=path") and not result.stderr:
                        raise AssertionError(f"Missing traversal diagnostic: {result.args}")
                actual, reference = results
                json_mode = "--json" in mode
                if normalize_output(actual.stdout, json_mode=json_mode) != normalize_output(reference.stdout, json_mode=json_mode):
                    raise AssertionError(
                        f"Output mismatch: {arguments}\n"
                        f"Scout: {actual.stdout!r}\nrg: {reference.stdout!r}"
                    )
                count += 1
        finally:
            blocked.chmod(0o755)
    print(f"OK {count} native traversal exit-status differentials")


if __name__ == "__main__":
    main()
