"""Remote metadata + covers.

Ports (HIGH evidence):
  * fetchGameTitleFromSteam() [lightningtools.js]:
      https://store.steampowered.com/api/appdetails?appids=<id>&l=spanish
      fallback title: "Game <id>"
  * cover fallback URL [lightningtools.js]:
      https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/<id>/library_600x900.jpg
  * official Steam assets [biblioteca-ipc.js 'BOTÓN AZUL']:
      shared.fastly.steamstatic.com/.../library_hero.jpg | library_600x900_2x.jpg | logo.png
"""
from __future__ import annotations

import json
import logging
import urllib.request
from pathlib import Path

from core.config import (
    APPDETAILS_URL,
    COVER_FALLBACK_URL,
    STEAM_ASSETS_URLS,
)

log = logging.getLogger("night_injection.covers.remote")


def cover_fallback_url(app_id: str) -> str:
    return COVER_FALLBACK_URL.format(app_id=app_id)


def steam_assets_urls(steam_id: str | int) -> dict[str, str]:
    return {k: v.format(steam_id=steam_id) for k, v in STEAM_ASSETS_URLS.items()}


def fetch_game_title_from_steam(app_id: str, cache: dict[str, str] | None = None) -> str:
    """Resolve a Steam game name, using ``Game <id>`` when offline.

    The optional cache dictionary avoids repeated requests in one process.
    """
    if cache and app_id in cache:
        return cache[app_id]
    url = APPDETAILS_URL.format(app_id=app_id)
    try:
        req = urllib.request.Request(url, headers={"user-agent": "Project-Lightning"})
        with urllib.request.urlopen(req, timeout=15) as res:
            payload = json.loads(res.read().decode("utf-8", "replace"))
        node = payload.get(app_id) or {}
        if node.get("success") and node.get("data"):
            name = str(node["data"].get("name") or "").strip()
            if name:
                if cache is not None:
                    cache[app_id] = name
                return name
    except Exception as exc:  # noqa: BLE001 — original catches everything
        log.debug("appdetails failed for %s: %s", app_id, exc)
    return f"Game {app_id}"


def download_cover(app_id: str, dest_dir: Path, timeout: float = 30.0) -> Path | None:
    """Extension (documented): persists the fallback cover to disk. Original
    only referenced the remote URL in the UI. Filename scheme mirrors the
    original naming idea '<AppID>.<ext>' used for lua/manifest files."""
    url = cover_fallback_url(app_id)
    dest = dest_dir / f"{app_id}.jpg"
    try:
        req = urllib.request.Request(url, headers={"user-agent": "Project-Lightning"})
        with urllib.request.urlopen(req, timeout=timeout) as res:
            data = res.read()
        if res.status != 200 or not data:
            return None
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        return dest
    except Exception as exc:  # noqa: BLE001
        log.debug("cover download failed for %s: %s", app_id, exc)
        return None
