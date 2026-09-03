"""Modern desktop controller for night-injection."""
from __future__ import annotations

import json
import logging
import queue
import re
import sys
import threading
import tkinter as tk
import traceback
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from tkinter import filedialog, messagebox
from typing import ClassVar

import customtkinter as ctk
from PIL import Image, ImageOps, ImageTk

from core.logging_setup import setup_logging
from core.paths import APP_NAME, ensure_runtime_dirs, resource_path
from core.settings import SettingsStore
from covers.cache import SteamAssetCache
from services.lightning_service import LightningService
from steam.discovery import get_steam_base_path, verify_steam_path
from storage.database import Database
from ui import theme
from ui.pages import (
    DashboardPage,
    HistoryPage,
    InjectPage,
    LibraryPage,
    LogsPage,
    SettingsPage,
)
from ui.widgets import StatusPill, ToastManager

try:
    from tkinterdnd2 import DND_FILES, TkinterDnD

    DND_AVAILABLE = True
except ImportError:
    DND_FILES = "DND_Files"
    TkinterDnD = None
    DND_AVAILABLE = False


if DND_AVAILABLE:
    class NightInjectionRoot(ctk.CTk, TkinterDnD.DnDWrapper):
        """CustomTkinter window enhanced with the tkdnd command set."""

        def __init__(self):
            ctk.CTk.__init__(self)
            self.TkdndVersion = TkinterDnD._require(self)
else:
    NightInjectionRoot = ctk.CTk


class QueueLogHandler(logging.Handler):
    """Forward formatted records to the main-thread event queue."""

    def __init__(self, events: queue.Queue):
        super().__init__()
        self.events = events

    def emit(self, record: logging.LogRecord) -> None:
        try:
            from datetime import datetime, timezone

            timestamp = datetime.fromtimestamp(record.created, tz=timezone.utc).astimezone().strftime("%H:%M:%S")
            self.events.put(("log", record.levelname, record.getMessage(), timestamp))
        except Exception:  # noqa: BLE001
            self.handleError(record)


