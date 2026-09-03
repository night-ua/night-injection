"""AppID validation — 1:1 port of isValidAppId() (HIGH evidence)."""
from __future__ import annotations

import re

_APPID_RE = re.compile(r"^\d+$")


def is_valid_app_id(app_id: object) -> bool:
    """Original: typeof appId === 'string' && /^\\d+$/.test(appId.trim())."""
    return isinstance(app_id, str) and bool(_APPID_RE.match(app_id.strip()))


def require_valid_app_id(app_id: str) -> str:
    if not is_valid_app_id(app_id):
        raise ValueError("AppID must be a positive numeric string")
    return app_id.strip()
