"""import_dropped_files - verbatim port of importDroppedFiles().

This is the "inject a lua file" path of the original app: the user provides
.lua / .manifest / .zip files and the app copies them into the Steam config
folders (no AppID, no download):
    .lua      -> config/stplug-in/<name>  AND  config/lua/<name>
    .manifest -> config/depotcache/<name>
    .zip      -> extracted; inner .lua/.manifest distributed the same way
Destination name = path.basename(filePath || name)  (verbatim original).
"""
from __future__ import annotations

import logging
import shutil
from pathlib import Path

from lua.base import walk_files as _walk_files
from lua.fetch import extract_zip, make_temp_dir
from steam.discovery import ensure_steam_dirs

log = logging.getLogger("night_injection.lua.importer")


def import_dropped_files(steam_base_path: str, files: list[dict], *, dry_run: bool = False) -> dict:
    """Original importDroppedFiles({steamBasePath, files}) - HIGH evidence.

    Each file dict: {'path': str|None, 'name': str, 'buffer': bytes|None}.
    dry_run (documented extension): computes the exact destinations without
    writing anything; zips are still extracted to %TEMP% for planning.

    Returns the original result shape plus 'planned' (dry-run) or
    'destinations' (applied).
    """
    steam_path = str(steam_base_path or "").strip()
    if not steam_path:
        raise ValueError("Steam path is empty")
    if not (Path(steam_path) / "steam.exe").is_file():
        raise ValueError("The selected Steam path is invalid (steam.exe was not found)")
    if not files:
        return {"ok": False, "error": "No files were provided."}

    dirs = ensure_steam_dirs(steam_path)
    plugin_folder, depot_cache_folder, lua_folder = dirs["plugin"], dirs["depotcache"], dirs["lua"]

    imported_count = 0
    ignored_count = 0
    errors: list[str] = []
    touched: list[str] = []

    def _place(src: Path | None, buffer: bytes | None, dest: Path) -> None:
        if dry_run:
            touched.append(str(dest))
            return
        dest.parent.mkdir(parents=True, exist_ok=True)
        if src is not None:
            shutil.copyfile(src, dest)
        else:
            dest.write_bytes(buffer or b"")
        touched.append(str(dest))

    for file_info in files:
        if not isinstance(file_info, dict):
            continue
        file_path = file_info.get("path") if isinstance(file_info.get("path"), str) else None
        name = file_info.get("name") or (Path(file_path).name if file_path else "")
        file_name = Path(file_path or name).name  # verbatim original naming
        ext = Path(file_path or file_name).suffix.lower()
        buffer = file_info.get("buffer")
        buffer = bytes(buffer) if buffer else None

        try:
            if ext == ".lua":
                if file_path:
                    _place(Path(file_path), None, plugin_folder / file_name)
                    _place(Path(file_path), None, lua_folder / file_name)
                elif buffer is not None:
                    _place(None, buffer, plugin_folder / file_name)
                    _place(None, buffer, lua_folder / file_name)
                else:
                    raise ValueError("No data is available for the Lua file")
                imported_count += 1
            elif ext == ".manifest":
                if file_path:
                    _place(Path(file_path), None, depot_cache_folder / file_name)
                elif buffer is not None:
                    _place(None, buffer, depot_cache_folder / file_name)
                else:
                    raise ValueError("No data is available for the manifest file")
                imported_count += 1
            elif ext == ".zip":
                tmp_dir = make_temp_dir("lightningtools")
                extract_dir = tmp_dir / "extract"
                tmp_dir.mkdir(parents=True, exist_ok=True)
                extract_dir.mkdir(parents=True, exist_ok=True)
                try:
                    zip_path = Path(file_path) if file_path else tmp_dir / file_name
                    if not file_path:
                        if buffer is None:
                            raise ValueError("No data is available for the ZIP archive")
                        zip_path.write_bytes(buffer)
                    has_lua = False
                    has_manifest = False
                    extract_zip(zip_path, extract_dir)
                    for extracted_path in _walk_files(extract_dir):
                        entry_ext = extracted_path.suffix.lower()
                        entry_name = extracted_path.name
                        if entry_ext == ".lua":
                            _place(extracted_path, None, plugin_folder / entry_name)
                            _place(extracted_path, None, lua_folder / entry_name)
                            has_lua = True
                        elif entry_ext == ".manifest":
                            _place(extracted_path, None, depot_cache_folder / entry_name)
                            has_manifest = True
                    if has_lua:
                        imported_count += 1
                    if has_manifest:
                        imported_count += 1
                    if not has_lua and not has_manifest:
                        ignored_count += 1
                        errors.append(f"ZIP archive {file_name} contains no Lua or manifest files")
                finally:
                    shutil.rmtree(tmp_dir, ignore_errors=True)
            else:
                ignored_count += 1
        except Exception as exc:  # noqa: BLE001 - mirrors original error collection
            errors.append(f"{file_name}: {exc}")

    result: dict = {
        "ok": len(errors) == 0,
        "importedCount": imported_count,
        "ignoredCount": ignored_count,
        "errors": errors[:6],
    }
    if dry_run:
        result["planned"] = touched
        result["dry_run"] = True
    else:
        result["destinations"] = touched
    return result
