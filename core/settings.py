"""Safe JSON-backed application settings."""
from __future__ import annotations

import json
import logging
import os
from dataclasses import asdict, dataclass, fields
from pathlib import Path

from core.paths import user_data_dir

log = logging.getLogger("night_injection.settings")


@dataclass(slots=True)
class AppSettings:
    steam_path: str = ""
    cache_directory: str = ""
    animations_enabled: bool = True
    auto_scroll_logs: bool = True
    remember_window_size: bool = True
    window_geometry: str = "1280x800"
    last_section: str = "Dashboard"
    debug_logging: bool = False
    fetch_metadata: bool = True


class SettingsStore:
    def __init__(self, path: str | Path | None = None):
        self.path = Path(path) if path else user_data_dir() / "settings.json"

    def load(self) -> AppSettings:
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
            allowed = {field.name for field in fields(AppSettings)}
            return AppSettings(**{key: value for key, value in raw.items() if key in allowed})
        except FileNotFoundError:
            return AppSettings()
        except (OSError, ValueError, TypeError) as exc:
            log.warning("Could not load settings from %s: %s", self.path, exc)
            return AppSettings()

    def save(self, settings: AppSettings) -> None:
        """Write atomically so an interrupted save cannot corrupt settings."""
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".tmp")
        try:
            temporary.write_text(
                json.dumps(asdict(settings), indent=2, ensure_ascii=False), encoding="utf-8"
            )
            os.replace(temporary, self.path)
        except OSError as exc:
            log.error("Could not save settings to %s: %s", self.path, exc)
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass
