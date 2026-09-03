"""Network fetch of per-AppID zip bundles from the evidence-backed repos.

Python equivalent of downloadZipToFile() + the repo loop in
buscarYAgregarAppId() [src/main/lightningtools-ipc.js] — HIGH evidence.

ZIP URL format (verbatim): f"{repo}/archive/refs/heads/{app_id}.zip"
User-Agent (verbatim): "Project-Lightning"
"""
from __future__ import annotations

import logging
import tempfile
import urllib.request
import uuid
import zipfile
from pathlib import Path

from core.config import HTTP_USER_AGENT, REPOS

log = logging.getLogger("night_injection.lua.fetch")


def make_temp_dir(prefix: str = "lightningtools") -> Path:
    """Original makeTempDir(): os.tmpdir()/<prefix>-<16 hex chars>."""
    unique = uuid.uuid4().hex  # crypto.randomBytes(8).toString('hex') == 16 hex
    return Path(tempfile.gettempdir()) / f"{prefix}-{unique}"


def repo_zip_url(repo: str, app_id: str) -> str:
    return f"{repo}/archive/refs/heads/{app_id}.zip"


def download_zip_to_file(url: str, out_path: Path, timeout: float = 30.0,
                         max_bytes: int = 100 * 1024 * 1024) -> bool:
    """Original downloadZipToFile(): fetch(redirect follow, UA Project-Lightning,
    accept zip/octet-stream). Returns False on any failure (never raises)."""
    req = urllib.request.Request(
        url,
        headers={
            "user-agent": HTTP_USER_AGENT,
            "accept": "application/zip,application/octet-stream,*/*",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as res:
            if res.status != 200:
                return False
            data = res.read(max_bytes + 1)
            if len(data) > max_bytes:
                log.warning("download rejected because it exceeds %d bytes: %s", max_bytes, url)
                return False
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_bytes(data)
        return True
    except Exception as exc:  # noqa: BLE001 — original swallows all errors
        log.debug("download failed %s: %s", url, exc)
        return False


def extract_zip(zip_path: Path, dest_dir: Path) -> None:
    """Extract a ZIP while rejecting entries that escape the target folder."""
    dest_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path) as zf:
        root = dest_dir.resolve()
        for member in zf.infolist():
            target = (dest_dir / member.filename).resolve()
            try:
                target.relative_to(root)
            except ValueError as exc:
                raise ValueError(f"Unsafe ZIP entry: {member.filename}") from exc
        zf.extractall(dest_dir)


def fetch_appid_bundle(
    app_id: str,
    tmp_dir: Path,
    repos: list[str] | None = None,
    downloader=download_zip_to_file,
) -> tuple[bool, str | None, Path]:
    """Iterate repos in order, download <app_id>.zip, extract to
    <tmp_dir>/extract. Returns (ok, repo_or_None, extract_dir).

    Mirrors the original loop: first successful repo wins; failed downloads are
    cleaned up and the loop continues; on total failure returns (False, None, _).
    """
    extract_dir = tmp_dir / "extract"
    for repo in (repos or REPOS):
        zip_url = repo_zip_url(repo, app_id)
        zip_path = tmp_dir / f"{app_id}.zip"
        try:
            tmp_dir.mkdir(parents=True, exist_ok=True)
            extract_dir.mkdir(parents=True, exist_ok=True)
            ok = downloader(zip_url, zip_path)
            if not ok:
                _rm_rf(tmp_dir)
                tmp_dir.mkdir(parents=True, exist_ok=True)
                extract_dir.mkdir(parents=True, exist_ok=True)
                continue
            extract_zip(zip_path, extract_dir)
            return True, repo, extract_dir
        except Exception as exc:  # noqa: BLE001 — original: catch {} continue
            log.debug("repo %s failed for %s: %s", repo, app_id, exc)
            _rm_rf(tmp_dir)
            tmp_dir.mkdir(parents=True, exist_ok=True)
            extract_dir.mkdir(parents=True, exist_ok=True)
            continue
    return False, None, extract_dir


def _rm_rf(path: Path) -> None:
    import shutil

    shutil.rmtree(path, ignore_errors=True)
