"""Page views for the night-injection desktop application."""
from __future__ import annotations

import tkinter as tk
from datetime import datetime, timezone
from pathlib import Path

import customtkinter as ctk
from PIL import Image, ImageOps

from ui import theme
from ui.widgets import DropZone, SectionHeader, StatCard, StatusPill, StructuredLogView


class BasePage(ctk.CTkFrame):
    def __init__(self, master, controller):
        super().__init__(master, fg_color=theme.BG)
        self.controller = controller


class DashboardPage(BasePage):
    def __init__(self, master, controller):
        super().__init__(master, controller)
        self.grid_columnconfigure(0, weight=1)
        SectionHeader(
            self, "Good evening", "Everything you need to manage your Steam injection workflow.",
            "Add files", controller.choose_files
        ).grid(row=0, column=0, sticky="ew", padx=30, pady=(28, 22))

        stats = ctk.CTkFrame(self, fg_color="transparent")
        stats.grid(row=1, column=0, sticky="ew", padx=30)
        for index in range(4):
            stats.grid_columnconfigure(index, weight=1, uniform="stats")
        self.steam_card = StatCard(stats, "Steam status", "Checking", "Detecting installation")
        self.games_card = StatCard(stats, "Library games", "—", "Processed entries")
        self.files_card = StatCard(stats, "Selected files", "0", "Ready to validate")
        self.ops_card = StatCard(stats, "Recent operations", "—", "Recorded locally")
        for index, card in enumerate((self.steam_card, self.games_card, self.files_card, self.ops_card)):
            card.grid(row=0, column=index, sticky="ew", padx=(0 if index == 0 else 6, 0 if index == 3 else 6))

        body = ctk.CTkFrame(self, fg_color="transparent")
        body.grid(row=2, column=0, sticky="nsew", padx=30, pady=22)
        self.grid_rowconfigure(2, weight=1)
        body.grid_columnconfigure(0, weight=3)
        body.grid_columnconfigure(1, weight=2)
        body.grid_rowconfigure(0, weight=1)

        quick = ctk.CTkFrame(body, fg_color=theme.SURFACE, border_width=1,
                             border_color=theme.BORDER, corner_radius=16)
        quick.grid(row=0, column=0, sticky="nsew", padx=(0, 10))
        quick.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(quick, text="Quick injection", font=theme.SECTION_TITLE,
                     text_color=theme.TEXT).grid(row=0, column=0, sticky="w", padx=20, pady=(18, 2))
        ctk.CTkLabel(quick, text="Add supported files and continue in one step.", font=theme.SMALL,
                     text_color=theme.TEXT_MUTED).grid(row=1, column=0, sticky="w", padx=20)
        self.drop_zone = DropZone(quick, controller.choose_files, controller.handle_drop_data)
        self.drop_zone.grid(row=2, column=0, sticky="ew", padx=20, pady=18)
        ctk.CTkButton(
            quick, text="Review and inject  →", command=lambda: controller.navigate("Inject"),
            height=42, corner_radius=10, fg_color=theme.ACCENT, hover_color=theme.ACCENT_HOVER,
            text_color="#180B00", font=(theme.FONT, 12, "bold")
        ).grid(row=3, column=0, sticky="ew", padx=20, pady=(0, 20))

        system = ctk.CTkFrame(body, fg_color=theme.SURFACE, border_width=1,
                              border_color=theme.BORDER, corner_radius=16)
        system.grid(row=0, column=1, sticky="nsew", padx=(10, 0))
        system.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(system, text="System overview", font=theme.SECTION_TITLE,
                     text_color=theme.TEXT).grid(row=0, column=0, sticky="w", padx=20, pady=(18, 12))
        self.status_pill = StatusPill(system)
        self.status_pill.grid(row=1, column=0, sticky="w", padx=20)
        self.path_label = ctk.CTkLabel(
            system, text="Steam path is being detected…", justify="left", anchor="w",
            font=theme.SMALL, text_color=theme.TEXT_SECONDARY, wraplength=330
        )
        self.path_label.grid(row=2, column=0, sticky="ew", padx=20, pady=(14, 18))
        ctk.CTkFrame(system, height=1, fg_color=theme.BORDER_SOFT).grid(
            row=3, column=0, sticky="ew", padx=20
        )
        ctk.CTkLabel(system, text="RECENT ACTIVITY", font=theme.CAPTION,
                     text_color=theme.TEXT_MUTED).grid(row=4, column=0, sticky="w", padx=20, pady=(18, 8))
        self.activity = ctk.CTkFrame(system, fg_color="transparent")
        self.activity.grid(row=5, column=0, sticky="nsew", padx=20, pady=(0, 18))
        system.grid_rowconfigure(5, weight=1)
        self.set_activity([])

    def enable_drop(self, dnd_files: str) -> bool:
        return self.drop_zone.enable_native_drop(dnd_files)

    def update_summary(self, steam_valid: bool, steam_path: str, games: int,
                       files: int, operations: int) -> None:
        if steam_valid:
            self.steam_card.set("Online", "Steam detected", theme.SUCCESS)
            self.status_pill.set("Steam detected", theme.SUCCESS)
        else:
            self.steam_card.set("Offline", "Path needs attention", theme.ERROR)
            self.status_pill.set("Steam not found", theme.ERROR)
        self.games_card.set(str(games))
        self.files_card.set(str(files), "Ready to inject" if files else "Drop or browse files")
        self.ops_card.set(str(operations))
        self.path_label.configure(text=steam_path or "No Steam installation path is configured.")

    def set_activity(self, rows: list[dict]) -> None:
        for child in self.activity.winfo_children():
            child.destroy()
        if not rows:
            ctk.CTkLabel(self.activity, text="No operations recorded yet.", font=theme.SMALL,
                         text_color=theme.TEXT_MUTED).pack(anchor="w", pady=10)
            return
        for row in rows[:4]:
            item = ctk.CTkFrame(self.activity, fg_color="transparent")
            item.pack(fill="x", pady=6)
            ctk.CTkLabel(item, text="●", width=18, text_color=theme.ACCENT,
                         font=(theme.FONT, 8)).pack(side="left")
            title = row.get("game_name") or f"AppID {row.get('app_id', '—')}"
            ctk.CTkLabel(item, text=title, font=(theme.FONT, 11, "bold"),
                         text_color=theme.TEXT, anchor="w").pack(side="left", fill="x", expand=True)
            ctk.CTkLabel(item, text=str(row.get("status", "info")).upper(), font=theme.CAPTION,
                         text_color=theme.TEXT_MUTED).pack(side="right")


