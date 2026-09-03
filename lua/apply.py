"""Public surface of the apply layer (name parity with lightningtools-ipc.js).

Implementation lives in lua.base (primitives) and lua.add / lua.remove /
lua.importer (operations). No circular imports.
"""
from __future__ import annotations

from lua.add import add_app_id
from lua.base import (
    ApplyResult,
    drop_setmanifestid,
    keep_addappid_only,
    keep_unfiltered,
    process_extracted_repo_files,
    walk_files,
    write_filtered_lines,
)
from lua.importer import import_dropped_files
from lua.remove import clear_plugins, remove_app_id

__all__ = [
    "ApplyResult",
    "add_app_id",
    "clear_plugins",
    "drop_setmanifestid",
    "import_dropped_files",
    "keep_addappid_only",
    "keep_unfiltered",
    "process_extracted_repo_files",
    "remove_app_id",
    "walk_files",
    "write_filtered_lines",
]
