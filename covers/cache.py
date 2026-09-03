"""Thread-safe Steam metadata and artwork cache."""
from __future__ import annotations

import json
import logging
import os
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path

from PIL import Image

from core.config import APPDETAILS_URL, COVER_FALLBACK_URL, HTTP_USER_AGENT
from core.paths import resource_path, user_data_dir
from covers.remote import steam_assets_urls

log = logging.getLogger("night_injection.covers.cache")


class SteamAssetCache:
    """Resolve Steam names and covers without blocking the UI thread.

    The GUI owns scheduling. This class handles persistence, request de-duplication,
    bounded retries, timeouts, and offline fallbacks.
    """

    def __init__(self, cache_dir: str | Path | None = None, timeout: float = 4.0):
        self.cache_dir = Path(cache_dir) if cache_dir else user_data_dir() / "cache" / "covers"
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.timeout = timeout
        self.metadata_path = self.cache_dir.parent / "steam_metadata.json"
        self._lock = threading.RLock()
        self._app_locks: dict[str, threading.Lock] = {}
        self._metadata = self._load_metadata()

    @property
    def placeholder_path(self) -> Path:
        return resource_path(Path("assets") / "logo.jpg")

    def _load_metadata(self) -> dict[str, dict]:
        try:
            value = json.loads(self.metadata_path.read_text(encoding="utf-8"))
            return value if isinstance(value, dict) else {}
        except (FileNotFoundError, OSError, ValueError):
            return {}

    def _save_metadata(self) -> None:
        self.metadata_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.metadata_path.with_suffix(".tmp")
        temporary.write_text(json.dumps(self._metadata, indent=2), encoding="utf-8")
        os.replace(temporary, self.metadata_path)

    def cached_title(self, app_id: str) -> str | None:
        with self._lock:
            title = self._metadata.get(str(app_id), {}).get("name")
            return str(title) if title else None

    def resolve_metadata(self, app_id: str) -> dict:
        app_id = str(app_id).strip()
        with self._lock:
            cached = self._metadata.get(app_id)
            if (
                cached
                and cached.get("name")
                and (not cached.get("offline") or time.time() - cached.get("checked_at", 0) < 3600)
            ):
                return dict(cached)
        url = APPDETAILS_URL.format(app_id=app_id).replace("&l=spanish", "&l=english")
        try:
            payload = self._read_json(url)
            node = payload.get(app_id) or {}
            data = node.get("data") if node.get("success") else None
            if not isinstance(data, dict):
                raise TypeError("Steam returned no game metadata")
            result = {
                "name": str(data.get("name") or f"Game {app_id}"),
                "type": str(data.get("type") or "game"),
            }
            with self._lock:
                self._metadata[app_id] = result
                self._save_metadata()
            return result
        except (OSError, ValueError, TypeError, urllib.error.URLError) as exc:
            log.info("Metadata unavailable for AppID %s: %s", app_id, exc)
            result = {
                "name": self.cached_title(app_id) or f"Game {app_id}",
                "offline": True,
                "checked_at": time.time(),
            }
            with self._lock:
                self._metadata[app_id] = result
                self._save_metadata()
            return result

    def get_cover(self, app_id: str, local_source: str | None = None) -> Path:
        local = self._local_path(local_source)
        if local and self._valid_image(local):
            return local
        app_id = str(app_id).strip()
        cached = self.cache_dir / f"{app_id}.jpg"
        missing_marker = self.cache_dir / f"{app_id}.missing"
        if self._valid_image(cached):
            return cached
        if missing_marker.is_file() and time.time() - missing_marker.stat().st_mtime < 21600:
            return self.placeholder_path
        with self._lock:
            app_lock = self._app_locks.setdefault(app_id, threading.Lock())
        with app_lock:
            if self._valid_image(cached):
                return cached
            urls = steam_assets_urls(app_id)
            candidates = [
                urls["cover"],
                COVER_FALLBACK_URL.format(app_id=app_id),
                urls["capsule"],
                urls["header"],
                urls["hero"],
            ]
            for url in candidates:
                if self._download_image(url, cached):
                    missing_marker.unlink(missing_ok=True)
                    return cached
            try:
                missing_marker.touch()
            except OSError:
                pass
        return self.placeholder_path

    def clear(self) -> int:
        removed = 0
        for path in self.cache_dir.glob("*"):
            if path.is_file():
                try:
                    path.unlink()
                    removed += 1
                except OSError as exc:
                    log.warning("Could not remove cache file %s: %s", path, exc)
        return removed

    def _read_json(self, url: str) -> dict:
        request = urllib.request.Request(url, headers={"User-Agent": HTTP_USER_AGENT})
        with urllib.request.urlopen(request, timeout=self.timeout) as response:
            return json.loads(response.read().decode("utf-8", "replace"))

    def _download_image(self, url: str, destination: Path) -> bool:
        temporary = destination.with_suffix(".download")
        try:
            request = urllib.request.Request(url, headers={"User-Agent": HTTP_USER_AGENT})
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                content_type = response.headers.get("Content-Type", "")
                if response.status != 200 or not content_type.startswith("image/"):
                    return False
                data = response.read(8 * 1024 * 1024 + 1)
            if not data or len(data) > 8 * 1024 * 1024:
                return False
            temporary.write_bytes(data)
            if not self._valid_image(temporary):
                temporary.unlink(missing_ok=True)
                return False
            os.replace(temporary, destination)
            return True
        except (OSError, ValueError, urllib.error.URLError) as exc:
            temporary.unlink(missing_ok=True)
            log.info("Artwork unavailable from %s: %s", url, exc)
        return False

    @staticmethod
    def _local_path(source: str | None) -> Path | None:
        if not source or source.startswith("http"):
            return None
        cleaned = source.removeprefix("file:///").replace("/", os.sep)
        return Path(cleaned)

    @staticmethod
    def _valid_image(path: Path) -> bool:
        if not path.is_file():
            return False
        try:
            with Image.open(path) as image:
                image.verify()
            return True
        except (OSError, ValueError):
            return False