class InjectPage(BasePage):
    def __init__(self, master, controller):
        super().__init__(master, controller)
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        SectionHeader(
            self, "Inject", "Validate and apply Lua, manifest, or ZIP files safely."
        ).grid(row=0, column=0, sticky="ew", padx=30, pady=(28, 22))
        content = ctk.CTkFrame(self, fg_color="transparent")
        content.grid(row=1, column=0, sticky="nsew", padx=30, pady=(0, 26))
        content.grid_columnconfigure(0, weight=1)
        content.grid_rowconfigure(0, weight=1)

        left = ctk.CTkFrame(content, fg_color=theme.SURFACE, border_width=1,
                            border_color=theme.BORDER, corner_radius=16)
        left.grid(row=0, column=0, sticky="nsew")
        left.grid_columnconfigure(0, weight=1)
        left.grid_rowconfigure(2, weight=1)
        self.drop_zone = DropZone(left, controller.choose_files, controller.handle_drop_data)
        self.drop_zone.grid(row=0, column=0, sticky="ew", padx=18, pady=18)
        queue_header = ctk.CTkFrame(left, fg_color="transparent")
        queue_header.grid(row=1, column=0, sticky="ew", padx=18, pady=(0, 8))
        queue_header.grid_columnconfigure(0, weight=1)
        self.queue_title = ctk.CTkLabel(queue_header, text="Selected files · 0", font=(theme.FONT, 12, "bold"),
                                        text_color=theme.TEXT)
        self.queue_title.grid(row=0, column=0, sticky="w")
        ctk.CTkButton(queue_header, text="Clear", command=controller.clear_selected_files,
                      width=62, height=26, fg_color="transparent", hover_color=theme.SURFACE_ALT,
                      text_color=theme.TEXT_MUTED).grid(row=0, column=1)
        self.file_list = ctk.CTkScrollableFrame(left, fg_color="#0B0B0C", corner_radius=10)
        self.file_list.grid(row=2, column=0, sticky="nsew", padx=18)
        footer = ctk.CTkFrame(left, fg_color="transparent")
        footer.grid(row=3, column=0, sticky="ew", padx=18, pady=18)
        footer.grid_columnconfigure(0, weight=1)
        self.dry_run = tk.BooleanVar(value=False)
        ctk.CTkSwitch(footer, text="Dry run", variable=self.dry_run, progress_color=theme.ACCENT,
                      font=theme.SMALL).grid(row=0, column=0, sticky="w")
        self.inject_button = ctk.CTkButton(
            footer, text="Inject selected files", command=controller.inject_selected_files,
            height=42, width=190, corner_radius=10, fg_color=theme.ACCENT,
            hover_color=theme.ACCENT_HOVER, text_color="#180B00", font=(theme.FONT, 12, "bold")
        )
        self.inject_button.grid(row=0, column=1)
        self.progress = ctk.CTkProgressBar(left, progress_color=theme.ACCENT, fg_color=theme.BORDER,
                                           mode="indeterminate")
        self.progress.grid(row=4, column=0, sticky="ew", padx=18, pady=(0, 6))
        self.progress.grid_remove()
        self.status = ctk.CTkLabel(left, text="", font=theme.SMALL,
                                   text_color=theme.TEXT_MUTED, anchor="w")
        self.status.grid(row=5, column=0, sticky="ew", padx=18, pady=(0, 12))

    def enable_drop(self, dnd_files: str) -> bool:
        return self.drop_zone.enable_native_drop(dnd_files)

    def set_files(self, paths: list[Path]) -> None:
        for child in self.file_list.winfo_children():
            child.destroy()
        self.queue_title.configure(text=f"Selected files · {len(paths)}")
        if not paths:
            ctk.CTkLabel(self.file_list, text="No files selected", font=theme.SMALL,
                         text_color=theme.TEXT_MUTED).pack(pady=22)
            return
        for path in paths:
            row = ctk.CTkFrame(self.file_list, fg_color=theme.SURFACE_ALT, corner_radius=9)
            row.pack(fill="x", pady=4)
            ext = path.suffix.lower().removeprefix(".").upper()
            ctk.CTkLabel(row, text=ext, width=48, font=theme.CAPTION,
                         text_color=theme.ACCENT).pack(side="left", padx=(10, 6), pady=10)
            names = ctk.CTkFrame(row, fg_color="transparent")
            names.pack(side="left", fill="x", expand=True, pady=7)
            ctk.CTkLabel(names, text=path.name, font=(theme.FONT, 11, "bold"),
                         text_color=theme.TEXT, anchor="w").pack(fill="x")
            ctk.CTkLabel(names, text=str(path.parent), font=(theme.FONT, 9),
                         text_color=theme.TEXT_MUTED, anchor="w").pack(fill="x")
            ctk.CTkButton(row, text="×", command=lambda p=path: self.controller.remove_selected_file(p),
                          width=28, height=28, fg_color="transparent", hover_color="#341719",
                          text_color=theme.TEXT_MUTED).pack(side="right", padx=8)

    def set_busy(self, busy: bool, status: str, animate: bool = True) -> None:
        state = "disabled" if busy else "normal"
        self.inject_button.configure(state=state)
        self.status.configure(text=status if busy else "", text_color=theme.ACCENT)
        if busy:
            self.progress.grid()
            if animate:
                self.progress.configure(mode="indeterminate")
                self.progress.start()
            else:
                self.progress.configure(mode="determinate")
                self.progress.set(0.55)
        else:
            self.progress.stop()
            self.progress.grid_remove()


