"""Steam library listing — port of loadSteamLibrary()/getSteamAppIds().

Original (HIGH evidence, lightningtools.js):
  * getSteamAppIds(folder): lists *.lua files, appId = filename stem, keeps the
    max mtime per appId, skips non-numeric stems.
  * loadSteamLibrary(): uses <steam>/config/lua; per appId searches
    <steam>/appcache/librarycache/<appId> recursively for library_600x900 /
    library_capsule images; fallback = shared.akamai URL + needsSteamRestart.
"""
from __future__ import annotations

import logging
import re
from dataclasses import dataclass
from pathlib import Path

from core.config import COVER_EXTENSIONS, COVER_FALLBACK_URL, COVER_NAME_PATTERNS
from covers.remote import fetch_game_title_from_steam

log = logging.getLogger("night_injection.steam.library")


@dataclass
class GameEntry:
    app_id: str
    title: str = ""
    img_src: str = ""
    needs_steam_restart: bool = False
    recent_ts: float = 0.0
    source: str = "lua-folder"  # provenance marker (Reimplemented)


def _parse_name(filename: str) -> str:
    """Original: path.parse(filename).name (stem without last extension)."""
    return Path(filename).stem


def get_steam_app_ids(stplugin_or_lua_path: str | Path) -> list[dict]:
    """Original: getSteamAppIds() — returns [{'appId': str, 'recentTs': float}].
    Non-numeric stems are skipped (isNaN check in original)."""
    folder = Path(stplugin_or_lua_path)
    by_id: dict[str, float] = {}
    if folder.exists():
        for file in folder.iterdir():
            if not file.is_file():
                continue
            if file.suffix.lower() != ".lua":
                continue
            app_id = _parse_name(file.name)
            if not app_id or not app_id.isdigit():
                continue
            try:
                ts = file.stat().st_mtime * 1000.0
            except OSError:
                ts = 0.0
            by_id[app_id] = max(by_id.get(app_id, 0.0), ts)
    return [{"appId": k, "recentTs": v} for k, v in by_id.items()]


def find_image_recursive(dir_path: str | Path) -> str:
    """Original: findImageRecursive() — recursive scan for files whose name
    matches library_600x900 / library_capsule with known image extensions.
    Returns '' when not found (original behavior)."""
    folder = Path(dir_path)
    if not folder.exists():
        return ""
    exts = tuple(COVER_EXTENSIONS)
    patterns = [re.compile(p, re.IGNORECASE) for p in COVER_NAME_PATTERNS]
    # original collects subfolders first (collect(dirPath)) then the root itself,
    # scanning each folder's *files* in that order
    folders: list[Path] = []

    def collect(current: Path) -> None:
        if not current.exists():
            return
        try:
            items = sorted(current.iterdir())
        except OSError:
            return
        for item in items:
            if item.name in (".", ".."):
                continue
            if item.is_dir():
                folders.append(item)
                collect(item)
        folders.append(current)

    collect(folder)
    for sub in folders:
        try:
            items = sorted(sub.iterdir())
        except OSError:
            continue
        for item in items:
            if item.is_dir():
                continue
            name = item.name.lower()
            if not name.endswith(exts):
                continue
            if any(p.search(name) for p in patterns):
                return str(item)
    return ""


def to_file_url(p: str | Path) -> str:
    """Original: toFileUrl() -> 'file:///' + p.replace(/\\\\/g, '/')."""
    return "file:///" + str(p).replace("\\", "/")


def load_steam_library(
    steam_base_path: str,
    saved_title_cache: dict[str, str] | None = None,
    fetch_titles: bool = True,
) -> list[GameEntry]:
    """Original: loadSteamLibrary() — HIGH confidence port.

    NOTE: original mkdirs config/, config/lua/, config/depotcache/ (side effect
    preserved). Titles come from the Steam appdetails API (l=spanish), matching
    the original endpoint, with an English offline fallback.
    """
    base = Path(steam_base_path)
    config_path = base / "config"
    lua_path = config_path / "lua"
    depotcache_path = config_path / "depotcache"
    librarycache_path = base / "appcache" / "librarycache"

    config_path.mkdir(parents=True, exist_ok=True)
    lua_path.mkdir(exist_ok=True)
    depotcache_path.mkdir(exist_ok=True)

    entries = get_steam_app_ids(lua_path)

    def _build(e: dict) -> GameEntry:
        app_id = e["appId"]
        cache_folder = librarycache_path / app_id
        img = find_image_recursive(cache_folder)
        if img:
            img_src = to_file_url(img)
            needs_restart = False
        else:
            needs_restart = True
            img_src = COVER_FALLBACK_URL.format(app_id=app_id)
        if fetch_titles:
            title = fetch_game_title_from_steam(app_id, cache=saved_title_cache)
        else:
            title = (saved_title_cache or {}).get(app_id) or f"Game {app_id}"
        return GameEntry(
            app_id=app_id,
            title=title,
            img_src=img_src,
            needs_steam_restart=needs_restart,
            recent_ts=e["recentTs"],
        )

    # Original uses Promise.all (parallel) — replicate with a thread pool.
    from concurrent.futures import ThreadPoolExecutor

    with ThreadPoolExecutor(max_workers=8) as pool:
        games = list(pool.map(_build, entries))
    return games

