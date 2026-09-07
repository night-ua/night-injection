# Architecture

## Solution map

```text
NightInjection.sln
├─ src/NightInjection.Core
│  ├─ Models       immutable domain records and settings
│  ├─ Interfaces   service/repository boundaries
│  ├─ Results      operation and validation results
│  ├─ Services     repository-specific Lua rules
│  └─ Validation   AppID validation
├─ src/NightInjection.Infrastructure
│  ├─ Configuration  paths and atomic settings
│  ├─ Filesystem     planning, execution, rollback, safe ZIP
│  ├─ Steam          detection, directory map, library scan, loader plan
│  ├─ Networking     AppID, metadata, artwork
│  ├─ Storage        Microsoft.Data.Sqlite repositories and migration
│  ├─ Logging        in-memory stream and daily file provider
│  └─ DependencyInjection
├─ src/NightInjection.UI
│  ├─ Views / ViewModels
│  ├─ Services       dialogs, pickers, clipboard, theme
│  ├─ Resources      centralized color/type/spacing/card/control tokens
│  ├─ Converters / Messages
│  └─ App + MainWindow
├─ installer/NightInjection.Setup
│  └─ self-contained per-user Setup, shortcut and uninstall registration
├─ build.ps1              one-command build/test/Portable/Setup pipeline
└─ tests/NightInjection.Tests
```

## Runtime flow

```text
WinUI view
   │ data binding / command
   ▼
MVVM ViewModel
   │ interface + CancellationToken
   ▼
Core contract ──────► Infrastructure implementation
                         ├─ verified Steam paths
                         ├─ bounded HttpClient requests
                         ├─ safe local filesystem
                         ├─ cache/settings JSON
                         └─ SQLite history
```

`App` owns a Generic Host and composes dependencies once. Pages receive their ViewModels through constructor injection. View code-behind is restricted to view-specific behavior: window navigation, picker window ownership, drag/drop extraction, dialog XAML roots, and list scrolling.

## Planning and execution

`IInjectionService.BuildFilePlanAsync` normalizes and validates inputs, expands ZIP files only into an owned temporary directory, calculates exact allowed destinations, reports collisions and overwrites, and returns an immutable `InjectionPlan`. It does not create Steam target folders.

`ExecutePlanAsync` re-verifies Steam and every destination, stages copies next to their final destinations, backs up overwritten files, commits the set, rolls back committed files on an expected I/O failure, writes history, and removes staging artifacts. Dry Run is therefore a plan display, not conditionals scattered through the write path.

## Async and caching

- `HttpClient` instances are created by DI with decompression, redirect limits, timeouts, and connection lifetime control.
- Library metadata/artwork work is bounded to six parallel items.
- Metadata, artwork, and library snapshots are cached independently.
- SQLite uses asynchronous APIs and WAL mode.
- File logs use a bounded channel and background writer.
- No `.Result` or `.Wait()` is used on the UI thread.

## Distribution

`build.ps1` produces one portable ZIP and one self-contained Setup executable. Setup embeds the exact verified portable ZIP, extracts only safe relative entries into a staged per-user location, swaps an existing installation only after extraction succeeds, creates the Start menu shortcut, optionally creates a Desktop shortcut, and registers an uninstaller under HKCU. It uses native Windows dialogs and shell links without a third-party installer dependency.
