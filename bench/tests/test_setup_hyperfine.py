"""Behavioral tests for private, pinned Hyperfine provisioning."""

from __future__ import annotations

import hashlib
import io
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class SetupHyperfineTests(unittest.TestCase):
    """Exercise bottle parsing, verification, and narrow extraction."""

    def test_matching_bottle_installs_only_the_pinned_binary(self) -> None:
        result, install_root, expected_binary = self._run_setup()

        self.assertEqual(0, result.returncode, result.stderr)
        binary = install_root / "bin" / "hyperfine"
        self.assertEqual(self._shell_path(binary), result.stdout.strip())
        self.assertEqual(expected_binary, binary.read_bytes())
        self.assertEqual(
            [binary],
            [path for path in install_root.rglob("*") if path.is_file()],
        )
        self.assertFalse((install_root.parent / "outside-install-root").exists())

    def test_bottle_hash_mismatch_is_rejected_before_extraction(self) -> None:
        result, install_root, _ = self._run_setup(bottle_hash_matches=False)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("macOS hyperfine bottle SHA-256 mismatch:", result.stderr)
        self.assertFalse(install_root.exists())

    def test_binary_hash_mismatch_is_rejected(self) -> None:
        result, install_root, _ = self._run_setup(binary_hash_matches=False)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("macOS hyperfine binary SHA-256 mismatch:", result.stderr)
        self.assertFalse(install_root.exists())

    def test_environment_specific_pin_must_match_the_neutral_pin(self) -> None:
        result, install_root, _ = self._run_setup(specific_version="9.9.9")

        self.assertNotEqual(0, result.returncode)
        self.assertIn(
            "Environment-specific macOS hyperfine version does not match "
            "the environment-neutral pin.",
            result.stderr,
        )
        self.assertFalse(install_root.exists())

    def test_install_root_must_be_absolute_and_fresh(self) -> None:
        root = Path(__file__).resolve().parents[2]
        script = root / "eng" / "setup-hyperfine.sh"

        relative = subprocess.run(
            [_SH, str(script), "relative"],
            check=False,
            capture_output=True,
            text=True,
        )
        missing = subprocess.run(
            [_SH, str(script)],
            check=False,
            capture_output=True,
            text=True,
        )
        with tempfile.TemporaryDirectory() as temporary_directory:
            existing_root = Path(temporary_directory) / "existing"
            existing_root.mkdir()
            existing = subprocess.run(
                [_SH, str(script), self._shell_path(existing_root)],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertNotEqual(0, relative.returncode)
        self.assertIn("INSTALL_ROOT must be an absolute path.", relative.stderr)
        self.assertNotEqual(0, missing.returncode)
        self.assertIn("Usage: eng/setup-hyperfine.sh INSTALL_ROOT", missing.stderr)
        self.assertNotEqual(0, existing.returncode)
        self.assertIn("INSTALL_ROOT must not already exist:", existing.stderr)

    def test_script_has_no_homebrew_or_global_install_dependency(self) -> None:
        root = Path(__file__).resolve().parents[2]
        script = (root / "eng" / "setup-hyperfine.sh").read_text(encoding="utf-8")

        self.assertNotIn("brew", script.lower())
        self.assertNotIn("/opt/homebrew", script)
        self.assertIn('BOTTLE_MEMBER="hyperfine/$VERSION/bin/hyperfine"', script)
        self.assertIn('/usr/bin/tar -xOf "$BOTTLE_ARCHIVE" "$BOTTLE_MEMBER"', script)
        self.assertIn('"$BOTTLE_URL"', script)
        self.assertEqual("#!/bin/sh", script.splitlines()[0])

    def _run_setup(
        self,
        *,
        bottle_hash_matches: bool = True,
        binary_hash_matches: bool = True,
        specific_version: str = "1.20.0",
    ) -> tuple[subprocess.CompletedProcess[str], Path, bytes]:
        root = Path(__file__).resolve().parents[2]
        version = "1.20.0"
        binary = (
            b"#!/bin/sh\n"
            b"if [ \"${1:-}\" = \"--version\" ]; then\n"
            b"    printf 'hyperfine 1.20.0\\n'\n"
            b"fi\n"
        )

        temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(temporary_directory.cleanup)
        temporary_root = Path(temporary_directory.name)
        eng = temporary_root / "eng"
        tests = temporary_root / "tests"
        tools = temporary_root / "tools"
        eng.mkdir()
        tests.mkdir()
        tools.mkdir()

        bottle = tools / "bottle.tar.gz"
        self._write_bottle(bottle, version, binary)
        actual_bottle_sha256 = hashlib.sha256(bottle.read_bytes()).hexdigest()
        bottle_sha256 = (
            actual_bottle_sha256
            if bottle_hash_matches
            else hashlib.sha256(b"different bottle").hexdigest()
        )
        actual_binary_sha256 = hashlib.sha256(binary).hexdigest()
        binary_sha256 = (
            actual_binary_sha256
            if binary_hash_matches
            else hashlib.sha256(b"different binary").hexdigest()
        )
        x64_bottle_sha256 = hashlib.sha256(b"x64 bottle").hexdigest()
        x64_binary_sha256 = hashlib.sha256(b"x64 binary").hexdigest()

        lock = (
            "[[tool.macos]]\n"
            'name = "hyperfine"\n'
            f'version = "{version}"\n'
            "bottle_url = \"https://ghcr.io/v2/homebrew/core/hyperfine/"
            f"blobs/sha256:{bottle_sha256}\"\n"
            f'bottle_sha256 = "{bottle_sha256}"\n'
            f'sha256 = "{binary_sha256}"\n'
            "\n"
            "[[tool.macos]]\n"
            'name = "hyperfine"\n'
            'rid = "osx-arm64"\n'
            'environment = "github-actions"\n'
            f'version = "{specific_version}"\n'
            "bottle_url = \"https://ghcr.io/v2/homebrew/core/hyperfine/"
            f"blobs/sha256:{bottle_sha256}\"\n"
            f'bottle_sha256 = "{bottle_sha256}"\n'
            f'sha256 = "{binary_sha256}"\n'
            "\n"
            "[[tool.macos]]\n"
            'name = "hyperfine"\n'
            'rid = "osx-x64"\n'
            'environment = "github-actions"\n'
            'version = "8.8.8"\n'
            "bottle_url = \"https://ghcr.io/v2/homebrew/core/hyperfine/"
            f"blobs/sha256:{x64_bottle_sha256}\"\n"
            f'bottle_sha256 = "{x64_bottle_sha256}"\n'
            f'sha256 = "{x64_binary_sha256}"\n'
        )
        (tests / "PREREQS.lock").write_text(lock, encoding="utf-8")

        fake_curl = tools / "curl"
        fake_curl.write_text(
            "#!/bin/sh\n"
            "set -eu\n"
            'tool_root="$(CDPATH= cd -- "$(/usr/bin/dirname -- "$0")" && pwd)"\n'
            "output=\n"
            "take_output=0\n"
            'for argument in "$@"; do\n'
            '    if [ "$take_output" -eq 1 ]; then\n'
            '        output="$argument"\n'
            "        take_output=0\n"
            "    elif [ \"$argument\" = \"--output\" ]; then\n"
            "        take_output=1\n"
            "    fi\n"
            "done\n"
            'if [ -n "$output" ]; then\n'
            '    /bin/cp "$tool_root/bottle.tar.gz" "$output"\n'
            "else\n"
            "    printf '{\"token\":\"fixture-token==\"}\\n'\n"
            "fi\n",
            encoding="utf-8",
        )
        fake_curl.chmod(0o755)

        source = (root / "eng" / "setup-hyperfine.sh").read_text(encoding="utf-8")
        source = source.replace("/usr/bin/curl", self._shell_path(fake_curl))
        shasum = subprocess.run(
            [_SH, "-c", "command -v shasum"],
            check=False,
            capture_output=True,
            text=True,
        )
        if shasum.returncode != 0:
            sha256sum = subprocess.run(
                [_SH, "-c", "command -v sha256sum"],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()
            source = source.replace(
                "/usr/bin/shasum -a 256", sha256sum
            )
        source = source.replace("/usr/bin/uname -s", "printf Darwin")
        source = source.replace("/usr/bin/uname -m", "printf arm64")
        setup = eng / "setup-hyperfine.sh"
        setup.write_text(source, encoding="utf-8")
        setup.chmod(0o755)

        install_root = temporary_root / "install"
        result = subprocess.run(
            [_SH, str(setup), self._shell_path(install_root)],
            check=False,
            capture_output=True,
            text=True,
        )
        return result, install_root, binary

    @staticmethod
    def _write_bottle(path: Path, version: str, binary: bytes) -> None:
        with tarfile.open(path, "w:gz") as archive:
            member = tarfile.TarInfo(f"hyperfine/{version}/bin/hyperfine")
            member.mode = 0o755
            member.size = len(binary)
            archive.addfile(member, io.BytesIO(binary))

            outside = b"must not be extracted\n"
            traversal = tarfile.TarInfo("../outside-install-root")
            traversal.size = len(outside)
            archive.addfile(traversal, io.BytesIO(outside))

    @staticmethod
    def _shell_path(path: Path) -> str:
        probe = subprocess.run(
            [_SH, "-c", "command -v cygpath"],
            check=False,
            capture_output=True,
            text=True,
        )
        if probe.returncode != 0:
            return str(path)
        converted = subprocess.run(
            [_SH, "-c", 'cygpath -u "$1"', "sh", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
        return converted


if __name__ == "__main__":
    unittest.main()
