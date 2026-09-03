"""Steam installation discovery.

Python equivalent of:
  * getSteamBasePath()            [src/renderer/pages/lightningtools/lightningtools.js]
  * 'lightningtools:verifySteamPath' handler [src/main/main.js @~103450]

Original behavior (HIGH evidence):
  1. saved path (localStorage 'lightningtools-steam-path' in the app; we accept
     a parameter / persisted config file instead) — used only if it exists
  2. window.STEAM_BASE_PATH — never found defined in the original bundle
     ("Not yet proven"), skipped here
  3. hardcoded candidate list, first that exists wins
  4. literal fallback r"C:\\Steam2" (yes, Steam2)
verify_steam_path checks that <path>/steam.exe exists.
"""
from __future__ import annotations

import logging
from pathlib import Path

from core.config import STEAM_PATH_CANDIDATES, STEAM_PATH_FALLBACK

log = logging.getLogger("night_injection.steam.discovery")


def get_steam_base_path(saved_path: str | None = None) -> str:
    """Original: getSteamBasePath() — HIGH confidence 1:1 port."""
    if saved_path:
        saved = str(saved_path).strip()
        if saved and Path(saved).exists():
            return saved
    for candidate in STEAM_PATH_CANDIDATES:
        if Path(candidate).exists():
            log.debug("steam path candidate matched: %s", candidate)
            return candidate
    log.warning("no steam path found; using original literal fallback %s", STEAM_PATH_FALLBACK)
    return STEAM_PATH_FALLBACK


def verify_steam_path(steam_path: str) -> dict:
    """Original: ipcMain.handle('lightningtools:verifySteamPath') — checks
    <path>\\steam.exe. Returns {'valid': bool, 'error'?: str}."""
    p = str(steam_path or "").strip()
    if not p:
        return {"valid": False, "error": "Steam path is empty"}
    exists = (Path(p) / "steam.exe").exists()
    if not exists:
        return {"valid": False, "error": "steam.exe was not found in the selected directory"}
    return {"valid": True}


def steam_config_dirs(steam_base_path: str) -> dict[str, Path]:
    """The three directories the original always mkdirs (recursive) before use:
    config/stplug-in, config/depotcache, config/lua."""
    base = Path(steam_base_path)
    return {
        "plugin": base / "config" / "stplug-in",
        "depotcache": base / "config" / "depotcache",
        "lua": base / "config" / "lua",
    }


def ensure_steam_dirs(steam_base_path: str) -> dict[str, Path]:
    dirs = steam_config_dirs(steam_base_path)
    for d in dirs.values():
        d.mkdir(parents=True, exist_ok=True)
    return dirs
