"""Exercise validation archive extraction with actual safe and malicious archives."""

from __future__ import annotations

import importlib.util
import io
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile


SPEC = importlib.util.spec_from_file_location(
    "setup_dotnet_validation", Path(__file__).resolve().parents[2] / "eng" / "setup-dotnet-validation.py",
)
assert SPEC is not None and SPEC.loader is not None
SETUP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SETUP)


class SetupDotnetValidationTests(unittest.TestCase):
    def test_regular_tar_files_retain_contents_and_executable_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "sdk.tar.gz"
            member = tarfile.TarInfo("sdk/dotnet")
            member.mode = 0o755
            member.size = 4
            with tarfile.open(archive, "w:gz") as source:
                source.addfile(member, io.BytesIO(b"tool"))
            destination = root / "install"
            if not callable(getattr(tarfile, "data_filter", None)):
                with self.assertRaisesRegex(RuntimeError, "Safe tar extraction is unavailable"):
                    SETUP.extract_archive(archive, destination)
                self.assertFalse(destination.exists())
            else:
                SETUP.extract_archive(archive, destination)
                self.assertEqual(b"tool", (destination / "sdk/dotnet").read_bytes())
            if os.name != "nt" and callable(getattr(tarfile, "data_filter", None)):
                self.assertEqual(0o755, (destination / "sdk/dotnet").stat().st_mode & 0o777)

    def test_tar_paths_and_links_cannot_escape_the_destination(self) -> None:
        for kind in ("parent_path", "symbolic_link", "hard_link"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                archive = root / "sdk.tar"
                member = tarfile.TarInfo("../outside" if kind == "parent_path" else "link")
                if kind != "parent_path":
                    member.type = tarfile.SYMTYPE if kind == "symbolic_link" else tarfile.LNKTYPE
                    member.linkname = "../outside"
                with tarfile.open(archive, "w") as source:
                    source.addfile(member)
                with self.assertRaises(getattr(tarfile, "FilterError", RuntimeError)):
                    SETUP.extract_archive(archive, root / "install")
                self.assertFalse((root / "outside").exists())

    def test_tar_special_files_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "sdk.tar"
            member = tarfile.TarInfo("pipe")
            member.type = tarfile.FIFOTYPE
            with tarfile.open(archive, "w") as source:
                source.addfile(member)
            with self.assertRaises(getattr(tarfile, "FilterError", RuntimeError)):
                SETUP.extract_archive(archive, root / "install")

    def test_missing_safe_filter_refuses_tar_extraction(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, patch.object(tarfile, "data_filter", None, create=True):
            root = Path(temporary)
            with self.assertRaisesRegex(RuntimeError, "Safe tar extraction is unavailable"):
                SETUP.extract_archive(root / "sdk.tar", root / "install")
            self.assertFalse((root / "install").exists())

    def test_zip_contents_stay_inside_the_destination(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "sdk.zip"
            with zipfile.ZipFile(archive, "w") as source:
                source.writestr("sdk/dotnet.exe", b"tool")
                source.writestr("../outside", b"outside")
            destination = root / "install"
            SETUP.extract_archive(archive, destination)
            self.assertEqual(b"tool", (destination / "sdk/dotnet.exe").read_bytes())
            self.assertFalse((root / "outside").exists())
