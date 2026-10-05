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
TAR_FILTERS = (("system", getattr(tarfile, "data_filter", None)), ("compatibility", None))


class SetupDotnetValidationTests(unittest.TestCase):
    def test_regular_tar_files_retain_contents_and_executable_permissions(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            with self.subTest(filter=name), tempfile.TemporaryDirectory() as temporary, \
                    patch.object(tarfile, "data_filter", extraction_filter, create=True):
                root = Path(temporary)
                archive = root / "sdk.tar.gz"
                directory = tarfile.TarInfo("sdk")
                directory.type = tarfile.DIRTYPE
                member = tarfile.TarInfo("sdk/dotnet")
                member.mode = 0o755
                member.mtime = 1234567890
                member.size = 4
                with tarfile.open(archive, "w:gz") as source:
                    source.addfile(directory)
                    source.addfile(member, io.BytesIO(b"tool"))
                destination = root / "install"
                SETUP.extract_archive(archive, destination)
                self.assertEqual(b"tool", (destination / "sdk/dotnet").read_bytes())
                self.assertEqual(member.mtime, (destination / "sdk/dotnet").stat().st_mtime)
                if os.name != "nt":
                    self.assertEqual(0o755, (destination / "sdk/dotnet").stat().st_mode & 0o777)

    def test_tar_paths_and_links_cannot_escape_the_destination(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            for kind in ("parent_path", "symbolic_link", "hard_link"):
                with self.subTest(filter=name, kind=kind), tempfile.TemporaryDirectory() as temporary, \
                        patch.object(tarfile, "data_filter", extraction_filter, create=True):
                    root = Path(temporary)
                    archive = root / "sdk.tar"
                    member = tarfile.TarInfo("../outside" if kind == "parent_path" else "link")
                    if kind != "parent_path":
                        member.type = tarfile.SYMTYPE if kind == "symbolic_link" else tarfile.LNKTYPE
                        member.linkname = "../outside"
                    with tarfile.open(archive, "w") as source:
                        source.addfile(member)
                    with self.assertRaises((tarfile.TarError, ValueError)):
                        SETUP.extract_archive(archive, root / "install")
                    self.assertFalse((root / "outside").exists())

    def test_tar_special_files_are_rejected(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            for member_type in (tarfile.FIFOTYPE, tarfile.CHRTYPE, tarfile.BLKTYPE):
                with self.subTest(filter=name, member_type=member_type), tempfile.TemporaryDirectory() as temporary, \
                        patch.object(tarfile, "data_filter", extraction_filter, create=True):
                    root = Path(temporary)
                    archive = root / "sdk.tar"
                    member = tarfile.TarInfo("special")
                    member.type = member_type
                    with tarfile.open(archive, "w") as source:
                        source.addfile(member)
                    with self.assertRaises((tarfile.TarError, ValueError)):
                        SETUP.extract_archive(archive, root / "install")
                    self.assertFalse((root / "install/special").exists())

    def test_compatibility_extraction_rejects_absolute_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, patch.object(tarfile, "data_filter", None, create=True):
            root = Path(temporary)
            archive = root / "sdk.tar"
            with tarfile.open(archive, "w") as source:
                source.addfile(tarfile.TarInfo(str(root / "outside")))
            with self.assertRaisesRegex(ValueError, "escapes the destination"):
                SETUP.extract_archive(archive, root / "install")
            self.assertFalse((root / "install").exists())
            self.assertFalse((root / "outside").exists())

    def test_compatibility_extraction_rejects_links_inside_the_destination(self) -> None:
        for member_type in (tarfile.SYMTYPE, tarfile.LNKTYPE):
            with self.subTest(member_type=member_type), tempfile.TemporaryDirectory() as temporary, \
                    patch.object(tarfile, "data_filter", None, create=True):
                root = Path(temporary)
                archive = root / "sdk.tar"
                member = tarfile.TarInfo("link")
                member.type = member_type
                member.linkname = "inside"
                with tarfile.open(archive, "w") as source:
                    source.addfile(tarfile.TarInfo("inside"))
                    source.addfile(member)
                with self.assertRaisesRegex(ValueError, "links and special files"):
                    SETUP.extract_archive(archive, root / "install")
                self.assertFalse((root / "install/link").exists())

    def test_existing_directory_links_cannot_redirect_tar_writes(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            with self.subTest(filter=name), tempfile.TemporaryDirectory() as temporary, \
                    patch.object(tarfile, "data_filter", extraction_filter, create=True):
                root = Path(temporary)
                destination = root / "install"
                destination.mkdir()
                outside = root / "outside"
                outside.mkdir()
                (destination / "sdk").symlink_to(outside, target_is_directory=True)
                archive = root / "sdk.tar"
                member = tarfile.TarInfo("sdk/dotnet")
                member.size = 4
                with tarfile.open(archive, "w") as source:
                    source.addfile(member, io.BytesIO(b"tool"))
                with self.assertRaises((tarfile.TarError, ValueError)):
                    SETUP.extract_archive(archive, destination)
                self.assertFalse((outside / "dotnet").exists())

    def test_existing_file_links_cannot_redirect_tar_writes(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            with self.subTest(filter=name), tempfile.TemporaryDirectory() as temporary, \
                    patch.object(tarfile, "data_filter", extraction_filter, create=True):
                root = Path(temporary)
                destination = root / "install"
                destination.mkdir()
                outside = root / "outside"
                outside.write_bytes(b"original")
                (destination / "dotnet").symlink_to(outside)
                archive = root / "sdk.tar"
                member = tarfile.TarInfo("dotnet")
                member.size = 4
                with tarfile.open(archive, "w") as source:
                    source.addfile(member, io.BytesIO(b"tool"))
                with self.assertRaises((tarfile.TarError, ValueError)):
                    SETUP.extract_archive(archive, destination)
                self.assertEqual(b"original", outside.read_bytes())

    def test_tar_permissions_remove_special_bits_and_unintended_execute_access(self) -> None:
        for name, extraction_filter in TAR_FILTERS:
            for mode, expected in ((0o7777, 0o755), (0o666, 0o644), (0o655, 0o644), (0o400, 0o600)):
                with self.subTest(filter=name, mode=mode), tempfile.TemporaryDirectory() as temporary, \
                        patch.object(tarfile, "data_filter", extraction_filter, create=True):
                    root = Path(temporary)
                    archive = root / "sdk.tar"
                    member = tarfile.TarInfo("file")
                    member.mode = mode
                    with tarfile.open(archive, "w") as source:
                        source.addfile(member)
                    destination = root / "install"
                    SETUP.extract_archive(archive, destination)
                    if os.name != "nt":
                        self.assertEqual(expected, (destination / "file").stat().st_mode & 0o7777)

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
