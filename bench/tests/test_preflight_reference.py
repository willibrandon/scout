"""Execute preflight's source checks against real artifact Git checkouts."""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_ROOT = Path(__file__).resolve().parents[2]
_PREFLIGHT = (_ROOT / "eng/preflight.sh").read_text(encoding="utf-8")
# Run the actual functions and source-check branches without requiring unrelated
# SDKs, compilers, corpora, or platform tools in these Git integration fixtures.
_FUNCTIONS = _PREFLIGHT[: _PREFLIGHT.index("\nEXPECTED_SDK=")]
_SOURCE_CHECK = _PREFLIGHT[
    _PREFLIGHT.index("\nif ARCHIVE_PATH_VALUE=") :
    _PREFLIGHT.index('\ncheck_file_hash "reference rg"')
]
_CARGO_CHECK = _PREFLIGHT[
    _PREFLIGHT.index('\nif [ "$HAS_RIPGREP_SOURCE_CHECKOUT" -eq 1 ]; then') :
    _PREFLIGHT.index("\nprintf 'Scout preflight passed.")
]


class PreflightReferenceTests(unittest.TestCase):
    """Verify release objects and Cargo.lock without an external checkout."""

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="scout-preflight-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "eng").mkdir()
        (self.root / "tests").mkdir()
        (self.root / "upstream").mkdir()
        for helper in ("sha256-set.sh", "read-ripgrep-oracle.sh"):
            shutil.copyfile(_ROOT / "eng" / helper, self.root / "eng" / helper)
        (self.root / "native").mkdir()
        shutil.copyfile(
            _ROOT / "native/toolchain-unix.sh",
            self.root / "native/toolchain-unix.sh",
        )
        self.checkout = self.root / "artifacts" / "reference with spaces"
        self.checkout.mkdir(parents=True)
        self.git("init", "--quiet")
        self.git("config", "user.name", "Preflight Test")
        self.git("config", "user.email", "preflight@example.invalid")
        release_lock = "release Cargo.lock\n"
        (self.checkout / "Cargo.lock").write_text(release_lock, encoding="utf-8")
        (self.root / "upstream/Cargo.lock").write_text(release_lock, encoding="utf-8")
        self.git("add", "Cargo.lock")
        self.git("commit", "--quiet", "-m", "release")
        self.release = self.git("rev-parse", "HEAD").strip()
        (self.checkout / "Cargo.lock").write_text("later lock\n", encoding="utf-8")
        self.git("commit", "--quiet", "-am", "later source")
        self.head = self.git("rev-parse", "HEAD").strip()
        (self.checkout / "Cargo.lock").write_text("local edits\n", encoding="utf-8")
        (self.checkout / "untracked").write_text("preserve\n", encoding="utf-8")
        self.status = self.git("status", "--porcelain")

    def git(self, *arguments: str) -> str:
        return subprocess.run(
            ["git", "-C", str(self.checkout), *arguments],
            check=True, capture_output=True, text=True,
        ).stdout

    def run_checks(self, *, executable: str = "rg", archive: bool = False) -> subprocess.CompletedProcess[str]:
        oracle_path = self.checkout / "target/release-lto" / executable
        oracle_path.parent.mkdir(parents=True, exist_ok=True)
        oracle_relative_path = oracle_path.relative_to(self.root).as_posix()
        lock = f'ripgrep_commit = "{self.release}"\nripgrep_rg_path = "{oracle_relative_path}"\n'
        if archive:
            archive_path = self.root / "reference.zip"
            archive_path.write_bytes(b"reference archive fixture")
            digest = hashlib.sha256(archive_path.read_bytes()).hexdigest()
            lock += 'ripgrep_oracle_archive_path = "reference.zip"\n'
            lock += f'ripgrep_oracle_archive_sha256 = "{digest}"\n'
        (self.root / "tests/PREREQS.lock").write_bytes(lock.encode("utf-8"))
        environment = os.environ.copy()
        environment["SCOUT_RIPGREP_REFERENCE"] = (self.root / "absent-external-checkout").as_posix()
        script = _FUNCTIONS + """
HOST_RID=osx-arm64
HOST_ORACLE_ENVIRONMENT=local
EXPECTED_RIPGREP="$(read_lock_value ripgrep_commit)"
RG_PATH="$(resolve_repo_path "$(read_oracle_value path ripgrep_rg_path)")"
""" + _SOURCE_CHECK + _CARGO_CHECK
        script_path = self.root / "eng/preflight.sh"
        script_path.write_bytes(script.encode("utf-8"))
        return subprocess.run(
            ["sh", script_path.as_posix()],
            env=environment, capture_output=True, text=True, check=False,
        )

    def test_artifact_checkout_supplies_release_and_lock_without_changing_head(self) -> None:
        for executable in ("rg", "rg.exe"):
            with self.subTest(executable=executable):
                result = self.run_checks(executable=executable)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(self.head, self.git("rev-parse", "HEAD").strip())
                self.assertEqual(self.status, self.git("status", "--porcelain"))

    def test_source_check_rejects_a_missing_release_object(self) -> None:
        self.release = "0" * 40
        result = self.run_checks()
        self.assertNotEqual(0, result.returncode)

    def test_source_check_rejects_a_different_release_lock(self) -> None:
        (self.root / "upstream/Cargo.lock").write_text("wrong lock\n", encoding="utf-8")
        result = self.run_checks()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Cargo.lock", result.stderr)

    def test_archive_verification_does_not_require_a_source_checkout(self) -> None:
        self.checkout = self.root / "artifacts/archive-only-reference"
        result = self.run_checks(archive=True)
        self.assertEqual(0, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()
