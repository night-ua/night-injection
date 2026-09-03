"""Configure timestamped application logs for files and diagnostics."""
from __future__ import annotations

import logging
import sys
from datetime import datetime

from core.config import LOGS_DIR

_CONFIGURED = False


def setup_logging(verbose: bool = False) -> logging.Logger:
    global _CONFIGURED
    LOGS_DIR.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger("night_injection")
    if _CONFIGURED:
        logger.setLevel(logging.DEBUG if verbose else logging.INFO)
        return logger
    logger.setLevel(logging.DEBUG)
    formatter = logging.Formatter(
        "%(asctime)s.%(msecs)03d [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )
    file_handler = logging.FileHandler(
        LOGS_DIR / f"night-injection_{datetime.now().astimezone():%Y%m%d}.log", encoding="utf-8"
    )
    file_handler.setFormatter(formatter)
    file_handler.setLevel(logging.DEBUG)
    console_handler = logging.StreamHandler(sys.stderr)
    console_handler.setFormatter(formatter)
    console_handler.setLevel(logging.DEBUG if verbose else logging.INFO)
    logger.addHandler(file_handler)
    logger.addHandler(console_handler)
    _CONFIGURED = True
    return logger