class GameCard(ctk.CTkFrame):
    def __init__(self, master, game: dict, on_remove):
        super().__init__(master, fg_color=theme.SURFACE, border_width=1,
                         border_color=theme.BORDER, corner_radius=14)
        self.game = game
        self.grid_columnconfigure(0, weight=1)
        cover_path = Path(game.get("cover_path") or "")
        try:
            image = ImageOps.fit(Image.open(cover_path).convert("RGB"), (344, 472))
            self.cover = ctk.CTkImage(light_image=image, dark_image=image, size=(172, 236))
            cover = ctk.CTkLabel(self, text="", image=self.cover, corner_radius=10)
        except (OSError, ValueError):
            cover = ctk.CTkLabel(self, text="NI", width=172, height=236,
                                 fg_color=theme.SURFACE_ALT, text_color=theme.ACCENT,
                                 font=(theme.FONT, 30, "bold"), corner_radius=10)
        cover.grid(row=0, column=0, sticky="ew", padx=10, pady=(10, 12))
        title = str(game.get("title") or f"Game {game.get('app_id')}")
        ctk.CTkLabel(self, text=title, font=(theme.FONT, 13, "bold"), text_color=theme.TEXT,
                     anchor="w", wraplength=174, justify="left").grid(
            row=1, column=0, sticky="ew", padx=12
        )
        meta = ctk.CTkFrame(self, fg_color="transparent")
        meta.grid(row=2, column=0, sticky="ew", padx=12, pady=(5, 4))
        ctk.CTkLabel(meta, text=f"AppID {game.get('app_id')}", font=theme.SMALL,
                     text_color=theme.TEXT_MUTED).pack(side="left")
        ctk.CTkLabel(meta, text="●  READY", font=(theme.FONT, 9, "bold"),
                     text_color=theme.SUCCESS).pack(side="right")
        stamp = game.get("recent_ts") or 0
        date_text = datetime.fromtimestamp(stamp / 1000, tz=timezone.utc).astimezone().strftime("Added %b %d, %Y") if stamp else "Date unavailable"
        operation = game.get("last_operation")
        if operation:
            date_text = f"Last operation: {str(operation).title()}  ·  {date_text}"
        ctk.CTkLabel(self, text=date_text, font=(theme.FONT, 9), text_color=theme.TEXT_MUTED,
                     anchor="w").grid(row=3, column=0, sticky="ew", padx=12, pady=(0, 8))
        ctk.CTkButton(
            self, text="Remove", command=lambda: on_remove(str(game.get("app_id"))),
            height=30, fg_color="transparent", hover_color="#321619", border_width=1,
            border_color=theme.BORDER, text_color=theme.TEXT_SECONDARY, font=theme.SMALL
        ).grid(row=4, column=0, sticky="ew", padx=10, pady=(0, 10))
        for widget in (self, cover):
            widget.bind("<Enter>", self._enter)
            widget.bind("<Leave>", self._leave)

    def _enter(self, _event=None):
        self.configure(border_color=theme.ACCENT, fg_color=theme.SURFACE_HOVER)

    def _leave(self, _event=None):
        self.configure(border_color=theme.BORDER, fg_color=theme.SURFACE)


