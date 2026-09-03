"""Core apply primitives shared by the lua modules (no circular imports).

Ports of lightningtools-ipc.js: writeFilteredLines, processExtractedRepoFiles,
walkFiles + the ApplyResult model. HIGH evidence.
"""
from __future__ import annotations

import shutil
from dataclasses import dataclass, field
from pathlib import Path

from core.config import OTHER_REPO_TAGS, REPO_PLM, REPO_SPIN, SIGNATURE_LINE


# ---------------------------------------------------------------------------
# writeFilteredLines — verbatim port
# ---------------------------------------------------------------------------
def write_filtered_lines(src_text: str, keep_line) -> str:
    """Original: split(/\\r?\\n/), filter by keepLine, then push '' and the
    LightningFast signature line, join with '\\n'."""
    out = []
    for raw_line in str(src_text).splitlines():
        if keep_line(raw_line):
            out.append(raw_line)
    out.append("")
    out.append(SIGNATURE_LINE)
    return "\n".join(out)


def keep_unfiltered(_line: str) -> bool:
    return True


def keep_addappid_only(line: str) -> bool:
    return str(line).lstrip().startswith("addappid(")


def drop_setmanifestid(line: str) -> bool:
    return "setManifestid" not in str(line)


def _read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def walk_files(root: Path) -> list[Path]:
    """Original walkFiles(): recursive, files only, silently skips unreadable dirs."""
    results: list[Path] = []
    if not root.exists():
        return results
    for path in sorted(root.rglob("*")):
        try:
            if path.is_file():
                results.append(path)
        except OSError:
            continue
    return results


@dataclass
class ApplyResult:
    ok: bool
    repo: str | None = None
    written: list[str] = field(default_factory=list)
    error: str | None = None


def process_extracted_repo_files(
    extract_dir: Path,
    repo_url: str,
    plugin_folder: Path,
    depot_cache_folder: Path,
    lua_folder: Path | None,
) -> list[Path]:
    """Original processExtractedRepoFiles — per-repo rules (HIGH evidence):

      * ProjectLightningManifests: .lua copied unfiltered to stplug-in AND lua;
        .manifest copied unfiltered to depotcache.
      * SPIN0ZAi: .lua only — keep lines starting with addappid(, + signature.
      * dvahana2424-web / sojorepo / SteamAutoCracks: .lua only — drop lines
        containing setManifestid, + signature. (.manifest ignored.)
      * everything else: ignored.
    Destination parent folders are created as needed (the original caller
    always mkdirs them first).
    """
    written: list[Path] = []
    repo_tag = str(repo_url)

    for file_path in walk_files(extract_dir):
        file_name = file_path.name
        if not file_name:
            continue
        lower = file_name.lower()
        is_lua = lower.endswith(".lua")
        is_manifest = lower.endswith(".manifest")

        if REPO_PLM in repo_tag and (is_lua or is_manifest):
            destino = plugin_folder / file_name if is_lua else depot_cache_folder / file_name
            destino.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file_path, destino)
            written.append(destino)
            if is_lua and lua_folder:
                dest2 = lua_folder / file_name
                dest2.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(file_path, dest2)
                written.append(dest2)
            continue

        if REPO_SPIN in repo_tag and is_lua:
            destino = plugin_folder / file_name
            destino.parent.mkdir(parents=True, exist_ok=True)
            filtered = write_filtered_lines(_read_text(file_path), keep_addappid_only)
            destino.write_text(filtered, encoding="utf-8")
            written.append(destino)
            if lua_folder:
                dest2 = lua_folder / file_name
                dest2.parent.mkdir(parents=True, exist_ok=True)
                dest2.write_text(filtered, encoding="utf-8")
                written.append(dest2)
            continue

        is_other_repo = any(tag in repo_tag for tag in OTHER_REPO_TAGS)
        if is_other_repo and is_lua:
            destino = plugin_folder / file_name
            destino.parent.mkdir(parents=True, exist_ok=True)
            filtered = write_filtered_lines(_read_text(file_path), drop_setmanifestid)
            destino.write_text(filtered, encoding="utf-8")
            written.append(destino)
            if lua_folder:
                dest2 = lua_folder / file_name
                dest2.parent.mkdir(parents=True, exist_ok=True)
                dest2.write_text(filtered, encoding="utf-8")
                written.append(dest2)
            continue

    return written
