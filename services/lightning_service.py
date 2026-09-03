"""High-level Lightning Tools service — orchestrates the proven pipeline:

  UI action (CLI here) -> validate -> duplicate check -> repo zip fetch ->
  extract -> per-repo filter/copy -> (optional) DB record -> library rebuild.

Original counterpart: the 'lightningtools:addAppId' / ':importFiles' /
':removeAppId' IPC round-trip between lightningtools.js and
lightningtools-ipc.js (HIGH evidence).
"""
from __future__ import annotations

import logging
from dataclasses import asdict
from pathlib import Path

from lua.add import add_app_id
from lua.apply import ApplyResult
from lua.importer import import_dropped_files
from lua.remove import clear_plugins, remove_app_id
from steam.discovery import get_steam_base_path
from steam.library import load_steam_library

log = logging.getLogger("night_injection.services.lightning")


class LightningService:
    def __init__(self, steam_base_path: str | None = None, db=None):
        self.steam_base_path = steam_base_path or get_steam_base_path()
        self.db = db

    # --- add ---------------------------------------------------------------
    def add_app_id(self, app_id: str, *, dry_run: bool = False, force: bool = False) -> ApplyResult | dict:
        result = add_app_id(
            self.steam_base_path, app_id, dry_run=dry_run, force=force, db=self.db
        )
        if result.ok and not dry_run and self.db is not None:
            try:
                self._enrich_history(app_id)
            except Exception as exc:  # noqa: BLE001
                log.debug("enrich failed: %s", exc)
        return result

    def _enrich_history(self, app_id: str) -> None:
        from covers.remote import fetch_game_title_from_steam

        name = fetch_game_title_from_steam(app_id)
        self.db.record_lightning_op(app_id, status="ok", game_name=name)

    # --- remove / inject / clear -------------------------------------------
    def remove_app_id(self, app_id: str) -> ApplyResult:
        return remove_app_id(self.steam_base_path, app_id, db=self.db)

    def inject_files(self, files: list[dict], *, dry_run: bool = False) -> dict:
        """Inject user-provided .lua / .manifest / .zip files — the original
        importDroppedFiles path (no AppID required). .lua goes to
        config/stplug-in + config/lua, .manifest to config/depotcache."""
        result = import_dropped_files(self.steam_base_path, files, dry_run=dry_run)
        if result.get("ok") and not dry_run:
            for f in files:
                try:
                    name = Path(f["path"]).name if f.get("path") else (f.get("name") or "")
                    if name.lower().endswith(".lua"):
                        self.db.record_lightning_op(
                            Path(name).stem, repo=None,
                            written=[d for d in result.get("destinations", []) if name in d],
                            status="injected",
                        )
                except Exception as exc:  # noqa: BLE001
                    log.debug("Could not record injected file history: %s", exc)
        return result

    def clear_plugins(self) -> dict:
        return clear_plugins(self.steam_base_path)

    # --- library ------------------------------------------------------------
    def build_library(self, fetch_titles: bool = True) -> list[dict]:
        games = load_steam_library(self.steam_base_path, fetch_titles=fetch_titles)
        return [asdict(g) for g in games]

    def processed_app_ids(self) -> list[dict]:
        """Evidence-based 'which games were processed' = the lua folder itself."""
        from steam.library import get_steam_app_ids

        lua_folder = Path(self.steam_base_path) / "config" / "lua"
        return get_steam_app_ids(lua_folder)
