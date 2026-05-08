The CShellItem class in WindowsApiLib uses a specialized event system to keep your application's UI in sync with the
  Windows File System. These "update events" are driven by a hidden infrastructure that listens for shell notifications
  from Windows.

  The Core Event: CShItemUpdate
  The primary event is a static event defined in CShellItem.cs:

   1 public static event CShItemUpdateEventHandler CShItemUpdate;

  It uses ShellItemUpdateEventArgs, which tells you which Item changed and what UpdateType occurred.

  How it Works
   1. Listening: When the application starts, a hidden CShellItemUpdater (which is a Windows Control) is created. It
      registers with the Windows Shell using SHChangeNotifyRegister to listen for system-wide file changes.
   2. Detection: When you (or any other program/user) create, delete, or rename a file, Windows sends a message
      (WM_SHNOTIFY) to the CShellItemUpdater.
   3. Processing: The updater's WndProc identifies which CShellItem in the library's internal cache is affected.
   4. Notification: It calls methods like Update, AddItem, or RemoveItem on that CShellItem. These methods update the
      internal cache and then raise the CShItemUpdate event.

  What the Update Types Do
  The CShItemUpdateType enum defines several types of changes:

  ┌─────────────┬─────────────────────────────────┬─────────────────────────────────────────────────────────┐
  │ UpdateType  │ Meaning                         │ Typical Usage                                           │
  ├─────────────┼─────────────────────────────────┼─────────────────────────────────────────────────────────┤
  │ Created     │ A new file or folder appeared.  │ Add a new node to a tree or a row to a list.            │
  │ Deleted     │ A file or folder was deleted.   │ Remove the corresponding UI element.                    │
  │ Renamed     │ An item was renamed or moved.   │ Update the text label or move the item to a new parent  │
  │             │                                 │ folder.                                                 │
  │ Updated     │ Attributes (size, date, etc.)   │ Refresh the details shown in a "Details" view.          │
  │             │ changed.                        │                                                         │
  │ UpdateDir   │ Significant directory changes.  │ Triggers a full refresh of that folder's contents.      │
  │ IconChange  │ The item's system icon changed. │ Redraw the icon for that file or folder.                │
  │ MediaChange │ A drive (USB/CD) was            │ Refresh the drive list or the current view if it was on │
  │             │ inserted/removed.               │ that drive.                                             │
  └─────────────┴─────────────────────────────────┴─────────────────────────────────────────────────────────┘