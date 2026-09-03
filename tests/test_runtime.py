"""Runtime infrastructure tests for settings, caching, and safe imports."""
from __future__ import annotations

import shutil
import tempfile
import unittest
import zipfile
from io import BytesIO
from pathlib import Path
from unittest.mock import patch

from PIL import Image

from core.settings import AppSettings, SettingsStore
from covers.cache import SteamAssetCache
from lua.fetch import extract_zip
from lua.importer import import_dropped_files


class TestSettingsStore(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="night-injection-settings-"))

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_round_trip_and_unknown_keys(self):
        store = SettingsStore(self.root / "settings.json")
        settings = AppSettings(steam_path=r"C:\Steam", animations_enabled=False)
        store.save(settings)
        loaded = store.load()
        self.assertEqual(loaded.steam_path, r"C:\Steam")
        self.assertFalse(loaded.animations_enabled)
        self.assertFalse((self.root / "settings.tmp").exists())

    def test_corrupt_settings_fall_back_safely(self):
        path = self.root / "settings.json"
        path.write_text("{broken", encoding="utf-8")
        self.assertEqual(SettingsStore(path).load(), AppSettings())


class TestSafeZipAndCache(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="night-injection-runtime-"))

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_zip_path_traversal_is_rejected(self):
        archive = self.root / "unsafe.zip"
        with zipfile.ZipFile(archive, "w") as handle:
            handle.writestr("../outside.lua", "addappid(1)")
        with self.assertRaises(ValueError):
            extract_zip(archive, self.root / "extract")
        self.assertFalse((self.root / "outside.lua").exists())

    def test_local_cover_is_preferred_offline(self):
        local = self.root / "local.jpg"
        Image.new("RGB", (80, 120), "orange").save(local)
        cache = SteamAssetCache(self.root / "cache", timeout=0.01)
        self.assertEqual(cache.get_cover("10", local.as_uri()), local)

    def test_remote_cover_is_validated_and_cached(self):
        payload = BytesIO()
        Image.new("RGB", (80, 120), "orange").save(payload, format="JPEG")

        class Response:
            status = 200

            def __init__(self):
                self.headers = {"Content-Type": "image/jpeg"}

            def __enter__(self):
                return self

            def __exit__(self, *_args):
                return False

            def read(self, _limit):
                return payload.getvalue()

        cache = SteamAssetCache(self.root / "cache")
        with patch("urllib.request.urlopen", return_value=Response()) as request:
            downloaded = cache.get_cover("123")
            reused = cache.get_cover("123")
        self.assertEqual(downloaded, reused)
        self.assertTrue(downloaded.is_file())
        self.assertEqual(request.call_count, 1)

    def test_invalid_steam_path_does_not_create_directories(self):
        steam = self.root / "missing-steam"
        source = self.root / "game.lua"
        source.write_text("addappid(10)", encoding="utf-8")
        with self.assertRaises(ValueError):
            import_dropped_files(str(steam), [{"path": str(source), "name": source.name}])
        self.assertFalse((steam / "config").exists())


if __name__ == "__main__":
    unittest.main()
