"""Constants extracted verbatim from the original application.

Evidence (HIGH): strings in src/main/lightningtools-ipc.js and src/main/main.js
read directly from resources/app.asar of Project Lightning 5.0.8.
"""
from __future__ import annotations

import os
from pathlib import Path

# ---------------------------------------------------------------------------
# REPOS (verbatim list from lightningtools-ipc.js — order matters: the original
# `for (const repo of REPOS)` loop stops at the first repo that yields a zip)
# ---------------------------------------------------------------------------
REPOS = [
    "https://github.com/LightnigFast/ProjectLightningManifests",
    "https://github.com/SPIN0ZAi/SB_manifest_DB",
    "https://github.com/dvahana2424-web/sojogamesdatabase1",
    "https://github.com/sojorepo/sojogames",
    "https://github.com/SteamAutoCracks/ManifestHub",
]

# Verbatim from lightningtools-ipc.js downloadZipToFile()
HTTP_USER_AGENT = "Project-Lightning"

# Verbatim from lightningtools-ipc.js writeFilteredLines()
SIGNATURE_LINE = "-- Made with love by LightningFast\u26a1\U0001f49c"

# Verbatim from lightningtools-ipc.js processExtractedRepoFiles()
REPO_PLM = "ProjectLightningManifests"     # copy .lua/.manifest unfiltered
REPO_SPIN = "SPIN0ZAi"                     # keep only lines starting with addappid(
OTHER_REPO_TAGS = ("dvahana2424-web", "sojorepo", "SteamAutoCracks")  # strip setManifestid lines

# Verbatim from lightningtools.js getSteamBasePath() candidate list
STEAM_PATH_CANDIDATES = [
    r"C:\Program Files (x86)\Steam",
    r"C:\Program Files\Steam",
    r"D:\Program Files (x86)\Steam",
    r"D:\Program Files\Steam",
    r"C:\Steam",
    r"D:\Steam",
]
# Original falls back to this literal when nothing exists:
STEAM_PATH_FALLBACK = r"C:\Steam2"

# localStorage key used by ajustes.js / lightningtools.js
STEAM_PATH_STORAGE_KEY = "lightningtools-steam-path"

# Verbatim from main.js (lightningtools:downloadAndInstall)
LIGHTNING_TOOLS_ZIP_URL = (
    "https://www.dropbox.com/scl/fo/kd6qy4kca8qgx679o2g18/"
    "AJD_YjPCPRyLsMUCKZTkcfE?rlkey=tkdu1ytkp23ml7ibbkzrcekm8&st=kq2z32bt&dl=1"
)
LOADER_TARGET_DLLS = ["dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll"]
LOADER_CLEANUP_FILES = ["steam.cfg", "hid.dll"]

# Verbatim from lightningtools.js findImageRecursive()
COVER_NAME_PATTERNS = ["library_600x900", "library_capsule"]
COVER_EXTENSIONS = [".jpg", ".jpeg", ".png", ".webp", ".bmp"]

# Verbatim from lightningtools.js loadSteamLibrary() fallback URL
COVER_FALLBACK_URL = (
    "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{app_id}/library_600x900.jpg"
)

# Verbatim from lightningtools.js fetchGameTitleFromSteam()
APPDETAILS_URL = "https://store.steampowered.com/api/appdetails?appids={app_id}&l=spanish"
TITLE_FALLBACK = "Game {app_id}"

# Verbatim from biblioteca-ipc.js (fastly "blue button" official Steam assets)
STEAM_ASSETS_URLS = {
    "hero": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{steam_id}/library_hero.jpg",
    "cover": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{steam_id}/library_600x900_2x.jpg",
    "capsule": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{steam_id}/library_capsule.jpg",
    "header": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{steam_id}/header.jpg",
    "logo": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{steam_id}/logo.png",
}

# Verbatim from nexus-api.js
NEXUS_API_URLS = [
    "https://nexus-images.pages.dev/api",
    "https://nexus-worker-mirror.pages.dev/api",
    "https://nexus-worker-mirror.pages.dev",
]

PROJECT_ROOT = Path(__file__).resolve().parent.parent


def runtime_logs_dir() -> Path:
    """Return the writable per-user log directory."""
    from core.paths import user_data_dir

    return user_data_dir() / "logs"


LOGS_DIR = runtime_logs_dir()


def default_appdata_dir() -> Path:
    """Mirror of Electron app.getPath('userData') for productName
    'project-lightning-data' (observed: %APPDATA%\\project-lightning-data)."""
    base = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    return Path(base) / "project-lightning-data"


def temp_subdir(prefix: str, unique_id: str) -> Path:
    """Original: path.join(os.tmpdir(), `${prefix}-${id}`) with 8 random hex bytes."""
    return Path(os.path.abspath(os.path.join(os.environ.get("TEMP", os.path.abspath(".")), f"{prefix}-{unique_id}")))
