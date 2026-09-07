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

## Build And Test

Run the complete release pipeline from PowerShell:

```powershell
.\build.ps1 -Configuration Release
```

The script:

1. restores dependencies;
2. regenerates Windows icon assets;
3. builds the complete solution;
4. runs the automated tests;
5. publishes the self-contained desktop application;
6. creates the portable ZIP and Setup executable;
7. verifies the embedded installer payload and Wizard layout;
8. writes SHA-256 hashes.

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
- Destructive actions require confirmation.
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
