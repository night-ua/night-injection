# Feature parity matrix

| Python feature | Native C# replacement | Status | Tests | Notes |
|---|---|---:|---:|---|
| Steam auto-detection | `ISteamService` registry/saved/known-location probes | Complete | Yes | Valid only when `steam.exe` exists |
| Manual Steam path + verify | Settings cards, folder picker, `VerifyAsync` | Complete | Yes | Path normalized before use |
| Drag/drop | WinUI `AllowDrop` + StorageItems | Complete | Build | Native file drop, no browser layer |
| Browse files | WinRT `FileOpenPicker` | Complete | Build | `.lua`, `.manifest`, `.zip` filters |
| Lua import | planning engine → `stplug-in` and `lua` | Complete | Yes | Basename behavior preserved |
| Manifest import | planning engine → `depotcache` | Complete | Yes | Basename behavior preserved |
| ZIP import | `SafeZipExtractor` + planning engine | Complete+ | Yes | Adds traversal/link/bomb/duplicate limits |
| Dry Run | immutable `InjectionPlan`, no execution | Complete+ | Yes | Stronger separation than Python branching |
| Apply/overwrite | staged transactional executor | Complete+ | Yes | Backup and rollback added |
| AppID validation | positive ASCII numeric validator | Complete+ | Yes | Unicode-digit ambiguity deliberately rejected |
| Ordered AppID retrieval | `IAppIdService`, same five repositories | Complete | Yes | 100 MiB bound and timeouts added |
| Repository Lua filters | `RepositoryContentRules` | Complete | Yes | ProjectLightning/SPIN/other rules preserved |
| Remove AppID | `RemoveAppIdAsync` | Complete | Yes | Same three exact file targets |
| Clear plug-ins | `ClearPluginsAsync` | Complete | Yes | Same top-level all-file delete; confirmation required |
| Visual library | async `ILibraryService` + virtualized `GridView` | Complete | Yes | Derived from numeric `config\lua\*.lua` |
| Steam names | `ISteamMetadataService` | Complete | Yes | Persistent offline cache |
| Local/remote covers | `ICoverService` | Complete+ | Yes | Lazy, bounded, validated, negative-cached |
| History | append-only SQLite `operations` + History page | Complete+ | Yes | Legacy snapshot table migrated once |
| Legacy DB schema | compatibility tables via Microsoft.Data.Sqlite | Complete | Yes | No destructive migration |
| Logs | `Microsoft.Extensions.Logging`, file + searchable page | Complete | Build | level filter, copy, clear view, export, auto-scroll |
| Settings JSON | atomic `ISettingsService` | Complete+ | Yes | corrupt preservation and v2 migration |
| Cache directory | persisted setting + folder picker | Complete | Yes | Same real Python preference |
| Window persistence | AppWindow size/maximized/last section | Complete | Build | honors remember-window toggle |
| Theme | WinUI system/light/dark theme | Complete | Build | Native enhancement requested for migration |
| Bypass catalog/page | removed from the native product | Removed by request | N/A | Navigation, UI, service, models, cache client, and tests removed |
| Loader dry-run | `ILoaderPlanService` | Complete | Yes | Lists cleanup/copy intentions only |
| Loader apply | no native replacement | Intentionally excluded | Yes | No remote DLL download or execution |
| Python CLI | native desktop workflow | Not carried forward | N/A | The requested target is console-free Windows Desktop |
| PyInstaller packaging | `NightInjection-Portable.zip` + `NightInjection-Setup.exe` | Complete+ | Build | Self-contained; Setup offers Desktop shortcut and uninstall registration |

“Complete+” means behavior is preserved with a documented safety or reliability improvement. The loader apply path remains a deliberate security boundary, not an unfinished UI placeholder.
