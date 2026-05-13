Windows does provide fine-grained updates (like SHCNE.CREATE, DELETE, and RENAMEITEM), and as you can see in
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



