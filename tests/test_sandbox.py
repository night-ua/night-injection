"""End-to-end sandbox tests: fake steam dir + fake repo fetcher (no network)."""
from __future__ import annotations

import shutil
import tempfile
import unittest
import zipfile
from pathlib import Path

from lua.add import add_app_id
from lua.importer import import_dropped_files
from lua.remove import clear_plugins, remove_app_id
from steam.discovery import ensure_steam_dirs, verify_steam_path


def make_fake_fetcher(repo_url: str, lua_text: str, manifest_text: str | None = None):
    def fake_fetch(app_id: str, tmp_dir: Path, repos=None):
        extract_dir = tmp_dir / "extract"
        extract_dir.mkdir(parents=True, exist_ok=True)
        sub = extract_dir / f"repo-{app_id}"
        sub.mkdir(parents=True, exist_ok=True)
        (sub / f"{app_id}.lua").write_text(lua_text, encoding="utf-8")
        if manifest_text is not None:
            (sub / f"{app_id + '1'}_1.manifest").write_text(manifest_text, encoding="utf-8")
        return True, repo_url, extract_dir

    return fake_fetch


class TestAddAppIdSandbox(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="inger-sandbox-"))
        self.steam = self.tmp / "Steam"
        self.steam.mkdir(parents=True)
        (self.steam / "steam.exe").write_bytes(b"")
        ensure_steam_dirs(str(self.steam))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_add_other_repo(self):
        fetcher = make_fake_fetcher(
            "https://github.com/SteamAutoCracks/ManifestHub",
            "addappid(12210)\nsetManifestid(12211, 99)\n",
            "MANIFEST",
        )
        res = add_app_id(str(self.steam), "12210", fetcher=fetcher)
        self.assertTrue(res.ok)
        self.assertEqual(res.repo, "https://github.com/SteamAutoCracks/ManifestHub")
        lua_plugin = (self.steam / "config" / "stplug-in" / "12210.lua").read_text(encoding="utf-8")
        self.assertNotIn("setManifestid", lua_plugin)
        self.assertTrue((self.steam / "config" / "lua" / "12210.lua").exists())
        # manifest ignored for non-PLM repos
        self.assertFalse((self.steam / "config" / "depotcache").exists()
                         and any((self.steam / "config" / "depotcache").iterdir()))

    def test_duplicate_detection(self):
        fetcher = make_fake_fetcher("https://github.com/SteamAutoCracks/ManifestHub", "addappid(1)\n")
        add_app_id(str(self.steam), "12210", fetcher=fetcher)
        res2 = add_app_id(str(self.steam), "12210", fetcher=fetcher)
        self.assertFalse(res2.ok)
        self.assertIn("duplicate", res2.error)
        res3 = add_app_id(str(self.steam), "12210", fetcher=fetcher, force=True)
        self.assertTrue(res3.ok)

    def test_dry_run_writes_nothing(self):
        fetcher = make_fake_fetcher("https://github.com/LightnigFast/ProjectLightningManifests", "addappid(1)\n", "M")
        res = add_app_id(str(self.steam), "12210", fetcher=fetcher, dry_run=True)
        self.assertTrue(res.ok)
        self.assertEqual(res.error, "dry-run")
        self.assertFalse((self.steam / "config" / "stplug-in" / "12210.lua").exists())
        self.assertGreater(len(res.written), 0)

    def test_invalid_appid_raises(self):
        with self.assertRaises(ValueError):
            add_app_id(str(self.steam), "abc", fetcher=make_fake_fetcher("r", "x"))

    def test_remove(self):
        # NOTE (faithful-to-original): 'lightningtools:removeAppId' removes
        # <id>.lua (stplug-in + lua) and depotcache/<id>.manifest. Repo
        # manifests named <depot>_<manifestid>.manifest are NOT matched —
        # exactly like the original.
        fetcher = make_fake_fetcher("https://github.com/LightnigFast/ProjectLightningManifests", "addappid(1)\n", "M")
        add_app_id(str(self.steam), "12210", fetcher=fetcher)
        res = remove_app_id(str(self.steam), "12210")
        self.assertTrue(res.ok)
        self.assertEqual(len(res.written), 2)  # stplug-in + lua
        self.assertFalse((self.steam / "config" / "stplug-in" / "12210.lua").exists())
        # removing again is friendly (no error)
        res2 = remove_app_id(str(self.steam), "12210")
        self.assertTrue(res2.ok)

    def test_clear_plugins(self):
        fetcher = make_fake_fetcher("https://github.com/SteamAutoCracks/ManifestHub", "addappid(1)\n")
        add_app_id(str(self.steam), "12210", fetcher=fetcher)
        add_app_id(str(self.steam), "12220", fetcher=fetcher)
        res = clear_plugins(str(self.steam))
        self.assertEqual(res["deletedCount"], 2)

    def test_inject_lua_file_dry_run_and_apply(self):
        """The GUI flow: user picks a .lua file and injects it — no AppID."""
        lua_src = self.tmp / "mygame.lua"
        lua_src.write_text("addappid(5555)\n", encoding="utf-8")
        files = [{"path": str(lua_src), "name": "mygame.lua", "buffer": None}]

        # dry-run: plan only, nothing written
        plan = import_dropped_files(str(self.steam), files, dry_run=True)
        self.assertTrue(plan["ok"])
        self.assertEqual(plan["importedCount"], 1)
        self.assertEqual(len(plan["planned"]), 2)  # stplug-in + lua
        self.assertFalse((self.steam / "config" / "stplug-in" / "mygame.lua").exists())

        # apply: real injection like the original (basename of source path)
        res = import_dropped_files(str(self.steam), files)
        self.assertTrue(res["ok"])
        self.assertEqual(len(res["destinations"]), 2)
        self.assertTrue((self.steam / "config" / "stplug-in" / "mygame.lua").exists())
        self.assertTrue((self.steam / "config" / "lua" / "mygame.lua").exists())

    def test_import_lua_and_zip(self):
        # NOTE (faithful-to-original): importDroppedFiles names the destination
        # after path.basename(filePath || name) — i.e. the SOURCE file name.
        lua_src = self.tmp / "manual.lua"
        lua_src.write_text("addappid(999)\n", encoding="utf-8")
        res = import_dropped_files(str(self.steam), [{"path": str(lua_src), "name": "999.lua"}])
        self.assertTrue(res["ok"])
        self.assertEqual(res["importedCount"], 1)
        self.assertTrue((self.steam / "config" / "stplug-in" / "manual.lua").exists())
        self.assertTrue((self.steam / "config" / "lua" / "manual.lua").exists())

        zip_path = self.tmp / "pack.zip"
        with zipfile.ZipFile(zip_path, "w") as zf:
            zf.writestr("inner/777.lua", "addappid(777)\n")
            zf.writestr("inner/778_1.manifest", "MANIFEST")
        res2 = import_dropped_files(str(self.steam), [{"path": str(zip_path), "name": "pack.zip"}])
        self.assertTrue(res2["ok"])
        self.assertTrue((self.steam / "config" / "stplug-in" / "777.lua").exists())
        self.assertTrue((self.steam / "config" / "depotcache" / "778_1.manifest").exists())

    def test_verify_steam_path(self):
        self.assertEqual(verify_steam_path(str(self.steam)), {"valid": True})
        self.assertEqual(verify_steam_path(str(self.tmp / "nope"))["valid"], False)
        self.assertEqual(verify_steam_path("")["valid"], False)


if __name__ == "__main__":
    unittest.main()