class LibraryPage(BasePage):
    def __init__(self, master, controller):
        super().__init__(master, controller)
        self.games: list[dict] = []
        self._visible = False
        self._render_generation = 0
        self._rendered_signature: tuple | None = None
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)
        SectionHeader(self, "Game library", "Your processed Steam games and locally cached artwork.",
                      "Refresh", controller.refresh_library).grid(
            row=0, column=0, sticky="ew", padx=30, pady=(28, 18)
        )
        toolbar = ctk.CTkFrame(self, fg_color="transparent")
        toolbar.grid(row=1, column=0, sticky="ew", padx=30, pady=(0, 14))
        toolbar.grid_columnconfigure(0, weight=1)
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self.render())
        ctk.CTkEntry(toolbar, textvariable=self.search_var, placeholder_text="Search by game or AppID…",
                     height=38, fg_color=theme.SURFACE, border_color=theme.BORDER).grid(
            row=0, column=0, sticky="ew", padx=(0, 12)
        )
        self.count = ctk.CTkLabel(toolbar, text="0 games", font=theme.SMALL,
                                  text_color=theme.TEXT_MUTED)
        self.count.grid(row=0, column=1)
        self.game_grid = ctk.CTkScrollableFrame(self, fg_color="transparent")
        self.game_grid.grid(row=2, column=0, sticky="nsew", padx=24, pady=(0, 24))

    def set_games(self, games: list[dict]) -> None:
        self.games = games
        self._rendered_signature = None
        self.count.configure(text=f"{len(games)} game{'s' if len(games) != 1 else ''}")
        if self._visible:
            self.render()

    def on_show(self) -> None:
        self._visible = True
        self.after_idle(self.render)

    def on_hide(self) -> None:
        self._visible = False
        self._render_generation += 1

    def render(self) -> None:
        if not self._visible:
            return
        query = self.search_var.get().strip().lower()
        signature = (tuple((game.get("app_id"), game.get("title"), game.get("last_operation"))
                           for game in self.games), query, max(self.winfo_width(), 1))
        if signature == self._rendered_signature:
            return
        self._rendered_signature = signature
        self._render_generation += 1
        generation = self._render_generation
        for child in self.game_grid.winfo_children():
            child.destroy()
        games = [g for g in self.games if not query or query in str(g.get("title", "")).lower()
                 or query in str(g.get("app_id", ""))]
        self.count.configure(text=f"{len(games)} game{'s' if len(games) != 1 else ''}")
        columns = max(2, min(5, max(self.winfo_width(), 700) // 235))
        for col in range(columns):
            self.game_grid.grid_columnconfigure(col, weight=1, uniform="library")
        if not games:
            empty = ctk.CTkFrame(self.game_grid, fg_color=theme.SURFACE, border_width=1,
                                 border_color=theme.BORDER, corner_radius=16)
            empty.grid(row=0, column=0, columnspan=columns, sticky="ew", padx=6, pady=20)
            ctk.CTkLabel(empty, text="No games found", font=theme.SECTION_TITLE,
                         text_color=theme.TEXT).pack(pady=(28, 4))
            ctk.CTkLabel(empty, text="Inject a numeric Lua file or refresh after Steam updates.",
                         font=theme.SMALL, text_color=theme.TEXT_MUTED).pack(pady=(0, 28))
            return
        self._render_batch(games, 0, columns, generation)

    def _render_batch(self, games: list[dict], start: int, columns: int, generation: int) -> None:
        if generation != self._render_generation or not self._visible:
            return
        end = min(start + 2, len(games))
        for index in range(start, end):
            GameCard(self.game_grid, games[index], self.controller.remove_game).grid(
                row=index // columns, column=index % columns, sticky="new", padx=7, pady=7
            )
        if end < len(games):
            self.after(8, lambda: self._render_batch(games, end, columns, generation))


class HistoryPage(BasePage):
    def __init__(self, master, controller):
        super().__init__(master, controller)
        self.rows: list[dict] = []
        self._visible = False
        self._render_generation = 0
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        SectionHeader(self, "History", "A local record of installation and removal operations.",
                      "Refresh", controller.refresh_history).grid(
            row=0, column=0, sticky="ew", padx=30, pady=(28, 20)
        )
        self.list = ctk.CTkScrollableFrame(self, fg_color="transparent")
        self.list.grid(row=1, column=0, sticky="nsew", padx=24, pady=(0, 24))

    def set_rows(self, rows: list[dict]) -> None:
        self.rows = rows
        if self._visible:
            self.render()

    def on_show(self) -> None:
        self._visible = True
        self.after_idle(self.render)

    def on_hide(self) -> None:
        self._visible = False
        self._render_generation += 1

    def render(self) -> None:
        if not self._visible:
            return
        self._render_generation += 1
        generation = self._render_generation
        for child in self.list.winfo_children():
            child.destroy()
        if not self.rows:
            ctk.CTkLabel(self.list, text="No history entries yet.", font=theme.BODY,
                         text_color=theme.TEXT_MUTED).pack(pady=40)
            return
        self._render_batch(0, generation)

    def _render_batch(self, start: int, generation: int) -> None:
        if generation != self._render_generation or not self._visible:
            return
        end = min(start + 6, len(self.rows))
        for row in self.rows[start:end]:
            card = ctk.CTkFrame(self.list, fg_color=theme.SURFACE, border_width=1,
                                border_color=theme.BORDER, corner_radius=12)
            card.pack(fill="x", padx=6, pady=5)
            ctk.CTkLabel(card, text="●", width=24, text_color=theme.SUCCESS if row.get("status") != "error" else theme.ERROR).pack(
                side="left", padx=(14, 4), pady=14
            )
            text = ctk.CTkFrame(card, fg_color="transparent")
            text.pack(side="left", fill="x", expand=True, pady=10)
            name = row.get("game_name") or f"Steam AppID {row.get('app_id', '—')}"
            ctk.CTkLabel(text, text=name, font=(theme.FONT, 12, "bold"), text_color=theme.TEXT,
                         anchor="w").pack(fill="x")
            repo = row.get("repo") or "Local file operation"
            ctk.CTkLabel(text, text=repo, font=(theme.FONT, 9), text_color=theme.TEXT_MUTED,
                         anchor="w").pack(fill="x")
            stamp = row.get("applied_at") or 0
            when = datetime.fromtimestamp(stamp / 1000, tz=timezone.utc).astimezone().strftime("%Y-%m-%d  %H:%M") if stamp else "Unknown date"
            ctk.CTkLabel(card, text=when, font=theme.SMALL, text_color=theme.TEXT_MUTED).pack(side="right", padx=16)
            ctk.CTkLabel(card, text=str(row.get("status", "info")).upper(), font=theme.CAPTION,
                         text_color=theme.ACCENT).pack(side="right", padx=8)
        if end < len(self.rows):
            self.after(8, lambda: self._render_batch(end, generation))


class SettingsPage(BasePage):
    def __init__(self, master, controller, settings):
        super().__init__(master, controller)
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        SectionHeader(self, "Settings", "Control Steam detection, caching, and application behavior.",
                      "Save changes", controller.save_settings).grid(
            row=0, column=0, sticky="ew", padx=30, pady=(28, 20)
        )
        scroll = ctk.CTkScrollableFrame(self, fg_color="transparent")
        scroll.grid(row=1, column=0, sticky="nsew", padx=24, pady=(0, 24))
        scroll.grid_columnconfigure(0, weight=1)

        steam = self._group(scroll, "Steam installation", "Automatically detected or manually selected.", 0)
        steam.grid_columnconfigure(0, weight=1)
        self.steam_path = tk.StringVar(value=settings.steam_path)
        ctk.CTkEntry(steam, textvariable=self.steam_path, height=40, fg_color=theme.SURFACE_ALT,
                     border_color=theme.BORDER).grid(row=2, column=0, sticky="ew", padx=18, pady=(14, 10))
        controls = ctk.CTkFrame(steam, fg_color="transparent")
        controls.grid(row=3, column=0, sticky="w", padx=18, pady=(0, 18))
        for text, command in (("Browse", controller.browse_steam_path), ("Detect", controller.detect_steam_path),
                              ("Verify", controller.verify_settings_path)):
            ctk.CTkButton(controls, text=text, command=command, width=82, height=32,
                          fg_color=theme.SURFACE_ALT, hover_color=theme.SURFACE_HOVER,
                          border_width=1, border_color=theme.BORDER).pack(side="left", padx=(0, 8))
        self.steam_status = StatusPill(steam, "Not checked", theme.WARNING)
        self.steam_status.grid(row=3, column=1, padx=(0, 18), pady=(0, 18))

        cache = self._group(scroll, "Artwork cache", "Covers and Steam metadata are stored locally for fast, offline rendering.", 1)
        cache.grid_columnconfigure(0, weight=1)
        self.cache_path = tk.StringVar(value=settings.cache_directory)
        ctk.CTkEntry(cache, textvariable=self.cache_path, height=40, fg_color=theme.SURFACE_ALT,
                     border_color=theme.BORDER).grid(row=2, column=0, sticky="ew", padx=18, pady=(14, 18))
        ctk.CTkButton(cache, text="Clear cache", command=controller.clear_cache, width=100, height=32,
                      fg_color="transparent", hover_color="#321619", border_width=1,
                      border_color=theme.BORDER, text_color=theme.TEXT_SECONDARY).grid(
            row=2, column=1, padx=(0, 18), pady=(14, 18)
        )

        prefs = self._group(scroll, "Preferences", "Customize feedback and startup behavior.", 2)
        self.animations = tk.BooleanVar(value=settings.animations_enabled)
        self.auto_scroll = tk.BooleanVar(value=settings.auto_scroll_logs)
        self.remember_window = tk.BooleanVar(value=settings.remember_window_size)
        self.debug = tk.BooleanVar(value=settings.debug_logging)
        self.fetch_metadata = tk.BooleanVar(value=settings.fetch_metadata)
        for index, (label, variable) in enumerate((
            ("Enable interface animations", self.animations),
            ("Auto-scroll application logs", self.auto_scroll),
            ("Remember window size", self.remember_window),
            ("Fetch Steam metadata and artwork", self.fetch_metadata),
            ("Enable debug logging", self.debug),
        )):
            ctk.CTkSwitch(prefs, text=label, variable=variable, progress_color=theme.ACCENT,
                          font=theme.BODY).grid(row=index + 2, column=0, sticky="w", padx=18,
                                                pady=(12 if index == 0 else 5, 12 if index == 4 else 5))

        advanced = self._group(scroll, "Advanced", "Review the preserved loader workflow without changing Steam.", 3)
        ctk.CTkButton(advanced, text="Plan loader installation", command=controller.plan_loader,
                      height=36, fg_color=theme.ACCENT_DARK, hover_color="#57280A",
                      border_width=1, border_color=theme.ACCENT, text_color=theme.ACCENT).grid(
            row=2, column=0, sticky="w", padx=18, pady=(14, 18)
        )

    @staticmethod
    def _group(master, title: str, subtitle: str, row: int):
        frame = ctk.CTkFrame(master, fg_color=theme.SURFACE, border_width=1,
                             border_color=theme.BORDER, corner_radius=14)
        frame.grid(row=row, column=0, sticky="ew", padx=6, pady=7)
        ctk.CTkLabel(frame, text=title, font=theme.SECTION_TITLE, text_color=theme.TEXT).grid(
            row=0, column=0, columnspan=2, sticky="w", padx=18, pady=(16, 2)
        )
        ctk.CTkLabel(frame, text=subtitle, font=theme.SMALL, text_color=theme.TEXT_MUTED).grid(
            row=1, column=0, columnspan=2, sticky="w", padx=18
        )
        return frame


class LogsPage(BasePage):
    def __init__(self, master, controller):
        super().__init__(master, controller)
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        header = SectionHeader(self, "Logs", "Structured runtime events and diagnostic information.")
        header.grid(row=0, column=0, sticky="ew", padx=30, pady=(28, 18))
        actions = ctk.CTkFrame(header, fg_color="transparent")
        actions.grid(row=0, column=1, rowspan=2)
        for text, command in (("Copy", controller.copy_logs), ("Export", controller.export_logs),
                              ("Clear", controller.clear_logs)):
            ctk.CTkButton(actions, text=text, command=command, width=72, height=32,
                          fg_color=theme.SURFACE, hover_color=theme.SURFACE_HOVER,
                          border_width=1, border_color=theme.BORDER).pack(side="left", padx=4)
        self.viewer = StructuredLogView(self)
        self.viewer.grid(row=1, column=0, sticky="nsew", padx=30, pady=(0, 28))
