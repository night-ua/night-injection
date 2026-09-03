"""Command-line interface for night-injection."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from core.logging_setup import setup_logging


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="night-injection",
        description=(
            "Clean-room Python port of Project Lightning v5 'Lightning Tools' "
            "(evidence-based). Writes <steam>/config/{stplug-in,lua,depotcache} "
            "exactly like the original app."
        ),
    )
    p.add_argument("--steam-path", default=None, help="Steam base path (default: auto-detect)")
    p.add_argument("--db", default=None, help="SQLite DB path (default: %%APPDATA%%/project-lightning-data/biblioteca.db)")
    p.add_argument("--dry-run", action="store_true", help="fetch and plan, write nothing")
    p.add_argument("-v", "--verbose", action="store_true")

    sub = p.add_subparsers(dest="command", required=True)

    add = sub.add_parser("add", help="add an AppID (original 'lightningtools:addAppId')")
    add.add_argument("appid", help="numeric Steam AppID")
    add.add_argument("--force", action="store_true", help="overwrite existing <appid>.lua")

    sub.add_parser("list", help="list games processed by Lightning (config/lua scan)")

    rm = sub.add_parser("remove", help="remove an AppID (original 'lightningtools:removeAppId')")
    rm.add_argument("appid")

    imp = sub.add_parser("inject", help="INJECT .lua/.manifest/.zip files (original importFiles — no AppID needed)")
    imp.add_argument("files", nargs="+")

    sub.add_parser("import", help="alias of 'inject'")

    sub.add_parser("clear", help="delete all files in config/stplug-in (original clearPlugins)")

    sub.add_parser("library", help="rebuild library view (titles + covers, original loadSteamLibrary)")

    verify = sub.add_parser("verify", help="verify steam path (original verifySteamPath)")
    verify.add_argument("path", nargs="?", default=None)

    inst = sub.add_parser(
        "install-loader",
        help="DRY-RUN by default: plan OpenSteamTool loader install (original downloadAndInstall)",
    )
    inst.add_argument("--apply", action="store_true", help="actually write DLLs into Steam (DANGEROUS)")

    sub.add_parser("history", help="show lightning_history table (documented extension)")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    setup_logging(args.verbose)

    from services.lightning_service import LightningService
    from storage.database import Database

    db = Database(args.db) if args.db else None
    svc = LightningService(steam_base_path=args.steam_path, db=db)
    print(f"[*] Steam path: {svc.steam_base_path}")

    if args.command == "add":
        res = svc.add_app_id(args.appid, dry_run=args.dry_run, force=args.force)
        if res.ok:
            tag = "[dry-run plan]" if res.error == "dry-run" else "[ok]"
            print(f"{tag} repo: {res.repo}")
            for w in res.written:
                print("   ", w)
            if res.error == "dry-run":
                print("use without --dry-run to apply")
        else:
            print(f"[error] {res.error}")
            return 1

    elif args.command == "list":
        entries = svc.processed_app_ids()
        if not entries:
            print("(no .lua files in config/lua)")
        for e in entries:
            print(f"  {e['appId']:<12} mtime_ts={int(e['recentTs'] / 1000)}")

    elif args.command == "remove":
        res = svc.remove_app_id(args.appid)
        print(f"[ok] removed {len(res.written)} file(s)")
        for w in res.written:
            print("   ", w)

    elif args.command in ("inject", "import"):
        files = []
        for f in args.files:
            path = Path(f)
            if not path.exists():
                print(f"[skip] not found: {f}")
                continue
            files.append({"path": str(path), "name": path.name, "buffer": None})
        res = svc.inject_files(files, dry_run=args.dry_run)
        print(json.dumps(res, indent=2, ensure_ascii=False))
        return 0 if res["ok"] else 1

    elif args.command == "clear":
        print(json.dumps(svc.clear_plugins(), indent=2))

    elif args.command == "library":
        games = svc.build_library(fetch_titles=not args.dry_run)
        if not games:
            print("(library empty — no .lua entries)")
        for g in games:
            restart = " [needs-steam-restart]" if g["needs_steam_restart"] else ""
            print(f"  {g['app_id']:<12} {g['title']}{restart}")
            print(f"               cover: {g['img_src']}")

    elif args.command == "verify":
        from steam.discovery import verify_steam_path

        target = args.path or svc.steam_base_path
        print(json.dumps(verify_steam_path(target), indent=2))

    elif args.command == "install-loader":
        from installer.loader import download_and_install

        report = download_and_install(svc.steam_base_path, apply=args.apply and not args.dry_run)
        print(json.dumps(report, indent=2))

    elif args.command == "history":
        if db is None:
            print("(no --db given; history is stored in SQLite)")
        else:
            for row in db.list_lightning_history():
                print(json.dumps(row, ensure_ascii=False))

    if db is not None:
        db.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
