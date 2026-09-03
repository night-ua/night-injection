"""Tests for steam discovery, library scan, covers and the database layer."""
from __future__ import annotations

import shutil
import tempfile
import unittest
from pathlib import Path

from covers.local import find_image_recursive, to_file_url
from covers.remote import cover_fallback_url, steam_assets_urls
from steam.discovery import steam_config_dirs
from steam.library import get_steam_app_ids, load_steam_library
from storage.database import Database


class TestDiscovery(unittest.TestCase):
    def test_fallback_literal(self):
        # On machines without any Steam folder the original falls back to C:\\Steam2.
        # We only assert the mechanism, not the environment.
        from core.config import STEAM_PATH_FALLBACK

        self.assertEqual(STEAM_PATH_FALLBACK, r"C:\Steam2")

    def test_config_dirs_layout(self):
        dirs = steam_config_dirs(r"X:\Steam")
        self.assertEqual(dirs["plugin"], Path(r"X:\Steam") / "config" / "stplug-in")
        self.assertEqual(dirs["depotcache"], Path(r"X:\Steam") / "config" / "depotcache")
        self.assertEqual(dirs["lua"], Path(r"X:\Steam") / "config" / "lua")


class TestLibraryScan(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="inger-lib-"))
        self.steam = self.tmp / "Steam"
        self.steam.mkdir(parents=True)
        (self.steam / "steam.exe").write_bytes(b"")
        lua = self.steam / "config" / "lua"
        lua.mkdir(parents=True)
        (lua / "12210.lua").write_text("addappid(12210)\n", encoding="utf-8")
        (lua / "backup.lua").write_text("x", encoding="utf-8")  # non-numeric stem
        (lua / "notes.txt").write_text("x", encoding="utf-8")   # not lua
        # librarycache with a local cover for 12210
        cover_dir = self.steam / "appcache" / "librarycache" / "12210"
        cover_dir.mkdir(parents=True)
        (cover_dir / "library_600x900_2x.jpg").write_bytes(b"\xff\xd8fake")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_get_steam_app_ids_skips_non_numeric(self):
        ids = get_steam_app_ids(self.steam / "config" / "lua")
        self.assertEqual([e["appId"] for e in ids], ["12210"])

    def test_find_image_recursive(self):
        found = find_image_recursive(self.steam / "appcache" / "librarycache" / "12210")
        self.assertTrue(found.endswith("library_600x900_2x.jpg"))
        self.assertEqual(to_file_url(found).startswith("file:///"), True)

    def test_load_steam_library_offline(self):
        games = load_steam_library(str(self.steam), fetch_titles=False)
        self.assertEqual(len(games), 1)
        g = games[0]
        self.assertEqual(g.app_id, "12210")
        self.assertFalse(g.needs_steam_restart)  # local cover found
        self.assertIn("librarycache", g.img_src)

    def test_fallback_cover_when_no_local(self):
        lua = self.steam / "config" / "lua"
        (lua / "99999.lua").write_text("addappid(99999)\n", encoding="utf-8")
        games = load_steam_library(str(self.steam), fetch_titles=False)
        by_id = {g.app_id: g for g in games}
        self.assertTrue(by_id["99999"].needs_steam_restart)
        self.assertEqual(by_id["99999"].img_src, cover_fallback_url("99999"))
        self.assertEqual(by_id["99999"].title, "Game 99999")

    def test_steam_assets_urls(self):
        urls = steam_assets_urls(12210)
        self.assertIn("fastly.steamstatic.com", urls["cover"])
        self.assertTrue(urls["cover"].endswith("library_600x900_2x.jpg"))


class TestDatabase(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="inger-db-"))
        self.db = Database(self.tmp / "biblioteca.db")

    def tearDown(self):
        self.db.close()
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_lightning_history_upsert(self):
        self.db.record_lightning_op("12210", repo="r1", written=["a.lua", "b.manifest"], status="ok")
        self.db.record_lightning_op("12210", status="ok", game_name="GTA IV")
        rows = self.db.list_lightning_history()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["game_name"], "GTA IV")
        self.assertEqual(rows[0]["lua_path"], "a.lua")
        self.assertEqual(rows[0]["manifest_path"], "b.manifest")

    def test_juegos_schema_parity(self):
        rid = self.db.upsert_juego_lightning("12210", "GTA IV", "http://x/y.jpg")
        rid2 = self.db.upsert_juego_lightning("12210", "GTA IV CE", None)
        self.assertEqual(rid, rid2)
        juegos = self.db.list_juegos()
        self.assertEqual(len(juegos), 1)
        self.assertEqual(juegos[0]["nombre"], "GTA IV CE")
        self.assertEqual(juegos[0]["steam_id"], 12210)

    def test_original_schema_objects_exist(self):
        names = {
            r[0]
            for r in self.db.con.execute("SELECT name FROM sqlite_master WHERE type='table'")
        }
        self.assertLessEqual({"juegos", "sesiones", "fuentes", "lightning_history"}, names)


if __name__ == "__main__":
    unittest.main()
