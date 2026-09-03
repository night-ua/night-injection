"""Steam loader installer — port of 'lightningtools:downloadAndInstall' (main.js).

DANGER ZONE — the original writes proxy DLLs (dwmapi.dll, xinput1_4.dll,
OpenSteamTool.dll) into the real Steam folder. This port therefore:
  * defaults to DRY-RUN (reports every action without writing),
  * requires explicit apply=True (CLI: --apply) to write anything,
  * never deletes steam.cfg / hid.dll unless applying.

Evidence (HIGH): LIGHTNING_TOOLS_ZIP_URL + LOADER target file list + cleanup
list + progress stages ('download'/'extract'/'done') are verbatim from main.js.
What OpenSteamTool.dll does internally (Lua 5.5 VM, addappid/setManifestid,
hooks on steamui.dll/steamclient64.dll) was proven by strings analysis —
see tools/analyze_opensteamtool.py output in REPORT.md.
"""
from __future__ import annotations

import logging
import urllib.request
import zipfile
from pathlib import Path

from core.config import (
    LIGHTNING_TOOLS_ZIP_URL,
    LOADER_CLEANUP_FILES,
    LOADER_TARGET_DLLS,
)

log = logging.getLogger("night_injection.installer.loader")


def _download(url: str, dest: Path, timeout: float = 300.0, max_redirects: int = 5) -> None:
    """Original downloadFilePromise(): manual redirect following, browser UA."""
    current = url
    for _ in range(max_redirects + 1):
        req = urllib.request.Request(
            current,
            headers={
                "user-agent": (
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                )
            },
        )
        with urllib.request.urlopen(req, timeout=timeout) as res:
            if res.status in (301, 302):
                location = res.headers.get("Location")
                if not location:
                    raise RuntimeError("Redirección sin location")  # verbatim original error
                current = location
                continue
            if res.status != 200:
                raise RuntimeError(f"HTTP {res.status}")
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(res.read())
            return
    raise RuntimeError("Too many redirects")


def download_and_install(steam_path: str, *, apply: bool = False, nested_target_dir: Path | None = None) -> dict:
    """Original stages: cleanup -> download -> extract outer zip -> find inner
    zip -> extract inner -> copy ONLY the 3 DLLs -> copy stplug-in content to
    config/lua. Progress events ('lightningtools:install-progress') become log
    lines. Returns a report dict; in dry-run nothing is written."""
    report: dict = {"applied": bool(apply), "stages": [], "would_write": [], "deleted": [], "ok": False}
    steam = Path(steam_path)
    if not (steam / "steam.exe").exists():
        report["error"] = "steam.exe not found in target path (verify_steam_path first)"
        return report

    # Stage 0: cleanup of conflicting files (verbatim list)
    for name in LOADER_CLEANUP_FILES:
        target = steam / name
        if apply and target.exists():
            target.unlink()
            report["deleted"].append(str(target))
        elif target.exists():
            report["would_write"].append(f"DELETE {target}")

    if not apply:
        report["stages"].append({"stage": "dry-run", "url": LIGHTNING_TOOLS_ZIP_URL})
        report["would_write"].extend(
            f"COPY {dll} -> {steam / dll}" for dll in LOADER_TARGET_DLLS
        )
        report["stages"].append({"stage": "note", "text": "use apply=True (--apply) to perform"})
        return report

    import tempfile

    tmp_dir = Path(tempfile.mkdtemp(prefix="lightningtools-install-"))
    try:
        zip_path = tmp_dir / "outer.zip"
        log.info("downloading loader bundle (this is the original Dropbox URL)")
        _download(LIGHTNING_TOOLS_ZIP_URL, zip_path)
        report["stages"].append({"stage": "download", "percent": 99})

        outer = tmp_dir / "nivel1"
        outer.mkdir()
        with zipfile.ZipFile(zip_path) as zf:
            zf.extractall(outer)

        inner_zips = sorted(outer.rglob("*.zip"))
        if not inner_zips:
            raise RuntimeError("The loader bundle does not contain the expected inner ZIP archive")

        inner_dir = tmp_dir / "inner"
        inner_dir.mkdir()
        with zipfile.ZipFile(inner_zips[0]) as zf:
            zf.extractall(inner_dir)

        # Original: copy ONLY the 3 target DLLs; error if count != 3
        extracted = 0
        for f in sorted(inner_dir.rglob("*")):
            if f.is_file() and f.name in LOADER_TARGET_DLLS:
                dest = (nested_target_dir or steam) / f.name
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(f.read_bytes())
                report["would_write"].append(f"COPY {dest}")
                extracted += 1
        if extracted != 3:
            raise RuntimeError(f"Expected 3 loader DLLs but found {extracted}")
        report["stages"].append({"stage": "extract", "percent": 80})

        # copy stplug-in content to config/lua (verbatim original behavior)
        plugin = steam / "config" / "stplug-in"
        lua_folder = steam / "config" / "lua"
        lua_folder.mkdir(parents=True, exist_ok=True)
        if plugin.exists():
            for f in sorted(plugin.rglob("*")):
                if f.is_file():
                    shutil_dest = lua_folder / f.name
                    shutil_dest.write_bytes(f.read_bytes())
                    report["would_write"].append(f"COPY {shutil_dest}")

        report["stages"].append({"stage": "done", "percent": 100, "copiedCount": extracted})
        report["ok"] = True
        return report
    finally:
        import shutil as _sh

        _sh.rmtree(tmp_dir, ignore_errors=True)
