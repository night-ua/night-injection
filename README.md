# night-injection

night-injection is a Windows desktop utility for managing Lua, manifest, and ZIP injection workflows for a local Steam installation. It preserves the project's proven file-processing behavior while providing a responsive black-and-orange interface, a visual game library, persistent settings, cached Steam artwork, structured history, and searchable logs.

## Features

- Automatic Steam detection, manual path selection, and validation
- Native drag and drop for `.lua`, `.manifest`, and `.zip` files
- Safe validation and dry-run planning before writes
- AppID-based configuration bundle retrieval through the command-line interface
- Visual Steam library with metadata and locally cached cover artwork
- Offline artwork and metadata fallbacks using the bundled night-injection logo
- Local operation history backed by SQLite
- Searchable, level-colored logs with copy, clear, export, and auto-scroll controls
- Persisted window, cache, Steam path, navigation, and feedback preferences
- Background network and filesystem work to keep the interface responsive
- Standalone, console-free Windows release built with PyInstaller

## Requirements

- Windows 10 or Windows 11
- Python 3.11 or later for development
- Steam for live injection and library workflows

The packaged release does not require a separate Python installation.

## Installation

For end users, open:

```text
dist\night-injection\night-injection.exe
```

For development, create and activate a virtual environment, then install dependencies:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
```

## Development

Launch the desktop application:

```powershell
python gui.py
```

Use the command-line interface when scripting is preferable:

```powershell
python main.py --help
python main.py --steam-path "C:\Program Files (x86)\Steam" verify
python main.py --steam-path "C:\Program Files (x86)\Steam" --dry-run inject game.lua
```

Run the automated tests:

```powershell
python -m unittest discover -s tests -v
```

## Build

Create the Windows release from PowerShell:

```powershell
.\scripts\build.ps1
```

When dependencies are already installed:

```powershell
.\scripts\build.ps1 -SkipDependencies
```

The output is `dist\night-injection\night-injection.exe`. The build uses `night-injection.spec`, bundles the logo and GUI dependencies, and suppresses the console window.

## Project structure

```text
core/        Runtime paths, configuration, errors, logging, and settings
covers/      Local and remote Steam artwork and metadata resolution
installer/   Preserved loader planning and installation workflow
lua/         Validation, fetch, import, apply, and removal operations
services/    Business-logic orchestration used by the GUI and CLI
steam/       Steam discovery and processed-library scanning
storage/     SQLite persistence
ui/          Desktop controller, page views, reusable widgets, and theme
tests/       Sandbox and runtime regression tests
assets/      Bundled application logo and generated Windows icon
scripts/     Reproducible release build script
```

## Usage

1. Open **Settings** and verify the detected Steam path. A valid directory contains `steam.exe`.
2. Drag supported files onto the Dashboard or Inject drop zone, or use **Browse files**.
3. Optionally enable **Dry run** to preview destination paths without writing.
4. Select **Inject selected files** and confirm the operation.
5. Use **Library** to review processed AppIDs, Steam names, cover artwork, and dates.
6. Use **History** and **Logs** to review operations and diagnostics.

An AppID can also be entered on the Inject page. That workflow preserves the original ordered repository lookup and file-filtering rules.

## Configuration and data

Writable state is stored under `%LOCALAPPDATA%\night-injection`:

- `settings.json` — application preferences
- `night-injection.db` — operation history and compatible library data
- `cache\covers` — downloaded artwork
- `cache\steam_metadata.json` — resolved Steam names
- `logs\night-injection_YYYYMMDD.log` — detailed diagnostics

Settings are written atomically. Cached artwork can be cleared from the Settings page.

## Logging

The Logs page shows timestamped `DEBUG`, `INFO`, `WARNING`, `ERROR`, and success-style events. Normal user feedback stays concise; detailed exception context is written to the daily log file when debug logging is enabled.

## Troubleshooting

- **Steam not found:** select the Steam installation directory in Settings and choose **Verify**. Select the folder containing `steam.exe`, not `steamapps`.
- **Permission denied:** close Steam if it has locked a destination file. If Steam is installed in a protected directory, start night-injection with appropriate access.
- **No covers or names:** confirm internet access and enable Steam metadata in Settings. Existing cached content and the bundled placeholder remain available offline.
- **Invalid ZIP:** verify that the archive is readable and contains `.lua` or `.manifest` files. Unsafe paths inside ZIP files are rejected.
- **No library entries:** the visual library is based on numeric `.lua` filenames in `<Steam>\config\lua`.
- **Drag and drop unavailable:** reinstall dependencies from `requirements.txt`; file browsing remains available as a fallback.
