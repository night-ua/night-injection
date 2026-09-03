"""Unit tests for the evidence-ported logic. Run from the project root:
    python -m unittest discover -s tests
"""
from __future__ import annotations

import shutil
import tempfile
import unittest
import zipfile
from pathlib import Path

from core.config import SIGNATURE_LINE
from lua.apply import process_extracted_repo_files, write_filtered_lines
from lua.validate import is_valid_app_id


class TestValidate(unittest.TestCase):
    def test_valid(self):
        self.assertTrue(is_valid_app_id("12210"))
        self.assertTrue(is_valid_app_id(" 12210 "))

    def test_invalid(self):
        self.assertFalse(is_valid_app_id("abc"))
        self.assertFalse(is_valid_app_id("12a10"))
        self.assertFalse(is_valid_app_id("-5"))
        self.assertFalse(is_valid_app_id(12210))  # original requires str
        self.assertFalse(is_valid_app_id(""))


class TestWriteFilteredLines(unittest.TestCase):
    def test_filter_and_signature(self):
        src = "addappid(1)\nsetManifestid(2,3)\naddappid(4)\n"
        out = write_filtered_lines(src, lambda l: "setManifestid" not in l)
        self.assertEqual(
            out,
            "addappid(1)\naddappid(4)\n\n" + SIGNATURE_LINE,
        )

    def test_addappid_only(self):
        src = "-- comment\naddappid(5, 1, \"key\")\nxyz\n"
        out = write_filtered_lines(src, lambda l: l.lstrip().startswith("addappid("))
        self.assertEqual(out, 'addappid(5, 1, "key")\n\n' + SIGNATURE_LINE)


def _make_repo_zip(root: Path, name: str, files: dict[str, str]) -> Path:
    """Build a repo-style zip: <root>/<repo>-<appid>/{...} like GitHub archives."""
    zip_path = root / name
    with zipfile.ZipFile(zip_path, "w") as zf:
        for rel, content in files.items():
            zf.writestr(rel, content)
    return zip_path


class TestProcessExtractedRepoFiles(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="inger-test-"))
        self.plugin = self.tmp / "stplug-in"
        self.depot = self.tmp / "depotcache"
        self.lua = self.tmp / "lua"
        for d in (self.plugin, self.depot, self.lua):
            d.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def _extract(self, files: dict[str, str]) -> Path:
        zpath = _make_repo_zip(self.tmp, "repo.zip", files)
        extract_dir = self.tmp / "extract"
        with zipfile.ZipFile(zpath) as zf:
            zf.extractall(extract_dir)
        return extract_dir

    def test_plm_copies_unfiltered(self):
        ed = self._extract({
            "repo-12210/12210.lua": "addappid(12210)\nsetManifestid(1,2)\n",
            "repo-12210/12211_1.manifest": "MANIFESTDATA",
        })
        written = process_extracted_repo_files(ed, "https://github.com/x/ProjectLightningManifests", self.plugin, self.depot, self.lua)
        # plugin lua + lua folder copy + depotcache manifest = 3 destinations
        self.assertEqual(len(written), 3)
        self.assertIn("setManifestid", (self.plugin / "12210.lua").read_text(encoding="utf-8"))
        self.assertTrue((self.lua / "12210.lua").exists())
        self.assertEqual((self.depot / "12211_1.manifest").read_text(encoding="utf-8"), "MANIFESTDATA")

    def test_spin_filters_to_addappid(self):
        ed = self._extract({
            "repo-12210/12210.lua": "-- hi\naddappid(12210)\nsetManifestid(1,2)\n",
        })
        process_extracted_repo_files(ed, "https://github.com/x/SB_manifest_DB.SPIN0ZAi", self.plugin, self.depot, self.lua)
        text = (self.plugin / "12210.lua").read_text(encoding="utf-8")
        self.assertIn("addappid(12210)", text)
        self.assertNotIn("setManifestid", text)
        self.assertIn(SIGNATURE_LINE, text)
        self.assertTrue((self.lua / "12210.lua").exists())

    def test_other_repo_drops_setmanifestid_and_ignores_manifest(self):
        ed = self._extract({
            "repo-12210/12210.lua": "addappid(12210)\nsetManifestid(1,2)\n",
            "repo-12210/12211_1.manifest": "MANIFESTDATA",
        })
        written = process_extracted_repo_files(ed, "https://github.com/SteamAutoCracks/ManifestHub", self.plugin, self.depot, self.lua)
        text = (self.plugin / "12210.lua").read_text(encoding="utf-8")
        self.assertNotIn("setManifestid", text)
        self.assertIn("addappid(12210)", text)
        self.assertFalse((self.depot / "12211_1.manifest").exists())
        self.assertEqual(len(written), 2)  # plugin + lua only


if __name__ == "__main__":
    unittest.main()
