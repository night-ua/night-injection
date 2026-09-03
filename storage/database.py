"""SQLite storage.

DB file: %APPDATA%/project-lightning-data/biblioteca.db (observed on this
machine; original uses better-sqlite3 with this exact path via
app.getPath('userData')).

The juegos/sesiones/fuentes schema below is the ACTUAL schema dumped from the
original biblioteca.db (HIGH evidence, verbatim SQL). 'lightning_history' is a
documented EXTENSION (the original tracks Lightning operations only via the
filesystem — config/lua/*.lua).
"""
from __future__ import annotations

import sqlite3
import threading
import time
from pathlib import Path

SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS juegos (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre          TEXT    NOT NULL,
            igdb_id         INTEGER,
            steam_id        INTEGER,
            descripcion     TEXT,
            genero          TEXT,
            anio            INTEGER,
            horas_jugadas   REAL    DEFAULT 0,
            last_played     INTEGER DEFAULT 0,
            fecha_agregado  INTEGER DEFAULT (strftime('%s','now') * 1000),
            cover_url       TEXT,
            art_url         TEXT,
            ruta_ejecutable TEXT,
            install_path    TEXT,
            install_stage   TEXT,
            instalado       INTEGER DEFAULT 0,
            activo          INTEGER DEFAULT 1
        , logo_url TEXT, logo_src_url TEXT, cover_src_url TEXT, art_src_url TEXT)
"""
SCHEMA_SQL_SESIONES = """
CREATE TABLE IF NOT EXISTS sesiones (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            juego_id   INTEGER NOT NULL REFERENCES juegos(id) ON DELETE CASCADE,
            inicio     INTEGER NOT NULL,
            fin        INTEGER,
            duracion_s INTEGER
        )
"""
SCHEMA_SQL_FUENTES = """
CREATE TABLE IF NOT EXISTS fuentes (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            nexus_id    TEXT NOT NULL UNIQUE,
            nombre      TEXT NOT NULL,
            url         TEXT NOT NULL,
            active      INTEGER DEFAULT 1,
            created_at  INTEGER DEFAULT (strftime('%s','now') * 1000),
            updated_at  INTEGER DEFAULT (strftime('%s','now') * 1000)
        )
"""
# ---- documented extension -------------------------------------------------
SCHEMA_SQL_LIGHTNING = """
CREATE TABLE IF NOT EXISTS lightning_history (
            app_id       TEXT PRIMARY KEY,
            game_name    TEXT,
            cover_path   TEXT,
            lua_path     TEXT,
            manifest_path TEXT,
            repo         TEXT,
            applied_at   INTEGER,
            status       TEXT NOT NULL,
            error        TEXT
        )
"""


class Database:
    def __init__(self, db_path: str | Path):
        self.db_path = Path(db_path)
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        # The GUI calls the service from worker threads — allow cross-thread
        # use and serialize writes with a lock.
        self._lock = threading.Lock()
        self.con = sqlite3.connect(str(self.db_path), check_same_thread=False)
        self.con.row_factory = sqlite3.Row
        self._init_schema()

    def _init_schema(self) -> None:
        cur = self.con.cursor()
        cur.execute(SCHEMA_SQL)
        cur.execute(SCHEMA_SQL_SESIONES)
        cur.execute(SCHEMA_SQL_FUENTES)
        cur.execute(SCHEMA_SQL_LIGHTNING)
        cur.execute("CREATE INDEX IF NOT EXISTS idx_last_played ON juegos(last_played DESC)")
        cur.execute("CREATE INDEX IF NOT EXISTS idx_nombre ON juegos(nombre COLLATE NOCASE)")
        self.con.commit()

    def close(self) -> None:
        with self._lock:
            self.con.close()

    # ---- lightning_history (extension) ------------------------------------
    def record_lightning_op(
        self,
        app_id: str,
        *,
        repo: str | None = None,
        written: list[str] | None = None,
        status: str = "ok",
        error: str | None = None,
        game_name: str | None = None,
        cover_path: str | None = None,
    ) -> None:
        written = written or []
        lua_path = next((w for w in written if w.lower().endswith(".lua")), None)
        manifest_path = next((w for w in written if w.lower().endswith(".manifest")), None)
        with self._lock:
            self.con.execute(
                """
                INSERT INTO lightning_history
                    (app_id, game_name, cover_path, lua_path, manifest_path, repo, applied_at, status, error)
                VALUES (?,?,?,?,?,?,?,?,?)
                ON CONFLICT(app_id) DO UPDATE SET
                    game_name=excluded.game_name, cover_path=excluded.cover_path,
                    lua_path=COALESCE(excluded.lua_path, lightning_history.lua_path),
                    manifest_path=COALESCE(excluded.manifest_path, lightning_history.manifest_path),
                    repo=COALESCE(excluded.repo, lightning_history.repo),
                    applied_at=excluded.applied_at, status=excluded.status, error=excluded.error
                """,
                (
                    str(app_id), game_name, cover_path, lua_path, manifest_path,
                    repo, int(time.time() * 1000), status, error,
                ),
            )
            self.con.commit()

    def list_lightning_history(self) -> list[dict]:
        with self._lock:
            rows = self.con.execute(
                "SELECT * FROM lightning_history ORDER BY applied_at DESC"
            ).fetchall()
        return [dict(r) for r in rows]

    # ---- juegos (original schema parity) -----------------------------------
    def upsert_juego_lightning(self, app_id: str, nombre: str, cover_url: str | None) -> int:
        """Insert/refresh a juegos row for a Lightning-processed game
        (steam_id = AppID). Returns the row id."""
        with self._lock:
            cur = self.con.execute(
                "SELECT id FROM juegos WHERE steam_id = ? AND activo = 1", (int(app_id),)
            )
            row = cur.fetchone()
            if row:
                self.con.execute(
                    "UPDATE juegos SET nombre = ?, cover_url = ? WHERE id = ?",
                    (nombre, cover_url, row["id"]),
                )
                self.con.commit()
                return int(row["id"])
            cur = self.con.execute(
                "INSERT INTO juegos (nombre, steam_id, cover_url) VALUES (?,?,?)",
                (nombre, int(app_id), cover_url),
            )
            self.con.commit()
            return int(cur.lastrowid)

    def list_juegos(self) -> list[dict]:
        with self._lock:
            rows = self.con.execute(
                "SELECT * FROM juegos WHERE activo = 1 ORDER BY fecha_agregado DESC"
            ).fetchall()
        return [dict(r) for r in rows]

