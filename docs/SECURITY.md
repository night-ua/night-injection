# Security and stability decisions

- Remote repository ZIPs are limited to 100 MiB before extraction. Archives reject rooted/traversal paths, drive syntax, symlinks/reparse entries, case-insensitive duplicate outputs, more than 4,000 entries, entries over 128 MiB, total expansion over 512 MiB, and compression ratios above 250:1.
- AppIDs must be positive ASCII decimal values fitting in `UInt64`. This prevents path-like and visually ambiguous identifiers.
- Execution accepts destinations only below the verified Steam `config\stplug-in`, `config\lua`, or `config\depotcache` folders.
- Files are copied to unique staging names, existing destinations are backed up, and expected failures trigger rollback. Staging and owned plan directories are cleaned without accepting arbitrary recursive-delete targets.
- Network calls are asynchronous, bounded, cancellation-aware, and use HTTPS endpoints. Images are checked by file signature and size before entering the cache.
- Settings and metadata use write-then-move/replace. SQLite schema migration is transactional and uses WAL.
- Clear and remove operations expose exact, narrowly scoped behavior and destructive UI actions require confirmation.
- No PowerShell, command shell, downloaded executable, DLL, or third-party patch is automatically executed.
- The Python README's `irm ... | iex` Steam-fix instruction is intentionally not reproduced or run.
- Loader installation remains unavailable. Its service provides a validation/compatibility plan only, making the missing authority and external dependency explicit to the user.
