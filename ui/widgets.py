"""Reusable custom widgets for the night-injection interface."""
from __future__ import annotations

import tkinter as tk
from collections.abc import Callable
from datetime import datetime

import customtkinter as ctk

from ui import theme


class SectionHeader(ctk.CTkFrame):
    def __init__(self, master, title: str, subtitle: str, action_text: str | None = None,
                 action: Callable | None = None):
        super().__init__(master, fg_color="transparent")
        self.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(self, text=title, font=theme.PAGE_TITLE, text_color=theme.TEXT).grid(
            row=0, column=0, sticky="w"
        )
        ctk.CTkLabel(
            self, text=subtitle, font=theme.BODY, text_color=theme.TEXT_SECONDARY
        ).grid(row=1, column=0, sticky="w", pady=(4, 0))
        if action_text and action:
            ctk.CTkButton(
                self,
                text=action_text,
                command=action,
                width=132,
                height=40,
                corner_radius=10,
                fg_color=theme.ACCENT,
                hover_color=theme.ACCENT_HOVER,
                text_color="#130900",
                font=(theme.FONT, 12, "bold"),
            ).grid(row=0, column=1, rowspan=2, padx=(16, 0))


class StatusPill(ctk.CTkFrame):
    def __init__(self, master, text: str = "Checking", color: str = theme.WARNING):
        super().__init__(master, fg_color=theme.SURFACE_ALT, corner_radius=20)
        self.dot = ctk.CTkLabel(self, text="●", width=18, text_color=color, font=(theme.FONT, 11))
        self.dot.pack(side="left", padx=(10, 1), pady=5)
        self.label = ctk.CTkLabel(self, text=text, font=theme.SMALL, text_color=theme.TEXT_SECONDARY)
        self.label.pack(side="left", padx=(0, 10), pady=5)

    def set(self, text: str, color: str) -> None:
        self.label.configure(text=text)
        self.dot.configure(text_color=color)


class StatCard(ctk.CTkFrame):
    def __init__(self, master, label: str, value: str, detail: str, accent: str = theme.ACCENT):
        super().__init__(
            master, fg_color=theme.SURFACE, border_color=theme.BORDER,
            border_width=1, corner_radius=14, height=112
        )
        self.grid_propagate(False)
        self.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(self, text=label.upper(), font=theme.CAPTION, text_color=theme.TEXT_MUTED).grid(
            row=0, column=0, sticky="w", padx=18, pady=(15, 1)
        )
        self.value_label = ctk.CTkLabel(
            self, text=value, font=(theme.FONT, 24, "bold"), text_color=theme.TEXT
        )
        self.value_label.grid(row=1, column=0, sticky="w", padx=18)
        self.detail_label = ctk.CTkLabel(self, text=detail, font=theme.SMALL, text_color=accent)
        self.detail_label.grid(row=2, column=0, sticky="w", padx=18, pady=(0, 12))

    def set(self, value: str, detail: str | None = None, color: str | None = None) -> None:
        self.value_label.configure(text=value)
        if detail is not None:
            self.detail_label.configure(text=detail)
        if color is not None:
            self.detail_label.configure(text_color=color)


class DropZone(ctk.CTkFrame):
    def __init__(self, master, on_browse: Callable, on_drop: Callable):
        super().__init__(
            master, fg_color="#0D0D0E", border_color="#63310C",
            border_width=1, corner_radius=16, height=190
        )
        self.on_drop_callback = on_drop
        self.grid_propagate(False)
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)
        content = ctk.CTkFrame(self, fg_color="transparent")
        content.grid(row=0, column=0)
        ctk.CTkLabel(content, text="⇩", font=(theme.FONT, 32, "bold"), text_color=theme.ACCENT).pack()
        ctk.CTkLabel(
            content, text="Drop Lua / Manifest / ZIP files here",
            font=(theme.FONT, 15, "bold"), text_color=theme.TEXT
        ).pack(pady=(3, 4))
        ctk.CTkLabel(
            content, text="or choose files from your computer", font=theme.SMALL,
            text_color=theme.TEXT_MUTED
        ).pack()
        ctk.CTkButton(
            content, text="Browse files", command=on_browse, width=112, height=32,
            corner_radius=9, fg_color=theme.ACCENT_DARK, hover_color="#57280A",
            border_width=1, border_color=theme.ACCENT, text_color=theme.ACCENT,
            font=(theme.FONT, 11, "bold")
        ).pack(pady=(14, 0))
        for widget in (self, content):
            widget.bind("<Enter>", lambda _event: self.set_active(True))
            widget.bind("<Leave>", lambda _event: self.set_active(False))

    def enable_native_drop(self, dnd_files: str) -> bool:
        try:
            self.drop_target_register(dnd_files)
            self.dnd_bind("<<DragEnter>>", self._drag_enter)
            self.dnd_bind("<<DragLeave>>", self._drag_leave)
            self.dnd_bind("<<Drop>>", self._drop)
            return True
        except (AttributeError, tk.TclError):
            return False

    def set_active(self, active: bool) -> None:
        self.configure(
            border_color=theme.ACCENT if active else "#63310C",
            border_width=2 if active else 1,
            fg_color="#17100B" if active else "#0D0D0E",
        )

    def _drag_enter(self, event):
        self.set_active(True)
        return getattr(event, "action", None)

    def _drag_leave(self, event):
        self.set_active(False)
        return getattr(event, "action", None)

    def _drop(self, event):
        self.set_active(False)
        self.on_drop_callback(event.data)
        return getattr(event, "action", None)


