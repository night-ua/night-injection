"""add_app_id - verbatim port of buscarYAgregarAppId + documented extensions
(dry-run via shadow planning, duplicate detection, rollback journal)."""
from __future__ import annotations

import logging
import shutil
from pathlib import Path

from lua.base import ApplyResult, process_extracted_repo_files
from lua.fetch import fetch_appid_bundle, make_temp_dir
from lua.validate import require_valid_app_id
from steam.discovery import steam_config_dirs

log = logging.getLogger("night_injection.lua.add")


def add_app_id(
    steam_base_path: str,
    app_id: str,
    *,
    dry_run: bool = False,
    force: bool = False,
    repos: list[str] | None = None,
    fetcher=fetch_appid_bundle,
    db=None,
) -> ApplyResult:
    """Original: buscarYAgregarAppId({steamBasePath, appId}) - HIGH evidence.

    Extensions (documented):
      * dry_run: processes the bundle into a SHADOW folder and reports the real
        destination paths that would be written; nothing touches Steam folders.
      * force: original has no duplicate check; by default we refuse to
        overwrite an existing <appId>.lua (duplicate detection requirement).
      * rollback: on unexpected exception, files already written are removed.
    """
    steam_path = str(steam_base_path or "").strip()
    app_id = require_valid_app_id(str(app_id or "").strip())
    if not steam_path:
        raise ValueError("Steam path is empty")

    dirs = steam_config_dirs(steam_path)
    plugin_folder = dirs["plugin"]
    depot_cache_folder = dirs["depotcache"]
    lua_folder = dirs["lua"]

    existing = plugin_folder / (f"{app_id}.lua")
    if existing.exists() and not force:
        return ApplyResult(
            ok=False,
            error=f"duplicate: {existing} already exists (use force to overwrite)",
        )

    tmp_dir = make_temp_dir("lightningtools")
    try:
        ok, repo, extract_dir = fetcher(app_id, tmp_dir, repos=repos)
        if not ok:
            return ApplyResult(ok=False, error=f"AppID {app_id} not found in any repo")

        if dry_run:
            plan_root = tmp_dir / "_plan"
            planned = process_extracted_repo_files(
                extract_dir, repo or "",
                plan_root / "stplug-in", plan_root / "depotcache", plan_root / "lua",
            )
            mapping = {
                plan_root / "stplug-in": plugin_folder,
                plan_root / "depotcache": depot_cache_folder,
                plan_root / "lua": lua_folder,
            }
            real = []
            for p in planned:
                for shadow, real_dir in mapping.items():
                    if p.parent == shadow:
                        real.append(str(real_dir / p.name))
                        break
            return ApplyResult(ok=True, repo=repo, written=real, error="dry-run")

        for d in dirs.values():
            d.mkdir(parents=True, exist_ok=True)
        journal: list[Path] = []
        try:
            journal = process_extracted_repo_files(
                extract_dir, repo or "", plugin_folder, depot_cache_folder, lua_folder
            )
        except Exception:
            # rollback (extension): remove files written during this operation
            for p in journal:
                try:
                    p.unlink()
                except OSError:
                    pass
            raise
        log.info("added AppID %s via %s -> %d files", app_id, repo, len(journal))
        if db is not None:
            db.record_lightning_op(app_id, repo=repo, written=[str(p) for p in journal], status="ok")
        return ApplyResult(ok=True, repo=repo, written=[str(p) for p in journal])
    except Exception as exc:  # noqa: BLE001 - mirrors original outer catch
        if db is not None:
            db.record_lightning_op(app_id, repo=None, written=[], status="error", error=str(exc))
        return ApplyResult(ok=False, error=str(exc))
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)
