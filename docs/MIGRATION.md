# Data and storage migration

## Current Python state

The Python GUI writes to `%LOCALAPPDATA%\night-injection`:

- `settings.json` with snake-case keys.
- `night-injection.db` with `juegos`, `sesiones`, `fuentes`, and `lightning_history`.
- `cache\covers` and `cache\steam_metadata.json`. Older builds may also leave an unused `cache\bypass_catalog.json` file.
- daily files under `logs`.

Some older/CLI installations may instead have `%APPDATA%\project-lightning-data\biblioteca.db`.

## Native migration behavior

1. The C# application intentionally uses the same `%LOCALAPPDATA%\night-injection` root, so a normal upgrade does not require moving current GUI data.
2. Existing snake-case settings keys are deserialized directly. `window_geometry` is converted into v2 width/height when explicit dimensions are absent. Unknown keys are ignored. A malformed settings file is renamed to `settings.json.corrupt-<timestamp>` and safe defaults are used.
3. The SQLite initializer leaves the four legacy tables intact, adds `schema_info` and `operations`, and imports `lightning_history` rows once with a unique `legacy_key`. The migration is transactional and versioned at schema version 2.
4. Existing cover and metadata caches remain usable. New image downloads are validated by signature and limited to 8 MiB.
5. Historical log files are retained; the native logger starts a new daily file using the same naming pattern.

## Older database import

For an old `biblioteca.db`, close both applications, back up both database files, then copy the old database to `%LOCALAPPDATA%\night-injection\night-injection.db` before the first native launch. The native initializer adds its new tables without dropping legacy data. Do not overwrite an already-used native database; merge that case with an explicit SQLite migration instead.

## Rollback

The migration is additive. Keep a backup of `settings.json` and the database before changing application generations. Python and C# cache contents are disposable and may be cleared without losing Steam configuration files.
