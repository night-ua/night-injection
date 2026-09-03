"""Visual tokens for the night-injection desktop interface."""
from __future__ import annotations

BG = "#070707"
SIDEBAR = "#0A0A0B"
SURFACE = "#101012"
SURFACE_ALT = "#151517"
SURFACE_HOVER = "#1B1714"
BORDER = "#29292D"
BORDER_SOFT = "#202024"
ACCENT = "#FF7800"
ACCENT_HOVER = "#FF922E"
ACCENT_DARK = "#3B1D08"
TEXT = "#F7F7F8"
TEXT_SECONDARY = "#A1A1AA"
TEXT_MUTED = "#71717A"
SUCCESS = "#4ADE80"
WARNING = "#FBBF24"
ERROR = "#FB7185"
INFO = "#60A5FA"

FONT = "Segoe UI"
MONO = "Cascadia Mono"

PAGE_TITLE = (FONT, 28, "bold")
SECTION_TITLE = (FONT, 18, "bold")
BODY = (FONT, 13)
SMALL = (FONT, 11)
CAPTION = (FONT, 10, "bold")


def configure() -> None:
    import customtkinter as ctk

    ctk.set_appearance_mode("dark")
    ctk.set_default_color_theme("dark-blue")