class NightInjectionApp:
    SUPPORTED_EXTENSIONS: ClassVar[frozenset[str]] = frozenset({".lua", ".manifest", ".zip"})
    NAV_ITEMS = (
        ("Dashboard", "⌂"),
        ("Library", "▦"),
        ("Inject", "↯"),
        ("History", "◷"),
        ("Settings", "⚙"),
        ("Logs", "≡"),
    )

    def __init__(self, root: ctk.CTk):
        self.root = root
        self.runtime = ensure_runtime_dirs()
        self.settings_store = SettingsStore()
        self.settings = self.settings_store.load()
        if not self.settings.cache_directory:
            self.settings.cache_directory = str(self.runtime["covers"])
        if not self.settings.steam_path:
            self.settings.steam_path = get_steam_base_path()

        self.events: queue.Queue = queue.Queue()
        self.active_jobs: set[str] = set()
        self.selected_files: list[Path] = []
        self.library_games: list[dict] = []
        self.history_rows: list[dict] = []
        self.library_enriched = False
        self.current_page_name: str | None = None
        self.nav_buttons: dict[str, ctk.CTkButton] = {}
        self.pages: dict[str, ctk.CTkFrame] = {}

        self.logger = setup_logging(self.settings.debug_logging)
        self.log_handler = QueueLogHandler(self.events)
        logging.getLogger("night_injection").addHandler(self.log_handler)
        self.db = Database(self.runtime["root"] / "night-injection.db")
        self.service = LightningService(self.settings.steam_path, self.db)
        self.asset_cache = SteamAssetCache(self.settings.cache_directory)

        self._configure_window()
        self._build_shell()
        self.toasts = ToastManager(root)
        self._enable_drag_and_drop()
        self.navigate(self.settings.last_section if self.settings.last_section in self.pages else "Dashboard")
        self.root.protocol("WM_DELETE_WINDOW", self.close)
        self.root.after(80, self._poll_events)
        self.root.after(180, self.verify_current_path)
        self.root.after(320, self.refresh_history)
        self.root.after(
            500,
            lambda: self.refresh_library(enrich=False)
            if "library" not in self.active_jobs and not self.library_enriched
            else None,
        )
        self.logger.info("night-injection started")

    def _configure_window(self) -> None:
        self.root.title(APP_NAME)
        screen_width = self.root.winfo_screenwidth()
        screen_height = self.root.winfo_screenheight()
        default_width = min(1280, max(980, int(screen_width * 0.84)))
        default_height = min(800, max(640, int(screen_height * 0.82)))
        width, height = default_width, default_height
        if self.settings.remember_window_size:
            match = re.match(r"^(\d+)x(\d+)", self.settings.window_geometry)
            if match:
                width = min(int(match.group(1)), screen_width - 60)
                height = min(int(match.group(2)), screen_height - 80)
        width = max(min(980, screen_width - 60), width)
        height = max(min(640, screen_height - 80), height)
        x = max(0, (screen_width - width) // 2)
        y = max(0, (screen_height - height) // 2)
        self.root.geometry(f"{width}x{height}+{x}+{y}")
        self.root.minsize(min(980, screen_width - 60), min(640, screen_height - 80))
        self.root.configure(fg_color=theme.BG)
        try:
            icon = Image.open(resource_path(Path("assets") / "logo.jpg"))
            icon = ImageOps.fit(icon, (64, 64), centering=(0.5, 0.5))
            self.window_icon = ImageTk.PhotoImage(icon)
            self.root.iconphoto(True, self.window_icon)
        except (OSError, ValueError) as exc:
            self.logger.warning("Could not load the application icon: %s", exc)
        if sys.platform == "win32":
            try:
                import ctypes

                ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("night-injection.desktop.1")
            except (AttributeError, OSError):
                pass

    def _build_shell(self) -> None:
        self.root.grid_columnconfigure(1, weight=1)
        self.root.grid_rowconfigure(0, weight=1)
        sidebar = ctk.CTkFrame(self.root, width=230, corner_radius=0, fg_color=theme.SIDEBAR)
        sidebar.grid(row=0, column=0, sticky="nsw")
        sidebar.grid_propagate(False)
        sidebar.grid_columnconfigure(0, weight=1)
        sidebar.grid_rowconfigure(8, weight=1)

        brand = ctk.CTkFrame(sidebar, fg_color="transparent")
        brand.grid(row=0, column=0, sticky="ew", padx=18, pady=(22, 26))
        try:
            logo = Image.open(resource_path(Path("assets") / "logo.jpg")).convert("RGB")
            logo = ImageOps.fit(logo, (38, 38), centering=(0.5, 0.5))
            self.brand_image = ctk.CTkImage(light_image=logo, dark_image=logo, size=(38, 38))
            ctk.CTkLabel(brand, text="", image=self.brand_image).pack(side="left")
        except (OSError, ValueError):
            ctk.CTkLabel(brand, text="↯", text_color=theme.ACCENT,
                         font=(theme.FONT, 24, "bold")).pack(side="left")
        name = ctk.CTkFrame(brand, fg_color="transparent")
        name.pack(side="left", padx=(10, 0))
        ctk.CTkLabel(name, text="night-injection", font=(theme.FONT, 15, "bold"),
                     text_color=theme.TEXT).pack(anchor="w")
        ctk.CTkLabel(name, text="STEAM TOOLKIT", font=(theme.FONT, 8, "bold"),
                     text_color=theme.ACCENT).pack(anchor="w")

        for index, (label, icon) in enumerate(self.NAV_ITEMS, start=1):
            button = ctk.CTkButton(
                sidebar, text=f"{icon}    {label}", command=lambda value=label: self.navigate(value),
                anchor="w", height=42, corner_radius=10, fg_color="transparent",
                hover_color=theme.SURFACE_ALT, text_color=theme.TEXT_SECONDARY,
                font=(theme.FONT, 12, "bold")
            )
            button.grid(row=index, column=0, sticky="ew", padx=12, pady=3)
            self.nav_buttons[label] = button

        status_box = ctk.CTkFrame(sidebar, fg_color=theme.SURFACE, border_width=1,
                                  border_color=theme.BORDER, corner_radius=12)
        status_box.grid(row=9, column=0, sticky="ew", padx=14, pady=(12, 18))
        ctk.CTkLabel(status_box, text="SYSTEM", font=theme.CAPTION,
                     text_color=theme.TEXT_MUTED).pack(anchor="w", padx=13, pady=(11, 3))
        self.sidebar_status = StatusPill(status_box, "Checking Steam", theme.WARNING)
        self.sidebar_status.pack(anchor="w", padx=5, pady=(0, 7))

        self.content = ctk.CTkFrame(self.root, fg_color=theme.BG, corner_radius=0)
        self.content.grid(row=0, column=1, sticky="nsew")
        self.content.grid_columnconfigure(0, weight=1)
        self.content.grid_rowconfigure(0, weight=1)
        self.pages = {
            "Dashboard": DashboardPage(self.content, self),
            "Library": LibraryPage(self.content, self),
            "Inject": InjectPage(self.content, self),
            "History": HistoryPage(self.content, self),
            "Settings": SettingsPage(self.content, self, self.settings),
            "Logs": LogsPage(self.content, self),
        }
        for page in self.pages.values():
            page.grid(row=0, column=0, sticky="nsew")
        self.pages["Logs"].viewer.auto_scroll.set(self.settings.auto_scroll_logs)

    def _enable_drag_and_drop(self) -> None:
        if not DND_AVAILABLE:
            self.logger.warning("Native drag and drop is unavailable; file browsing remains enabled")
            return
        enabled = False
        for name in ("Dashboard", "Inject"):
            enabled = self.pages[name].enable_drop(DND_FILES) or enabled
        if enabled:
            self.logger.info("Native drag and drop enabled")
        else:
            self.logger.warning("Native drag and drop could not be registered")

    def navigate(self, name: str) -> None:
        page = self.pages.get(name)
        if page is None:
            return
        previous = self.pages.get(self.current_page_name or "")
        if previous is not None and hasattr(previous, "on_hide"):
            previous.on_hide()
        page.tkraise()
        self.current_page_name = name
        if hasattr(page, "on_show"):
            page.on_show()
        for label, button in self.nav_buttons.items():
            selected = label == name
            button.configure(
                fg_color=theme.ACCENT_DARK if selected else "transparent",
                text_color=theme.ACCENT if selected else theme.TEXT_SECONDARY,
                border_width=1 if selected else 0,
                border_color="#733607" if selected else theme.SIDEBAR,
            )
        self.settings.last_section = name
        if name == "Library" and not self.library_enriched and "library" not in self.active_jobs:
            self.root.after(20, self.refresh_library)

    def submit(self, key: str, function, on_done=None, busy_text: str | None = None) -> bool:
        if key in self.active_jobs:
            self.toasts.show("That operation is already running.", "warning")
            return False
        self.active_jobs.add(key)
        if busy_text:
            self.pages["Inject"].set_busy(True, busy_text, self.settings.animations_enabled)

        def worker():
            try:
                result = function()
                self.events.put(("result", key, on_done, result, None))
            except Exception as exc:  # noqa: BLE001
                self.logger.debug("Background task %s failed:\n%s", key, traceback.format_exc())
                self.events.put(("result", key, on_done, None, exc))

        threading.Thread(target=worker, daemon=True, name=f"night-injection-{key}").start()
        return True

    def _poll_events(self) -> None:
        try:
            while True:
                event = self.events.get_nowait()
                if event[0] == "log":
                    _, level, message, timestamp = event
                    self.pages["Logs"].viewer.append(level, message, timestamp)
                elif event[0] == "result":
                    _, key, callback, result, error = event
                    self.active_jobs.discard(key)
                    if key == "inject":
                        self.pages["Inject"].set_busy(False, "Ready")
                    if error:
                        self.logger.error("%s failed: %s", key.replace("_", " ").title(), error)
                        self.toasts.show(self._human_error(error), "error", 5200)
                    elif callback:
                        callback(result)
        except queue.Empty:
            pass
        if self.root.winfo_exists():
            self.root.after(80, self._poll_events)

    @staticmethod
    def _human_error(error: Exception) -> str:
        if isinstance(error, PermissionError):
            return "Permission was denied. Close Steam or run with suitable access."
        text = str(error).strip()
        return text if text else "The operation could not be completed. See Logs for details."

    def choose_files(self) -> None:
        paths = filedialog.askopenfilenames(
            parent=self.root, title="Select Lua, manifest, or ZIP files",
            filetypes=[("Supported files", "*.lua *.manifest *.zip"), ("Lua files", "*.lua"),
                       ("Manifest files", "*.manifest"), ("ZIP archives", "*.zip")]
        )
        if paths:
            self.add_files([Path(path) for path in paths])

    def handle_drop_data(self, data: str) -> None:
        try:
            items = self.root.tk.splitlist(data)
        except tk.TclError:
            items = (data,)
        self.add_files([Path(str(item).strip().strip("{}").strip('"')) for item in items])

    def add_files(self, paths: list[Path]) -> None:
        added = 0
        rejected: list[str] = []
        known = {str(path.resolve()).casefold() for path in self.selected_files if path.exists()}
        for path in paths:
            try:
                resolved = path.resolve(strict=True)
            except OSError:
                rejected.append(f"{path.name or path}: not found")
                continue
            if not resolved.is_file():
                rejected.append(f"{resolved.name}: not a file")
                continue
            if resolved.suffix.lower() not in self.SUPPORTED_EXTENSIONS:
                rejected.append(f"{resolved.name}: unsupported type")
                continue
            key = str(resolved).casefold()
            if key in known:
                continue
            self.selected_files.append(resolved)
            known.add(key)
            added += 1
        self._sync_selected_files()
        if added:
            self.logger.info("Added %d file(s) to the injection queue", added)
            self.toasts.show(f"{added} file{'s' if added != 1 else ''} ready to inject.", "success")
        if rejected:
            self.logger.warning("Rejected dropped files: %s", "; ".join(rejected))
            self.toasts.show(f"Rejected {len(rejected)} unsupported or missing file(s).", "warning")

    def remove_selected_file(self, path: Path) -> None:
        self.selected_files = [item for item in self.selected_files if item != path]
        self._sync_selected_files()

    def clear_selected_files(self) -> None:
        self.selected_files.clear()
        self._sync_selected_files()

    def _sync_selected_files(self) -> None:
        self.pages["Inject"].set_files(self.selected_files)
        self._update_dashboard()

    def _steam_is_valid(self) -> bool:
        return bool(verify_steam_path(self.settings.steam_path).get("valid"))

    def inject_selected_files(self) -> None:
        if not self.selected_files:
            self.toasts.show("Add at least one supported file first.", "warning")
            return
        if not self._steam_is_valid():
            self.toasts.show("Select and verify a valid Steam installation first.", "error")
            self.navigate("Settings")
            return
        dry_run = self.pages["Inject"].dry_run.get()
        if not dry_run and not messagebox.askyesno(
            APP_NAME, f"Inject {len(self.selected_files)} selected file(s) into Steam?\n\nTarget: {self.settings.steam_path}",
            parent=self.root
        ):
            return
        payload = [{"path": str(path), "name": path.name, "buffer": None} for path in self.selected_files]
        self.logger.info("Validating %d selected file(s)", len(payload))
        self.submit("inject", lambda: self.service.inject_files(payload, dry_run=dry_run),
                    self._after_inject, "Processing selected files…")

    def _after_inject(self, result: dict) -> None:
        if result.get("ok"):
            mode = "Dry-run completed" if result.get("dry_run") else "Injection completed successfully"
            destinations = result.get("planned") or result.get("destinations") or []
            self.logger.info("%s: %d destination(s)", mode, len(destinations))
            for destination in destinations:
                self.logger.debug("Destination: %s", destination)
            self.toasts.show(mode + ".", "success")
            if not result.get("dry_run"):
                self.selected_files.clear()
                self._sync_selected_files()
                self.refresh_history()
                self.refresh_library()
        else:
            errors = result.get("errors") or [result.get("error") or "Unknown validation error"]
            self.logger.error("Injection rejected: %s", "; ".join(map(str, errors)))
            self.toasts.show(str(errors[0]), "error")

    def verify_current_path(self) -> None:
        self._apply_path_status(verify_steam_path(self.settings.steam_path))

    def _apply_path_status(self, result: dict) -> None:
        valid = bool(result.get("valid"))
        label = "Steam detected" if valid else "Steam not found"
        color = theme.SUCCESS if valid else theme.ERROR
        self.sidebar_status.set(label, color)
        self.pages["Settings"].steam_status.set(label, color)
        self._update_dashboard()

    def refresh_library(self, enrich: bool = True) -> None:
        if not self._steam_is_valid():
            self.library_games = []
            self.pages["Library"].set_games([])
            self._update_dashboard()
            return

        def job():
            games = self.service.build_library(fetch_titles=False)

            def enrich(game: dict) -> dict:
                app_id = str(game["app_id"])
                if enrich and self.settings.fetch_metadata:
                    game["title"] = self.asset_cache.resolve_metadata(app_id).get("name", game["title"])
                    game["cover_path"] = str(self.asset_cache.get_cover(app_id, game.get("img_src")))
                else:
                    local = self.asset_cache._local_path(game.get("img_src"))
                    game["cover_path"] = str(local if local and local.exists() else self.asset_cache.placeholder_path)
                    game["title"] = self.asset_cache.cached_title(app_id) or f"Game {app_id}"
                return game

            with ThreadPoolExecutor(max_workers=6) as pool:
                return list(pool.map(enrich, games))

        self.submit("library", job, lambda games: self._after_library(games, enrich))

    def _after_library(self, games: list[dict], enriched: bool = True) -> None:
        by_app_id = {str(row.get("app_id")): row for row in self.history_rows}
        for game in games:
            history = by_app_id.get(str(game.get("app_id")))
            if history:
                game["last_operation"] = history.get("status")
        self.library_games = games
        self.library_enriched = self.library_enriched or enriched
        self.pages["Library"].set_games(games)
        self._update_dashboard()
        self.logger.info("Library loaded: %d game(s)", len(games))
        if self.current_page_name == "Library" and not self.library_enriched:
            self.root.after(20, self.refresh_library)

    def remove_game(self, app_id: str) -> None:
        if not messagebox.askyesno(APP_NAME, f"Remove the generated files for AppID {app_id}?", parent=self.root):
            return
        self.submit("remove", lambda: self.service.remove_app_id(app_id), self._after_remove)

    def _after_remove(self, result) -> None:
        self.logger.info("Removed %d file(s)", len(result.written))
        self.toasts.show("Game files removed.", "success")
        self.refresh_history()
        self.refresh_library()

    def refresh_history(self) -> None:
        self.submit("history", self.db.list_lightning_history, self._after_history)

    def _after_history(self, rows: list[dict]) -> None:
        self.history_rows = rows
        self.pages["History"].set_rows(rows)
        self.pages["Dashboard"].set_activity(rows)
        by_app_id = {str(row.get("app_id")): row for row in rows}
        for game in self.library_games:
            history = by_app_id.get(str(game.get("app_id")))
            if history:
                game["last_operation"] = history.get("status")
        if self.library_games:
            self.pages["Library"].set_games(self.library_games)
        self._update_dashboard()

    def _update_dashboard(self) -> None:
        self.pages["Dashboard"].update_summary(
            self._steam_is_valid(), self.settings.steam_path, len(self.library_games),
            len(self.selected_files), len(self.history_rows)
        )

    def browse_steam_path(self) -> None:
        path = filedialog.askdirectory(parent=self.root, title="Select the Steam installation directory")
        if path:
            self.pages["Settings"].steam_path.set(path)
            self.verify_settings_path()

    def detect_steam_path(self) -> None:
        path = get_steam_base_path()
        self.pages["Settings"].steam_path.set(path)
        self.verify_settings_path()

    def verify_settings_path(self) -> None:
        path = self.pages["Settings"].steam_path.get().strip()
        result = verify_steam_path(path)
        if result.get("valid"):
            self.settings.steam_path = path
            self.service = LightningService(path, self.db)
            self.library_enriched = False
            self._apply_path_status(result)
            self.toasts.show("Steam installation verified.", "success")
            self.refresh_library()
        else:
            self._apply_path_status(result)
            self.toasts.show("Invalid Steam path: steam.exe was not found.", "error")

    def save_settings(self) -> None:
        page: SettingsPage = self.pages["Settings"]
        old_cache = self.settings.cache_directory
        self.settings.steam_path = page.steam_path.get().strip()
        self.settings.cache_directory = page.cache_path.get().strip() or str(self.runtime["covers"])
        self.settings.animations_enabled = page.animations.get()
        self.settings.auto_scroll_logs = page.auto_scroll.get()
        self.settings.remember_window_size = page.remember_window.get()
        self.settings.debug_logging = page.debug.get()
        self.settings.fetch_metadata = page.fetch_metadata.get()
        self.pages["Logs"].viewer.auto_scroll.set(self.settings.auto_scroll_logs)
        if old_cache != self.settings.cache_directory:
            try:
                self.asset_cache = SteamAssetCache(self.settings.cache_directory)
            except OSError as exc:
                self.toasts.show(f"Cache directory is not writable: {exc}", "error")
                return
        self.settings_store.save(self.settings)
        self.service = LightningService(self.settings.steam_path, self.db)
        self.library_enriched = False
        self.verify_current_path()
        self.toasts.show("Settings saved.", "success")
        self.logger.info("Application settings updated")

    def clear_cache(self) -> None:
        self.submit("clear_cache", self.asset_cache.clear, self._after_clear_cache)

    def _after_clear_cache(self, count: int) -> None:
        self.toasts.show(f"Removed {count} cached artwork file(s).", "success")
        self.logger.info("Artwork cache cleared: %d file(s)", count)
        self.refresh_library()

    def plan_loader(self) -> None:
        from installer.loader import download_and_install

        self.submit("loader_plan", lambda: download_and_install(self.settings.steam_path, apply=False),
                    self._after_loader_plan)

    def _after_loader_plan(self, report: dict) -> None:
        self.logger.info("Loader dry-run plan:\n%s", json.dumps(report, indent=2))
        if report.get("error"):
            self.toasts.show(str(report["error"]), "error")
        else:
            self.toasts.show("Loader plan created. Review it in Logs.", "success")
            self.navigate("Logs")

    def clear_logs(self) -> None:
        self.pages["Logs"].viewer.clear()
        self.toasts.show("Visible logs cleared.", "success")

    def copy_logs(self) -> None:
        content = self.pages["Logs"].viewer.content()
        self.root.clipboard_clear()
        self.root.clipboard_append(content)
        self.toasts.show("Logs copied to the clipboard.", "success")

    def export_logs(self) -> None:
        path = filedialog.asksaveasfilename(
            parent=self.root, title="Export logs", defaultextension=".log",
            initialfile="night-injection-export.log", filetypes=[("Log file", "*.log"), ("Text file", "*.txt")]
        )
        if not path:
            return
        try:
            Path(path).write_text(self.pages["Logs"].viewer.content(), encoding="utf-8")
            self.toasts.show("Logs exported successfully.", "success")
        except OSError as exc:
            self.toasts.show(f"Could not export logs: {exc}", "error")

    def close(self) -> None:
        if self.settings.remember_window_size:
            self.settings.window_geometry = self.root.geometry()
        self.settings_store.save(self.settings)
        logging.getLogger("night_injection").removeHandler(self.log_handler)
        if not self.active_jobs:
            try:
                self.db.close()
            except Exception as exc:  # noqa: BLE001
                self.logger.debug("Database close failed: %s", exc)
        self.root.destroy()


def main() -> None:
    theme.configure()
    root = NightInjectionRoot()
    app = NightInjectionApp(root)
    if "--smoke-test" in sys.argv:
        root.after(2200, app.close)
    root.mainloop()


if __name__ == "__main__":
    main()
