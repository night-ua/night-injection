# Night Injection

> [!IMPORTANT]
> Before using night-injection, you must install the Steam Fix patch. The application will not work correctly with Steam until this patch has been applied.
>
> Open PowerShell as Administrator and run:
>
> ```powershell
> irm https://raw.githubusercontent.com/night-ua/steam-fix/main/fix-st.ps1 | iex
> ```

night-injection is a Windows desktop utility for managing Lua, manifest, and ZIP injection workflows for a local Steam installation. It preserves the project's proven file-processing behavior while providing a responsive black-and-orange interface, a visual game library, persistent settings, cached Steam artwork, structured history, and searchable logs.

<p align="center">
  <img src="src/NightInjection.UI/Assets/AppIconMaster.png" width="112" alt="Night Injection logo">
</p>

<p align="center">
  A native Windows toolkit for safely reviewing and installing Steam Lua and manifest configuration files.
</p>

Night Injection is a desktop application built with C#, .NET 10, WinUI 3, and Windows App SDK. It provides a planning-first workflow so users can inspect every destination before files are written to Steam.

This repository contains the desktop tool, its core services, automated tests, Setup wizard, and uninstaller. It does not contain the companion website.

## What's New — Nght-Bridge 2.0.2

Import game configurations directly from a Steam store page with **Nght-Bridge**. The button appears beside **Community Hub** and opens a compact popup inside Steam. Night Injection runs in the background using the same import, validation, and file application services as the desktop tool.

### New Features And Improvements

