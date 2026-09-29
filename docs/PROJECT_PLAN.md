# WitchDrawer Project Plan

## MVP
- Implement normal boxes: real file/folder move into the app data directory.
- Implement mapping boxes: absolute-path references without moving files.
- Implement quick panel: `Ctrl+Alt+W`, all indexed items, search, and Shell open.
- Persist boxes and items in SQLite.
- Deleting a whole normal/pixel/drawer box restores its remaining items to their original locations (desktop fallback if missing); mapping boxes only remove references.
- Deletion semantics per box type: deleting an item from a mapping box removes only the stored reference and never touches the source file; deleting an item from a normal/pixel/drawer box or a bound box removes the file from the box (for bound boxes, from the bound folder too, since it is two-way synced) but routes it through the recycle bin first, and restoring puts it back into the same box or folder.
- In-app recycle bin: deleting an item from a normal/pixel/drawer box moves PODO-owned files into `%LocalAppData%\PODO\Recycle\{EntryId}` and records them in SQLite instead of destroying them. Entries live 30 days, are purged automatically on startup, and can be restored (back into the box when possible, otherwise to the original directory with desktop fallback) or permanently deleted from the recycle bin page. Mapping boxes keep storing references only and are never moved into the recycle bin; bound boxes do use the recycle bin and restore back into the bound folder.

## Next Milestone
- Target boxes bound to existing folders with two-way file-system sync.
- Rename and archive workflows for normal boxes.
- Tray icon, startup setting, and installer.
- Icon extraction and thumbnail cache with strict background processing.

## Later Milestone
- Magnetic access window attached to standard open/save dialogs.
- Split quick panels and browser-like file tabs.
- Performance instrumentation for launch time, hotkey latency, list render latency, and memory use.

## Acceptance Gates
- `dotnet build WitchDrawer.sln` passes.
- `dotnet test WitchDrawer.sln` passes.
- Manual smoke test covers drag into normal/mapping boxes, quick panel search, open, and delete.
- Manual smoke test covers recycle bin: delete from a normal box, restore it, then permanently delete it.
- Manual smoke test covers per-box-type deletion: deleting from a mapping box leaves the source file untouched; deleting from a normal/pixel/drawer or bound box removes the file and restoring it from the recycle bin puts it back into the same box or bound folder.

