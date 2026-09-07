# Python repository audit

Audit source: `C:\Users\devil\Documents\night-injection-main`. All tracked source, tests, scripts, configuration, and assets were inspected before the native implementation was created.

## 1. Current architecture map

```text
main.py / gui.py
├─ ui/
│  ├─ cli.py                 argparse command surface
│  ├─ gui.py                 CustomTkinter controller, workers, navigation
│  ├─ pages.py/widgets.py    page and reusable widget construction
│  ├─ bypass_page.py         catalog browser/detail flow
│  └─ theme.py               black/orange style constants
├─ services/
│  ├─ lightning_service.py   injection/AppID/removal/library orchestration
│  └─ bypass_service.py      remote catalog, cache, normalization, preflight
├─ lua/
│  ├─ validate.py            AppID rules
│  ├─ fetch.py               ordered GitHub branch ZIP retrieval/extraction
│  ├─ base.py                repository filters and copy behavior
│  ├─ add.py                 AppID workflow, dry-run shadow, rollback
│  ├─ importer.py            dropped-file workflow
│  ├─ remove.py              exact AppID removal and clear-all plug-ins
│  └─ apply.py               exported operation surface
├─ steam/
│  ├─ discovery.py           registry/known-path detection and directory map
│  └─ library.py             numeric Lua scan and local cover discovery
├─ covers/
│  ├─ cache.py               persistent validated artwork cache
│  ├─ local.py               Steam librarycache lookup
│  └─ remote.py              Steam appdetails/name/artwork requests
├─ storage/database.py       sqlite3 schema and lightning snapshot history
├─ installer/loader.py       risky loader dry-run/apply implementation
├─ core/                     settings, runtime paths, logging, constants/errors
├─ tests/                    sandbox/runtime/DB/covers/catalog regressions
├─ scripts/build.ps1         dependency install and PyInstaller release
├─ night-injection.spec      console-free package and bundled assets
└─ assets/logo.{jpg,ico}
```

The GUI controller owns page navigation, background-thread submission, feedback, settings synchronization, and service replacement. Business behavior itself is mostly separated into `services`, `lua`, `steam`, `covers`, and `storage`, making a service-oriented C# redesign possible without line-by-line translation.

## 2. Feature inventory and behavior

| Area | Inputs | Outputs / side effects | Rules and edge cases |
|---|---|---|---|
| Steam discovery | optional saved folder, registry, fixed candidates | verified base path | first folder containing `steam.exe`; manual path is supported |
| Directory mapping | Steam root | `config\stplug-in`, `config\lua`, `config\depotcache`, `appcache\librarycache` | injection creates target parents only during apply |
| Local injection | file path/name/buffer, Dry Run | Lua to two folders; manifest to depotcache | ignores unsupported types; basename becomes destination name |
| ZIP injection | ZIP path/buffer | recursively distributes inner Lua/manifests | empty supported content is an error; Python extraction needed stronger safety bounds |
| AppID workflow | Steam path, AppID, dry-run, force | repository, planned/written paths, error | validates numeric ID, optional duplicate refusal, checks five repositories in fixed order |
| Repository rules | extracted files + repository URL | copied or filtered Lua/manifests | ProjectLightning copies both types; SPIN keeps `addappid(` lines; three other repos remove `setManifestid`; signature appended |
| Remove AppID | AppID | deletes exactly two Lua paths and one manifest | missing files are friendly success |
| Clear plug-ins | Steam path | deletes every top-level file in `stplug-in` | does not recurse into folders |
| Library | `config\lua` and metadata preference | AppID/name/cover/date/restart flag | numeric `.lua` stems only; recursive local cover name patterns; parallel names |
| Steam metadata | AppID | name/type-like metadata cache | appdetails endpoint; offline `Game <id>` fallback |
| Covers | local Steam cache, CDN candidates | local cached image or logo | validates image response, remembers misses, supports offline fallback |
| Bypass | public JSON catalog, publisher, game folder | normalized game details and preflight | 30-minute cache; official prerequisite links; fix distribution already deliberately stubbed |
| Loader | Steam folder, apply flag | plan or three proxy DLL writes | Python defaults to dry-run but apply can download/copy DLLs and delete conflicting files |
| History | operation snapshot | SQLite rows | `lightning_history` upserts one latest row per AppID; compatible `juegos` schema retained |
| Settings | JSON | loaded preferences | Steam/cache paths, animations, auto-scroll, window memory/geometry, last page, debug, metadata |
| Logging | events and exceptions | UI viewer + daily file | level filter/search/copy/clear/export/auto-scroll; detailed diagnostics stay on disk |
| Window | last geometry/section | persisted settings | geometry restored only when the preference is enabled |

## 3. Python dependency inventory

