You can either register for shell notifications or file system notifications.
There are two ways to register for shell notifications.  IShellChangeNotify, SHChangeNotifyRegister.
This library uses SHChangeNotifyRegister.

Both `IShellChangeNotify` and `SHChangeNotifyRegister` are mechanisms for receiving shell notifications (like file creation, deletion, or renaming), but they differ in how they are implemented and the architectural level at which they operate.

### SHChangeNotifyRegister
This is the **classic Win32 function-based approach**. It is the most common way for standard desktop applications to listen for shell changes.

*   **Mechanism**: You provide a window handle (`HWND`) and a specific message ID (usually `WM_USER + some_number`).
*   **Delivery**: When a shell event occurs, Windows posts the specified message to your window's message queue. You then catch this in your `WindowProc`.
*   **Use Case**: Ideal for traditional Win32 or MFC applications that already have a message loop and want a straightforward way to update a UI list or tree view.
*   **Workflow**:
    1.  Call `SHChangeNotifyRegister` with your `HWND`, the events you care about (e.g., `SHCNE_ALLEVENTS`), and the paths you want to watch.
    2.  In your `WndProc`, handle the custom message.
    3.  Call `SHChangeNotifyUnregister` when your window is destroyed.

### IShellChangeNotify
This is the **modern COM-based interface approach**. It is more flexible but requires your object to implement a COM interface.

*   **Mechanism**: You implement the `IShellChangeNotify::OnChange` method in a COM object.
*   **Delivery**: The Shell calls your implementation of `OnChange` directly.
*   **Use Case**: Primarily used by Shell Extensions, Namespace Extensions, or modern C++ applications that prefer a callback-driven architecture over a window-message-driven one. It is often used in conjunction with `SHChangeNotifyRegister` by passing the `SHCNRF_NewDelivery` flag, which allows the event to be delivered via this interface rather than a window message.
*   **Workflow**:
    1.  Implement the `IShellChangeNotify` interface in your class.
    2.  Register your interest using `SHChangeNotifyRegister` (or internal shell registration methods), specifying that you want COM delivery.
    3.  The system calls your logic whenever a change occurs, passing the event ID and the PIDLs (Pointer to an Item Identifier List) of the items involved.

### Key Differences

| Feature | SHChangeNotifyRegister (Standard) | IShellChangeNotify |
| :--- | :--- | :--- |
| **Communication** | Window Messages (`SendMessage`/`PostMessage`) | COM Interface Callbacks (`Invoke`/`OnChange`) |
| **Requirements** | Requires a valid `HWND` and a message loop | Requires a COM object implementation |
| **Complexity** | Easier for simple UI-based tools | Slightly higher (COM boilerplate) |
| **Flexibility** | Limited to the window thread | Can be easier to integrate into non-UI logic |
| **Pidl Handling** | You must unpack `WPARAM` and `LPARAM` | PIDLs are passed directly to `OnChange` |

### Which should you use?
If you are writing a standard Windows application with a UI, **`SHChangeNotifyRegister`** is generally the better choice because it plugs directly into your existing message handling. If you are building a headless service or a highly decoupled system where you don't want to manage a hidden window just to receive messages, **`IShellChangeNotify`** is the more robust, albeit "heavier," architectural choice.




Windows provides fine-grained updates (like SHCNE.CREATE, DELETE, and RENAMEITEM), and as you can see in
  CShellItemUpdater.cs, the code does try to handle them individually.

  However, relying only on those individual messages is dangerous for a few critical reasons:

  1. The "Message Queue" Problem
  Windows Shell notifications are delivered as window messages (WM_USER + 200). Under high load (like your 100,000 file
  operation):
   * Messages can be dropped: If the message queue fills up, Windows will stop sending individual file updates.
   * Messages can be coalesced: Instead of sending 10,000 CREATE messages, Windows will often send a single UPDATEDIR
     message. This essentially says: "Hey, too much is happening in this folder for me to list everything. Just go check
     the folder yourself."

  2. Atomic Operations vs. Multiple Notifications
  Some operations (like a complex Save or a specialized installer) don't trigger a simple CREATE. They might move
  temporary files, delete old ones, and rename others so quickly that the individual notifications are unreliable or out
  of order. UPDATEDIR is the OS telling the app to "re-sync" its state to be 100% sure.

  3. Missing Notifications
  The Windows Shell notification system is notorious for occasionally "missing" a message, especially on network drives
  or during heavy I/O. Without UpdateRefresh, if your app misses one DELETE message, that file would stay in your UI
  forever (a "ghost" file), even though it's gone from disk.

  4. Initialization
  When you first open a folder, the app doesn't have an "old set" at all. It has to examine the entirety of the contents
  to build its initial internal cache.









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