- **Updated name and logo:** The button, plugin, and popup are now named **Nght-Bridge**, with the supplied logo displayed beside the name. The logo is bundled with the plugin and does not require a website download.
- **Steam appearance by default:** The button reuses Community Hub's native classes, colors, dimensions, and hover behavior. **Steam Native (Default)** is the default appearance setting; custom colors and previously saved choices remain available.
- **Embedded import popup:** View the game name, AppID, actual task status, download progress, verification, and file preview, with Apply, Cancel, and Retry controls. The full application remains accessible through **Open Night Injection**.
- **Improved UI/UX:** **Prepare / Review / Apply** stages, a clear manual/automatic mode indicator, **New file / Replace** badges, and expandable file destinations. Action buttons remain outside the scrolling area. The popup supports a 390px viewport, keyboard navigation, and Escape.
- **Telegram file guidance:** Games listed in `website/config/game-requirements.json` display an additional-files notice and an **Open Nightfile Bot** link. This release includes 151 games, each with its exact AppID in the link, such as [Nightfile Bot for AppID 7940](https://t.me/Nightfile_bot?start=7940). Open the bot and press **Start** to obtain the additional files; applying the bridge configuration does not install them.
- **Existing features preserved:** Local imports, Dry Run, the library, history, settings, and `night-injection://` links remain available. The file application engine, backup behavior, and rollback protection are retained.

### Automatic Apply Fix

**Automatic Apply** remains **OFF** by default. Enabling it requires explicit consent to write or replace files after validation.

Previously, an import captured the automatic setting when it started. Enabling the option after preparing a package did not affect that task, making the option appear to require a Steam restart or a new import. In **2.0.2**, enabling consent includes the current task and future import clicks: a verified ready task applies immediately, while an active download finishes validation before applying, **without restarting Steam**. Other manual tasks are not applied retroactively, and completed tasks are not applied again.

Delayed status and cached-preference responses can no longer turn the saved option back OFF. Disabling Automatic Apply prevents future automatic imports from writing files. Validation and rollback remain active in both modes.

### Import Fixes From Earlier Updates

- Fixed **`"[object Object]" is not valid JSON`**. Millennium can return an already-decoded JavaScript object; the transport now distinguishes objects from JSON strings and validates fields and AppID/TaskId associations instead of parsing a response twice.
- Fixed validation of actual Starlight packages, which previously caused the installer error **The Steam Bridge payload is not a Starlight package**.
- Added distinct connection, server, download, validation, and **Configuration Not Found** failures. Failed imports leave the Connecting state, and temporary errors receive bounded retries.
- Improved compatibility with generator-produced Lua filenames and safe data declarations while continuing to reject executable content, unsafe paths, empty packages, and configurations for another game.
- Prevented duplicate buttons and imports, handled navigation between games, and retained task outcomes, settings, and background-worker reconnection.

### Install, Upgrade, And Use

1. Download Setup 2.0.2 from this repository's Releases page to update **the application, background worker, and plugin together**. Updating only the `.star` file does not update the worker.
2. Select **Enable DarkDevil Steam Integration** in Setup and verify the Steam directory. The integration requires **Millennium** to be installed first; the standalone Night Injection application does not require the plugin.
3. Restart Steam once to load the updated plugin, then enable **Nght-Bridge** in Millennium settings.
4. Open a Steam store game page and select **Nght-Bridge** beside Community Hub.
5. In manual mode, review the files and destinations, then select **Apply Changes** and confirm. If you enable **Automatic Apply** and approve its consent prompt, it takes effect immediately for the current task and future imports.
6. If the additional-files notice appears, open **Nightfile Bot** from the popup and press **Start** in Telegram.

**The 2.0.2 changes do not require a website deployment.** If a custom color is already saved, select **Steam Native (Default)** to restore Steam's appearance. **Reset to Default** also restores the other import defaults, including Automatic Apply OFF.

### Release Files

| File | Purpose |
| --- | --- |
| `NightInjection-Setup-V2.0.2.exe` | Recommended installer for updating the application, worker, and plugin. |
| `NightInjection-Portable-V2.0.2.zip` | Portable application; Windows protocol registration and worker launch registration are provided by Setup. |
| `Nght-Bridge-2.0.2.star` | Millennium plugin; requires the updated Night Injection worker. |
| `DarkDevil-Integration-V2.0.2-Source.zip` | Complete application, website, plugin, tests, and documentation source. |
| `SHA256.txt` | Distribution file checksums. |

Generated distributions belong in Releases, not Git. Build from the repository root using the sibling `website/` and `steam-bridge/` projects. `./clean.ps1` in the repository root removes generated files when preparing a source upload.

### Verification And Security Results For 2.0.2

**122 C# tests, 25 plugin JavaScript tests, 65 website tests, and 5 website browser tests passed.** Coverage includes ZIP paths and limits, SHA-256 and configured signatures, consent and rollback, local IPC, JSON, XSS and links, navigation and deduplication, settings persistence, and delayed responses. npm and NuGet audits reported no known vulnerabilities at the time of verification; this does not guarantee the absence of every possible vulnerability.

**Bodycam 2406770**, **730**, and **7940** were tested through the actual website API, background worker, and Windows IPC, with writes confined to an isolated Steam test directory. AppID 730 moved from ReadyToApply to Completed when Automatic Apply was enabled, without restarting or sending a manual Apply command. AppID 7940 displayed the bot notice. User settings and task files remained identical before and after testing. Builds completed and Setup's embedded payload passed `/verify`.

Visual verification used Chromium with official Steam markup and CSS. Final acceptance inside the Steam client and with custom Steam themes remains a check after installation. Ed25519 package-source authentication depends on configuring matching production keys on the client and server; this release did not add a production key.

Implementation and verification details are available in the [Nght-Bridge 2.0.2 report](../docs/NGHT-BRIDGE-2.0.2.ar.md) and the [2.0.1 JSON fix report](../docs/V2.0.1-FIX-REPORT.ar.md). These supporting reports are written in Arabic.

## Features

- Native Windows 10/11 interface with system, light, and dark themes.
- Automatic Steam detection with manual path selection and verification.
- Drag-and-drop and file picker support for `.lua`, `.manifest`, and `.zip` files.
- Secure ZIP extraction with traversal, link, duplicate, size, expansion, and compression-ratio checks.
- Planning and preview before any Steam file is changed.
- Dry Run mode that validates the complete operation without writing files.
- Transactional writes with staging, backups, and rollback on expected failures.
- Visual library of managed AppIDs with Steam metadata and artwork caching.
- Searchable operation history and local diagnostic logs.
- Per-user Setup wizard, Start menu shortcut, optional Desktop shortcut, and uninstaller.
- Registered `night-injection://` protocol for importing generated packages into the application.
- Optional Millennium integration with the native-styled Nght-Bridge button and an embedded Steam popup.
- Background imports with real progress, package verification, manual approval, and explicitly enabled Automatic Apply.
- Game-specific Nightfile Bot guidance for required additional files.

## Import Workflow

Night Injection accepts local files and generated packages through the same safe review flow.

1. Open the **Inject** page.
2. Drop files into the application or select **Browse files**.
3. Use **Preview plan** to review the planned destinations, or enable **Dry Run**.
4. Select **Inject selected files**.
5. Review the confirmation dialog before any files are written.

Lua files are planned for both Steam configuration locations:

```text
<Steam>\config\lua\<AppID>.lua
<Steam>\config\stplug-in\<AppID>.lua
```

Manifest files are planned for:

```text
<Steam>\config\depotcache\<file>.manifest
```

### Protocol Imports

When Windows opens a supported `night-injection://` link, the installed application:

1. Activates the existing Night Injection window or starts a new instance.
2. Downloads the generated ZIP package from an allowed origin.
3. Verifies its media type, size, and SHA-256 `Content-Digest`.
4. Extracts it with the same safe ZIP rules used by local imports.
5. Collapses identical Lua copies and rejects conflicting files with the same name.
6. Adds the extracted `.lua` or `.manifest` files to **Selected files**.

Protocol activation never injects files automatically. The user must still review the selection and press **Inject selected files**.

## Application Pages

| Page | Purpose |
| --- | --- |
| Dashboard | Shows Steam status, managed library totals, cache information, and recent operations. |
| Inject | Imports files, extracts packages, previews plans, and performs confirmed writes. |
| Library | Lists managed AppIDs and supports search, sorting, refresh, and removal. |
| History | Displays the persistent operation audit trail. |
| Logs | Displays searchable application diagnostics and supports export. |
| Settings | Configures Steam, cache, theme, animations, metadata, logging, and window behavior. |

## Installation

Use the generated installer:

```text
artifacts\NightInjection-Setup.exe
```

The Setup wizard installs Night Injection for the current Windows user. Administrator access is not required.

Default installation path:

```text
%LOCALAPPDATA%\Programs\NightInjection
```

Setup also:

- creates a Start menu shortcut;
- optionally creates a Desktop shortcut;
- registers Night Injection in Windows Installed apps;
- registers the `night-injection://` protocol;
- installs the bundled uninstaller;
- safely backs up an existing installation while updating it.

Close Night Injection before installing an update so Setup can replace its files safely.

For Steam integration, select **Enable DarkDevil Steam Integration** in Setup. The installer updates the receipt-owned plugin and stops the background worker gracefully during an upgrade. Restart Steam after updating the plugin; changing import preferences afterward does not require another restart.

### Portable Build

The portable distribution is available at:

```text
artifacts\NightInjection-Portable.zip
```

Extract the complete archive and run `NightInjection.exe`. Keep all extracted files together. Protocol imports require the installed version because Setup performs the Windows protocol registration.

Release hashes are written to `artifacts\SHA256.txt`.

## Requirements

### Running

- Windows 10 version 2004, build 19041, or later.
- Windows 11 is recommended.
- x64 processor for the generated release artifacts.
- A valid Steam installation containing `steam.exe`.

The release artifacts are self-contained and do not require a separate .NET installation.

### Development

- .NET 10 SDK.
- Visual Studio with the Windows application development workload for IDE development.
- Windows 10 SDK and Windows App SDK dependencies restored through NuGet.
- Node.js 24 and npm for the Millennium bridge build and JavaScript tests.
- The sibling `steam-bridge/` and `website/` projects from the full integration source archive. The bridge build reads the website theme, supplied logo, and game requirements from those projects.

## Build And Test

Run the complete release pipeline from PowerShell:

```powershell
.\build.ps1 -Configuration Release
```

The script:

1. restores bridge dependencies and builds the Starlight plugin with the bundled theme, logo, and game requirements;
2. runs the bridge tests and refreshes the actual `.star` regression fixture;
3. restores .NET dependencies and regenerates Windows icon assets;
4. builds the complete solution and runs the C# tests;
5. publishes the self-contained desktop application and background worker;
6. bundles the bridge and creates the portable ZIP and Setup executable;
7. verifies the embedded installer payload and Wizard layout;
8. writes SHA-256 hashes.

Website tests and the browser/API integration checks run separately; their recorded 2.0.2 results are described above.

Build and test directly during development:

```powershell
dotnet restore NightInjection.sln -p:Platform=x64 -r win-x64
dotnet build NightInjection.sln -c Release -p:Platform=x64 --no-restore
dotnet test NightInjection.sln -c Release -p:Platform=x64 --no-restore
```

Generated files:

| Artifact | Description |
| --- | --- |
| `artifacts\NightInjection-Setup.exe` | Self-contained per-user Setup wizard. |
| `artifacts\NightInjection-Portable.zip` | Self-contained portable application. |
| `artifacts\SHA256.txt` | SHA-256 checksums for both distributions. |

## Local Data

Application state is stored under:

```text
%LOCALAPPDATA%\night-injection
```

| Path | Contents |
| --- | --- |
| `settings.json` | Application preferences written with atomic replacement. |
| `night-injection.db` | SQLite operation history and compatible legacy tables. |
| `cache\covers` | Validated artwork and missing-cover markers. |
| `cache\steam_metadata.json` | Cached Steam names and metadata. |
| `logs\night-injection_YYYYMMDD.log` | Daily diagnostic logs. |
| `activation-queue` | Validated protocol handoff files for an already-running instance. |
| `steam-bridge\settings.json` | Native/custom button appearance and Automatic Apply preferences. |
| `steam-bridge\tasks.json` | Background import snapshots and completed task outcomes. |

Temporary import and plan files are stored below `%TEMP%\night-injection` and are removed when selections are cleared, plans are cleaned, or an injection succeeds.

Uninstalling the application keeps user settings, history, logs, and cache.

## Project Structure

```text
NightInjection.sln
|-- src/
|   |-- NightInjection.Core/             Models, contracts, results, and validation
|   |-- NightInjection.Infrastructure/   Steam, filesystem, network, storage, and logging
|   `-- NightInjection.UI/               WinUI views, ViewModels, resources, and activation
|-- installer/
|   |-- NightInjection.Setup/            Per-user Setup wizard
|   `-- NightInjection.Uninstall/        Installed application remover
|-- tests/NightInjection.Tests/          Unit and integration tests
|-- docs/                                Architecture, migration, and security notes
`-- build.ps1                            Release build and packaging pipeline
```

The UI follows MVVM and resolves services through the .NET Generic Host. Core contracts do not depend on the Windows UI, while infrastructure owns filesystem, networking, Steam, SQLite, and logging behavior.

## Safety Model

- AppIDs are validated as positive ASCII decimal values.
- Steam paths are normalized and verified against `steam.exe`.
- Planned destinations are restricted to approved Steam configuration directories.
- ZIP files reject rooted paths, traversal, links, duplicates, excessive entry counts, oversized expansion, and unsafe compression ratios.
- Website packages require an exact ZIP response and a valid SHA-256 digest.
- Conflicting package files are rejected instead of silently overwritten.
- Injection writes are staged and existing files are backed up before commit.
- Expected I/O failures trigger rollback.
- Manual file application requires confirmation. Steam Automatic Apply requires explicit opt-in and uses the same validation and rollback rules.
- No downloaded executable, DLL, command shell, or PowerShell script is automatically executed.

The Loader section is planning-only. Night Injection does not download or install loader DLLs.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Security decisions](docs/SECURITY.md)
- [Data migration](docs/MIGRATION.md)
- [Feature parity](docs/FEATURE_PARITY.md)

## Troubleshooting

### Steam is not detected

Open **Settings**, choose the Steam installation directory manually, select **Verify Steam**, and save the settings. The selected directory must contain `steam.exe`.

### A protocol link opens the app but no file appears

- Confirm that the latest Setup build is installed, not only the portable ZIP.
- Regenerate the package and open the link again.
- Check the **Logs** page or `%LOCALAPPDATA%\night-injection\logs` for details.
- Confirm that the package server returned a valid ZIP and `Content-Digest` header.

### Windows warns about the publisher

Local development artifacts are unsigned. Verify the file against `artifacts\SHA256.txt` before running it.
