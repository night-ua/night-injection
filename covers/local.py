"""Local cover resolution — port of findImageRecursive() usage in
loadSteamLibrary(): scan <steam>/appcache/librarycache/<appId> for
library_600x900 / library_capsule images. See steam.library (shared impl)."""
from __future__ import annotations

from steam.library import find_image_recursive, to_file_url  # re-export for symmetry

__all__ = ["find_image_recursive", "to_file_url"]