| Dependency | Where/why used | Native replacement |
|---|---|---|
| Python 3.11+ | runtime and application language | .NET 10 / C# |
| customtkinter | desktop controls and theme | WinUI 3 native controls and XAML resources |
| tkinterdnd2 | drag/drop bridge | WinUI `DragOver`/`Drop` with WinRT StorageItems |
| Pillow | artwork decoding/resize in Tk UI | WinUI `Image`/`BitmapImage`; cache validates image signatures |
| tkinter | dialogs/file pickers/base event loop | ContentDialog and WinRT pickers |
| sqlite3 | local relational storage | Microsoft.Data.Sqlite |
| urllib.request/error | HTTP downloads | typed `HttpClient` through DI |
| json/dataclasses | models, settings, caches | records + System.Text.Json |
| zipfile | archive extraction | System.IO.Compression with explicit safety validation |
| pathlib/os/shutil/tempfile | filesystem and temporary work | System.IO and owned temporary roots |
| logging | diagnostics | Microsoft.Extensions.Logging + custom provider |
| threading/concurrent.futures | GUI background work | async/await, Task, Parallel.ForEachAsync |
| argparse | CLI | not carried into the requested desktop-only target |
| PyInstaller | console-free Windows bundle | Windows App SDK packaging/self-contained publish |

## 4. C# replacement table

| Python component | C# component |
|---|---|
| `SettingsStore` | `ISettingsService` / `SettingsService` |
| runtime path helpers | `IAppPathService` / `AppPathService` |
| Steam discovery/config directories | `ISteamService` / `SteamService` |
| `import_dropped_files` + shadow Dry Run | `IInjectionService.BuildFilePlanAsync` + `ExecutePlanAsync` |
| `zipfile.extractall` | `SafeZipExtractor` |
| `add_app_id` / repository fetch | `IAppIdService` / `AppIdService` |
| `process_extracted_repo_files` | `RepositoryContentRules` + AppID plan builder |
| `load_steam_library` | `ILibraryService` / `LibraryService` |
| cover cache modules | `ICoverService` / `CoverService` |
| Steam appdetails helper | `ISteamMetadataService` / `SteamMetadataService` |
| sqlite `Database` | `IHistoryRepository` / `HistoryRepository` |
| Python logging setup | `AppLogProvider` and `IAppLogStore` |
| Bypass service/page | Removed from the native product at the product owner's request |
| loader module | non-mutating `ILoaderPlanService` |
| CustomTkinter controller/pages | Generic Host + DI + MVVM ViewModels + WinUI Pages |

## 5. Resulting solution structure

The implemented structure is documented in [ARCHITECTURE.md](ARCHITECTURE.md). It uses three production projects rather than placing Steam, storage, networking, and UI logic into one executable. There is no extra domain layer or ORM beyond what the repository needs.

## 6. Data/storage migration plan

See [MIGRATION.md](MIGRATION.md). In short: reuse the current LocalAppData root, deserialize existing settings keys, add versioned SQLite tables without drops, transactionally import legacy `lightning_history`, and reuse disposable caches.

## 7. UI page map

| Navigation | Purpose | Primary controls |
|---|---|---|
| Dashboard | Steam/cache/activity health and quick actions | cards, InfoBar, progress, recent ListView |
| Inject | local file/ZIP and AppID planning/execution | native drop zone, picker, ListViews, Dry Run toggle, confirmation dialog |
| Library | visual processed AppID collection | AutoSuggestBox, sort ComboBox, virtualized GridView |
| History | persistent operation audit | search/filter, ListView table, export, confirmed clear |
| Logs | live application events | search/level filter, copy, clear view, export, auto-scroll |
| Settings | real persisted preferences and safety plan | SettingsCard/SettingsExpander, theme, paths, toggles, cache, Steam verify |

## 8. Migration risks and mitigations

| Risk | Impact | Mitigation in native build |
|---|---|---|
| Remote GitHub branch layouts can change | AppID lookup stops finding files | preserve ordered fallback, report repositories checked, log per-repository failures |
| Archive traversal/bombs/links | arbitrary write or resource exhaustion | prevalidate every entry and enforce counts/sizes/ratios |
| Steam files locked or protected | partial operation | preflight, staged copies, backups, rollback, friendly failure result |
| Duplicate basenames inside nested ZIPs | silent overwrite | case-insensitive destination collision invalidates plan |
| Interrupted settings/cache write | corrupt JSON | unique temporary file then atomic move/replace |
| Legacy database divergence | lost or duplicated history | additive schema, version table, transaction, unique legacy migration key |
| Offline Steam services | blank library | persistent metadata/artwork fallback and bundled placeholder |
| Large libraries freeze UI | poor UX | bounded parallel async work, virtualized controls, short snapshot cache |
| Python loader modifies Steam with downloaded DLLs | security and trust risk | explicit read-only plan; no download/apply implementation |
| UI/thread affinity | hangs or invalid cross-thread changes | async commands; UI-specific dispatch only for live log events |

## 9. Feature parity checklist

The live checklist and test state are maintained in [FEATURE_PARITY.md](FEATURE_PARITY.md). The Bypass area was removed by explicit product-owner request; the remaining audited user-facing Python GUI capabilities have a native service/page or a documented desktop-appropriate replacement.
