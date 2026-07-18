"""Behavioral tests for pinned corpus archive verification."""

from __future__ import annotations

import gzip
import hashlib
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class CorpusArchiveVerificationTests(unittest.TestCase):
    """Exercise corpus verification without downloading the release corpora."""

    def test_matching_archive_passes(self) -> None:
        archive = b"pinned corpus archive\n"
        expected_sha256 = hashlib.sha256(archive).hexdigest()

        result = self._run_verifier(archive, expected_sha256)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Verified corpus archive: test-corpus", result.stderr)

    def test_mismatching_archive_is_rejected(self) -> None:
        archive = b"different corpus archive\n"
        expected_sha256 = hashlib.sha256(b"pinned corpus archive\n").hexdigest()

        result = self._run_verifier(archive, expected_sha256)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Corpus archive SHA-256 mismatch:", result.stderr)
        self.assertIn(f"expected: {expected_sha256}", result.stderr)
        self.assertIn(f"actual:   {hashlib.sha256(archive).hexdigest()}", result.stderr)

    def test_fetch_rejects_bad_archive_before_decompression(self) -> None:
        root = Path(__file__).resolve().parents[2]
        pinned_sha256 = hashlib.sha256(b"the pinned archive").hexdigest()

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            temporary_eng = temporary_root / "eng"
            temporary_tests = temporary_root / "tests"
            output = temporary_root / "corpora"
            archive_path = output / "opensubtitles" / "en.txt.gz"
            temporary_eng.mkdir()
            temporary_tests.mkdir()
            archive_path.parent.mkdir(parents=True)
            shutil.copy2(root / "eng" / "fetch-corpora.sh", temporary_eng)
            shutil.copy2(root / "eng" / "verify-corpus-archive.sh", temporary_eng)
            self._write_lock(
                temporary_tests / "PREREQS.lock", "opensubtitles-en", pinned_sha256
            )
            archive_path.write_bytes(b"not even a gzip stream")

            result = subprocess.run(
                [
                    _SH,
                    str(temporary_eng / "fetch-corpora.sh"),
                    "--opensubtitles",
                    "--output-dir",
                    str(output),
                    "--verify-lock",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            extracted_path = output / "opensubtitles" / "en.txt"
            extracted_exists = extracted_path.exists()

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Corpus archive SHA-256 mismatch:", result.stderr)
        self.assertNotIn("not in gzip format", result.stderr)
        self.assertFalse(extracted_exists)

    def test_fetch_accepts_pinned_archive_and_decompresses(self) -> None:
        root = Path(__file__).resolve().parents[2]
        corpus = b"the release corpus\n"
        archive = gzip.compress(corpus)
        pinned_sha256 = hashlib.sha256(archive).hexdigest()

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            temporary_eng = temporary_root / "eng"
            temporary_tests = temporary_root / "tests"
            output = temporary_root / "corpora"
            archive_path = output / "opensubtitles" / "en.txt.gz"
            temporary_eng.mkdir()
            temporary_tests.mkdir()
            archive_path.parent.mkdir(parents=True)
            shutil.copy2(root / "eng" / "fetch-corpora.sh", temporary_eng)
            shutil.copy2(root / "eng" / "verify-corpus-archive.sh", temporary_eng)
            self._write_lock(
                temporary_tests / "PREREQS.lock", "opensubtitles-en", pinned_sha256
            )
            archive_path.write_bytes(archive)

            result = subprocess.run(
                [
                    _SH,
                    str(temporary_eng / "fetch-corpora.sh"),
                    "--opensubtitles",
                    "--output-dir",
                    str(output),
                    "--verify-lock",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            extracted = (output / "opensubtitles" / "en.txt").read_bytes()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Verified corpus archive: opensubtitles-en", result.stderr)
        self.assertEqual(corpus, extracted)

    def test_release_gate_verifies_root_and_worktree_archives(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )

        self.assertIn(
            '"$ROOT/eng/fetch-corpora.sh" --all --verify-lock', driver
        )
        self.assertIn(
            '"$PERFORMANCE_WORKTREE/eng/fetch-corpora.sh" --all --verify-lock',
            driver,
        )

    def _run_verifier(
        self, archive: bytes, expected_sha256: str
    ) -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        verifier = root / "eng" / "verify-corpus-archive.sh"

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            lock = temporary_root / "PREREQS.lock"
            archive_path = temporary_root / "corpus.tar.gz"
            self._write_lock(lock, "test-corpus", expected_sha256)
            archive_path.write_bytes(archive)
            return subprocess.run(
                [_SH, str(verifier), str(lock), "test-corpus", str(archive_path)],
                check=False,
                capture_output=True,
                text=True,
            )

    @staticmethod
    def _write_lock(lock: Path, name: str, archive_sha256: str) -> None:
        lock.write_text(
            "[[corpus]]\n"
            f'name = "{name}"\n'
            f'archive_sha256 = "{archive_sha256}"\n',
            encoding="utf-8",
        )
