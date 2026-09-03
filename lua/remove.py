"""remove_app_id + clear_plugins — verbatim ports."""
from __future__ import annotations

import logging

from lua.base import ApplyResult
from lua.validate import require_valid_app_id
from steam.discovery import steam_config_dirs

log = logging.getLogger("night_injection.lua.remove")


def remove_app_id(steam_base_path: str, app_id: str, *, db=None) -> ApplyResult:
    """Original 'lightningtools:removeAppId' [lightningtools-ipc.js]: silently
    removes stplug-in/<id>.lua, lua/<id>.lua, depotcache/<id>.manifest. Never
    fails if files are missing, matching the original friendly behavior."""
    steam_path = str(steam_base_path or "").strip()
    app_id = require_valid_app_id(str(app_id or "").strip())
    if not steam_path:
        raise ValueError("Steam path is empty")

    dirs = steam_config_dirs(steam_path)
    targets = [
        dirs["plugin"] / f"{app_id}.lua",
        dirs["lua"] / f"{app_id}.lua",
        dirs["depotcache"] / f"{app_id}.manifest",
    ]
    removed: list[str] = []
    for t in targets:
        if t.exists():
            t.unlink()
            removed.append(str(t))
    log.info("removed AppID %s: %d files", app_id, len(removed))
    if db is not None:
        db.record_lightning_op(app_id, repo=None, written=removed, status="removed")
    return ApplyResult(ok=True, written=removed)


def clear_plugins(steam_path: str) -> dict:
    """Original 'lightningtools:clearPlugins' [main.js]: deletes every file in
    config/stplug-in. Returns {'ok': True, 'deletedCount': int}."""
    p = str(steam_path or "").strip()
    if not p:
        raise ValueError("Steam path is empty")
    plugin_folder = steam_config_dirs(p)["plugin"]
    deleted = 0
    if plugin_folder.exists():
        for f in list(plugin_folder.iterdir()):
            if f.is_file():
                f.unlink(missing_ok=True)
                deleted += 1
    log.info("clearPlugins: deleted %d files from %s", deleted, plugin_folder)
    return {"ok": True, "deletedCount": deleted}
