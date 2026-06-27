When a file is moved on Windows, the shell broadcasts change notifications via `SHChangeNotify` (received by listeners through `SHChangeNotifyRegister` / `IShellChangeNotify::OnChange`). The exact events and order depend on whether it's a **same-volume move (rename)** or a **cross-volume move (copy + delete)**.

### Same-volume move (true rename)
The shell treats this as a rename, so you get a single paired event:

| Order | Notification | Meaning |
|-------|-------------|---------|
| 1 | `SHCNE_RENAMEITEM` (file) or `SHCNE_RENAMEFOLDER` (folder) | Item moved from old path to new path. `dwItem1` = old PIDL, `dwItem2` = new PIDL |

Optionally followed by directory-update events for the parent folders:

| 2 | `SHCNE_UPDATEDIR` | Source and/or destination folder content refreshed |

### Cross-volume move (copy then delete)
Because the bits are physically copied to the new volume and removed from the old one, the shell does **not** fire a rename. Instead you typically see:

| Order | Notification | Meaning |
|-------|-------------|---------|
| 1 | `SHCNE_CREATE` (file) / `SHCNE_MKDIR` (folder) | Item created at destination |
| 2 | `SHCNE_UPDATEITEM` / `SHCNE_UPDATEDIR` | Destination item/folder updated (data written) |
| 3 | `SHCNE_DELETE` (file) / `SHCNE_RMDIR` (folder) | Source item removed |
| 4 | `SHCNE_UPDATEDIR` | Source parent folder refreshed |

### Key points and caveats
- **Ordering is not strictly guaranteed.** The shell coalesces and may delay/reorder events, especially when `SHCNF_FLUSH`/`SHCNF_FLUSHNOWAIT` aren't used. Treat the above as the typical sequence, not a contract.
- For **folder** moves, the parent gets `SHCNE_UPDATEDIR` and children may generate their own events on a cross-volume move.
- Bulk/large operations can be collapsed into a single `SHCNE_UPDATEDIR` instead of per-item events (the shell falls back to "just refresh the directory").
- Underlying filesystem notifications (`ReadDirectoryChangesW`) differ: a same-volume rename produces `FILE_ACTION_RENAMED_OLD_NAME` then `FILE_ACTION_RENAMED_NEW_NAME`; a cross-volume move produces `FILE_ACTION_ADDED` at the target and `FILE_ACTION_REMOVED` at the source.

If you tell me which API layer you're listening on (shell `IShellChangeNotify`, `SHChangeNotifyRegister`, or filesystem `ReadDirectoryChangesW`), I can give you the precise event constants and a code snippet.