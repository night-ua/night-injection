"""Custom error types (mirrors of the original error strings where they exist)."""
from __future__ import annotations


class NightInjectionError(Exception):
    """Base error."""


class InvalidAppIdError(NightInjectionError):
    """Raised when an AppID is not a positive numeric string."""


class SteamPathMissingError(NightInjectionError):
    """Raised when no usable Steam installation path is available."""


class RepoNotFoundError(NightInjectionError):
    """Original result: { ok: false } after trying every repo."""
