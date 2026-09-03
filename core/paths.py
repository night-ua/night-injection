"""Runtime paths that work from source and from a frozen Windows build."""
from __future__ import annotations

import os
import sys
from pathlib import Path

APP_NAME = "night-injection"


def resource_path(relative: str | Path) -> Path:
    """Return a bundled asset path for source and PyInstaller runtimes."""
    bundle_root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    return bundle_root / Path(relative)


def user_data_dir() -> Path:
    """Return the per-user writable data directory without touching it."""
    base = os.environ.get("LOCALAPPDATA") or os.environ.get("APPDATA")
    if base:
        return Path(base) / APP_NAME
    return Path.home() / f".{APP_NAME}"


def ensure_runtime_dirs() -> dict[str, Path]:
    root = user_data_dir()
    paths = {
        "root": root,
        "logs": root / "logs",
        "cache": root / "cache",
        "covers": root / "cache" / "covers",
    }
    for path in paths.values():
        path.mkdir(parents=True, exist_ok=True)
    return paths