class ToastManager:
    def __init__(self, root):
        self.root = root
        self.active: list[ctk.CTkFrame] = []

    def show(self, message: str, level: str = "info", duration: int = 3600) -> None:
        colors = {
            "success": theme.SUCCESS,
            "warning": theme.WARNING,
            "error": theme.ERROR,
            "info": theme.ACCENT,
        }
        toast = ctk.CTkFrame(
            self.root, width=350, height=54, fg_color="#171719",
            border_width=1, border_color=colors.get(level, theme.ACCENT), corner_radius=12
        )
        toast.pack_propagate(False)
        ctk.CTkLabel(
            toast, text="●", text_color=colors.get(level, theme.ACCENT), width=26
        ).pack(side="left", padx=(12, 2))
        ctk.CTkLabel(
            toast, text=message, font=theme.SMALL, text_color=theme.TEXT,
            anchor="w", wraplength=275
        ).pack(side="left", fill="both", expand=True, pady=8)
        self.active.append(toast)
        self._position()
        self.root.after(duration, lambda: self._remove(toast))

    def _position(self) -> None:
        for index, toast in enumerate(reversed(self.active)):
            toast.place(relx=1.0, rely=1.0, x=-24, y=-24 - index * 64, anchor="se")
            toast.lift()

    def _remove(self, toast) -> None:
        if toast in self.active:
            self.active.remove(toast)
            toast.destroy()
            self._position()


class StructuredLogView(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, fg_color=theme.SURFACE, corner_radius=14,
                         border_width=1, border_color=theme.BORDER)
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        toolbar = ctk.CTkFrame(self, fg_color="transparent")
        toolbar.grid(row=0, column=0, sticky="ew", padx=14, pady=(12, 8))
        toolbar.grid_columnconfigure(0, weight=1)
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._apply_filter())
        self.search = ctk.CTkEntry(
            toolbar, textvariable=self.search_var, placeholder_text="Search logs…",
            height=34, fg_color=theme.SURFACE_ALT, border_color=theme.BORDER
        )
        self.search.grid(row=0, column=0, sticky="ew", padx=(0, 10))
        self.auto_scroll = tk.BooleanVar(value=True)
        ctk.CTkSwitch(
            toolbar, text="Auto-scroll", variable=self.auto_scroll, width=100,
            progress_color=theme.ACCENT, font=theme.SMALL
        ).grid(row=0, column=1)
        self.text = tk.Text(
            self, bg="#0B0B0C", fg=theme.TEXT_SECONDARY, insertbackground=theme.TEXT,
            selectbackground=theme.ACCENT_DARK, relief="flat", borderwidth=0,
            font=(theme.MONO, 10), padx=15, pady=12, wrap="word", state="disabled"
        )
        self.text.grid(row=1, column=0, sticky="nsew", padx=1, pady=(0, 1))
        for level, color in {
            "DEBUG": theme.TEXT_MUTED, "INFO": theme.INFO, "WARNING": theme.WARNING,
            "ERROR": theme.ERROR, "SUCCESS": theme.SUCCESS
        }.items():
            self.text.tag_configure(level, foreground=color)
        self.records: list[tuple[str, str, str]] = []

    def append(self, level: str, message: str, timestamp: str | None = None) -> None:
        timestamp = timestamp or datetime.now().astimezone().strftime("%H:%M:%S")
        self.records.append((timestamp, level.upper(), message))
        if len(self.records) > 3000:
            self.records = self.records[-2500:]
        if self._matches(message, level):
            self._insert(timestamp, level.upper(), message)

    def clear(self) -> None:
        self.records.clear()
        self.text.configure(state="normal")
        self.text.delete("1.0", "end")
        self.text.configure(state="disabled")

    def content(self) -> str:
        return "\n".join(f"{ts} [{level}] {message}" for ts, level, message in self.records)

    def _matches(self, message: str, level: str) -> bool:
        needle = self.search_var.get().strip().lower()
        return not needle or needle in message.lower() or needle in level.lower()

    def _insert(self, timestamp: str, level: str, message: str) -> None:
        self.text.configure(state="normal")
        self.text.insert("end", f"{timestamp}  ", "DEBUG")
        self.text.insert("end", f"{level:<7} ", level if level in self.text.tag_names() else "INFO")
        self.text.insert("end", message.rstrip() + "\n")
        if self.auto_scroll.get():
            self.text.see("end")
        self.text.configure(state="disabled")

    def _apply_filter(self) -> None:
        self.text.configure(state="normal")
        self.text.delete("1.0", "end")
        self.text.configure(state="disabled")
        for timestamp, level, message in self.records:
            if self._matches(message, level):
                self._insert(timestamp, level, message)
