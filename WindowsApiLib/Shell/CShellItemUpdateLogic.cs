using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsApiLib.Util;
using WindowsApiLib;
using static WindowsApiLib.Shell.ShellAPI;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace WindowsApiLib.Shell
{
    public class CShellItemUpdateLogic<TPidl> where TPidl : ICPidl
    {
        private readonly CShellItemHierachyManager _hierarchyManager;
        private readonly IShellApiWrapper _shellApi;
        private readonly IFileSystem _fileSystem;
        private readonly IShellItemFactoryWrapper _shellItemFactory;
        private readonly LruConcurrentDictionary<string, bool> _activeDeletes = new(1000);
        private bool _isUpdatingDir = false;

        public event CShellItemUpdater.CShItemUpdateEventHandler? UpdateEvent; //we're not actually using this for anything right now.  really need to rethink this whole thing.

        public bool AllowUpdates { get; set; }

        public CShellItemUpdateLogic(
            CShellItemHierachyManager hierarchyManager,
            IShellApiWrapper shellApi = null,
            IFileSystem fileSystem = null,
            IShellItemFactoryWrapper shellItemFactory = null)
        {
            _hierarchyManager = hierarchyManager;
            _shellApi = shellApi ?? new ShellApiWrapper();
            _fileSystem = fileSystem ?? new FileSystemWrapper();
            _shellItemFactory = shellItemFactory ?? new ShellItemFactoryWrapper();
        }

        /// <summary>
        /// Handles windows file systems event notifications.
        /// 
        /// </summary>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        /// <remarks>Note that there is no MOVE event - moves are done with a DELETE and CREATE.</remarks>
        public void HandleNotification(IntPtr wParam, IntPtr lParam)
        {
            if (!AllowUpdates) return;

            IntPtr ppidl = IntPtr.Zero;
            var msgID = default(SHCNE);
            SHNOTIFYSTRUCT shNotify = default;
            IntPtr userPidl1 = IntPtr.Zero;
            IntPtr userPidl2 = IntPtr.Zero;
            var hLock = _shellApi.SHChangeNotification_Lock(wParam, (uint)lParam, ref ppidl, ref msgID); //note that the memory blocks pointed to by the params and pidl are owned by the OS, not user space. 
            if (hLock == IntPtr.Zero) return;

            try
            {
                if (!IsItemNotificationEvent(msgID)) return;

                msgID &= SHCNE.ALLEVENTS;
                shNotify = (SHNOTIFYSTRUCT)Marshal.PtrToStructure(ppidl, shNotify.GetType());

                Debug.Write("CShellItemUpdater.HandleNotification - Msg: " + msgID.ToString());

                if (shNotify.dwItem1 == IntPtr.Zero)
                {
                    Debug.WriteLine(", dwItem1 is Zero (Returning)");
                    return;
                }

                if (IsExcludedSystemFolder(shNotify.dwItem1))
                {
                    Debug.WriteLine(", dwItem1 is in Recycle Bin (Ignoring)");
                    return;
                }

                userPidl1 = TPidl.Clone(shNotify.dwItem1);
                userPidl2 = shNotify.dwItem2 != IntPtr.Zero ? TPidl.Clone(shNotify.dwItem2) : IntPtr.Zero;

                var pidlName = TPidl.GetDisplayNameFull(userPidl1);
                Debug.WriteLine(", dwItem1: " + pidlName);

                //Debug.WriteLine($"[SHCN] event=0x{(int)msgID:X} ({msgID})  " +
                //$"pidl1={TPidl.GetDisplayNameFull(userPidl1)}  " +
                //$"pidl2={(userPidl2 != IntPtr.Zero ? TPidl.GetDisplayNameFull(userPidl2) : "<null>")}");

                lock (_hierarchyManager.Lock)
                {
                    switch (msgID)
                    {
                        case SHCNE.CREATE:
                            HandleCreate(userPidl1, pidlName);
                            break;
                        case SHCNE.DELETE:
                            HandleDelete(userPidl1, pidlName);
                            break;
                        case SHCNE.RENAMEITEM:
                            HandleRenameItem(userPidl1, userPidl2);
                            break;
                        case SHCNE.UPDATEDIR:
                            HandleUpdateDir(userPidl1);
                            break;
                        case SHCNE.UPDATEITEM:
                            HandleUpdateItem(userPidl1);
                            break;
                        case SHCNE.MKDIR:
                        case SHCNE.DRIVEADD:
                            HandleMkdirOrDriveAdd(userPidl1);
                            break;
                        case SHCNE.RENAMEFOLDER:
                            HandleRenameFolder(userPidl1, userPidl2);
                            break;
                        case SHCNE.RMDIR:
                        case SHCNE.DRIVEREMOVED:
                            HandleRmdirOrDriveRemoved(userPidl1);
                            break;
                        case SHCNE.MEDIAINSERTED:
                        case SHCNE.MEDIAREMOVED:
                            HandleMediaChange(userPidl1);
                            break;
                        case SHCNE.UPDATEIMAGE:
                            HandleUpdateImage(userPidl1);
                            break;
                        default:
                            Debug.WriteLine("  [OTHER] processing... No action taken.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ERROR: Exception in CShellItemUpdateLogic.HandleNotification - " + ex.ToString());
            }
            finally
            {
                _shellApi.SHChangeNotification_Unlock(hLock);
                if (userPidl1 != IntPtr.Zero) Marshal.FreeCoTaskMem(userPidl1);
                if (userPidl2 != IntPtr.Zero) Marshal.FreeCoTaskMem(userPidl2);
            }
        }

        private void HandleCreate(IntPtr userPidl1, string? pidlName)
        {
            Debug.WriteLine("  [CREATE] processing...");
            CShellItem? parentItem = null;
            PidlSplitResult splitPidl = default;
            try
            {
                splitPidl = TPidl.Split(userPidl1);
                parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);
                if (parentItem is not null)
                {
                    Debug.WriteLine("  [CREATE] Parent found: " + parentItem.ItemPath);
                    if (parentItem.DirectoriesInitialized || parentItem.FilesInitialized)
                    {
                        CShellItem existingItem = null;
                        if (pidlName is not null)
                        {
                            if (parentItem.FilesInitialized)
                                parentItem.Files.Dictionary.TryGetValue(pidlName, out existingItem);
                            if (existingItem is null && parentItem.DirectoriesInitialized)
                                parentItem.Directories.Dictionary.TryGetValue(pidlName, out existingItem);
                        }

                        if (existingItem is not null)
                        {
                            Debug.WriteLine("  [CREATE] Item already in list: " + existingItem.ItemPath + ". Updating PIDL and refreshing info.");
                            IntPtr newPidl = TPidl.Clone(userPidl1);
                            Marshal.FreeCoTaskMem(existingItem.m_Pidl);
                            existingItem.m_Pidl = newPidl;
                            existingItem.m_FullPath = null;
                            existingItem.ReloadInfo();
                            RaiseUpdateEvent(parentItem, new ShellItemUpdateEventArgs(existingItem, CShItemUpdateType.Updated));
                        }
                        else
                        {
                            var clonedCreatePidl = TPidl.Clone(userPidl1);
                            var newItem = _shellItemFactory.Create(clonedCreatePidl, parentItem);
                            if (newItem is not null)
                            {
                                Debug.WriteLine("  [CREATE] Created newItem: " + newItem.ItemPath);
                                AddItem(parentItem, newItem);
                            }
                            else
                            {
                                Debug.WriteLine("  [CREATE] CShellItemFactory.Create returned null");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("  [CREATE] Parent not ready (neither Directories nor Files initialized). Skipping add.");
                    }
                }
                else
                {
                    Debug.WriteLine("  [CREATE] Parent NOT found in hierarchy.");
                }
            }
            finally
            {
                if (splitPidl.ParentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                if (splitPidl.ChildPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
            }
        }

        private void HandleDelete(IntPtr userPidl1, string? pidlName)
        {
            Debug.WriteLine("  [DELETE] processing...");

            if (userPidl1 == IntPtr.Zero)
            {
                Debug.WriteLine("  [DELETE] message with no location specified. Skipping.");
                return;
            }

            if (_activeDeletes.ContainsKey(pidlName))
            {
                Debug.WriteLine("  [DELETE] Already processing delete for this item. Skipping to avoid duplicate work.");
                return;
            }

            PidlSplitResult splitPidl = default;
            try
            {
                _activeDeletes.Add(pidlName, true);
                splitPidl = TPidl.Split(userPidl1);
                var parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);

                if (parentItem is null)
                { 
                    Debug.WriteLine("  [DELETE] Parent NOT found."); 
                    return;
                }

                Debug.WriteLine("  [DELETE] Parent found: " + parentItem.ItemPath);

                string? name = TPidl.GetDisplayNameFull(userPidl1);

                if (name != null)
                {
                    CShellItem childItem = null;

                    if (parentItem.FilesInitialized)
                    {
                        parentItem.Files.Dictionary.TryGetValue(name, out childItem);
                    }

                    if (childItem == null && parentItem.Directories != null)
                        parentItem.Directories.Dictionary.TryGetValue(name, out childItem);

                    if (childItem != null)
                    {
                        Debug.WriteLine("  [DELETE] Child item found: " + childItem.ItemPath + ". Updating as deleted.");
                        childItem.Ghostify();
                        DoUpdateDeleted(childItem);
                    }
                    else
                    {
                        Debug.WriteLine("  [DELETE] Child item NOT found in parent's lists.");
                    }
                }
            }
            finally
            {
                if (splitPidl.ParentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                if (splitPidl.ChildPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                _activeDeletes.Remove(pidlName);
            }
        }

        private void HandleRenameItem(IntPtr userPidl1, IntPtr userPidl2)
        {
            Debug.WriteLine("  [RENAMEITEM] processing...");
            if (userPidl2 != IntPtr.Zero)
            {
                //if the parent folder is not already part of the hierarchy, we don't need to do anything
                //because the rename is happening in a folder that we are not monitoring.
                var splitPidl = CPidl.Split(userPidl1);
                var parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);
                if (parentItem is null)
                    return;

                var item = _hierarchyManager.Find(userPidl1); //find old item
                if (item is not null)
                {
                    Debug.WriteLine("  [RENAMEITEM] Item found: " + item.ItemPath + ". New PIDL: " + TPidl.GetDisplayNameFull(userPidl2));
                    HandleRenamed(item, userPidl2);
                }
                else
                {
                    Debug.WriteLine("  [RENAMEITEM] Item NOT found.");
                }
            }
            else
            {
                Debug.WriteLine("  [RENAMEITEM] dwItem2 is Zero.");
            }
        }

        private void HandleUpdateDir(IntPtr userPidl1)
        {
            Debug.WriteLine("  [UPDATEDIR] processing...");
            if (userPidl1 == IntPtr.Zero || TPidl.SegmentCount(userPidl1) == 0)
            {
                Debug.WriteLine("  [UPDATEDIR] message with no location specified.");
                return;
            }

            var upCSI = _hierarchyManager.Find(userPidl1);
            if (upCSI is not null)
            {
                Debug.WriteLine("  [UPDATEDIR] Found item: '" + upCSI.ItemPath + "'.  Updating dir.");
                DoUpdateDir(upCSI);
            }
            else
            {
                Debug.WriteLine("  [UPDATEDIR] could not find item for '" + TPidl.GetDisplayNameFull(userPidl1) + "' in the shell item hierarchy.");
            }
        }

        private void HandleUpdateItem(IntPtr userPidl1)
        {
            Debug.WriteLine("  [UPDATEITEM] processing... " + DateTime.Now.ToString("HH:mm:ss.fff"));
            if (userPidl1 == IntPtr.Zero || TPidl.SegmentCount(userPidl1) == 0)
            {
                Debug.WriteLine("  [UPDATEITEM] Empty pidl received from UPDATEITEM event");
                return;
            }

            var item = _hierarchyManager.Find(userPidl1);
            if (item is null)
            {
                Debug.WriteLine("  [UPDATEITEM] item was not found " + DateTime.Now.ToString("HH:mm:ss.fff"));
                return;
            }

            Debug.WriteLine("  [UPDATEITEM] Found item: " + item.ItemPath + (item.IsFolder ? " (Folder)" : " (File)"));
            if (item.IsFolder)
            {
                DoUpdateDir(item);
            }
            else
            {
                DoUpdateUpdated(item);
            }
        }

        private void HandleMkdirOrDriveAdd(IntPtr userPidl1)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]  [MKDIR/DRIVEADD] processing... ");
            PidlSplitResult splitPidl = default;
            try
            {
                splitPidl = TPidl.Split(userPidl1);
                var parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);
                if (parentItem is null)
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]  [MKDIR] Parent Not Found");
                    return;
                }

                Debug.WriteLine("  [MKDIR] Parent found: " + parentItem.ItemPath);
                if (!parentItem.DirectoriesInitialized)
                {
                    //Debug.WriteLine("  [MKDIR] Parent folders NOT initialized.");
                    _ = parentItem.Directories;
                    return;
                }

                if (parentItem.Directories.Contains(userPidl1))
                {
                    Debug.WriteLine("  [MKDIR] Folder already in DirectoryList");
                    return;
                }

                Debug.WriteLine("  [MKDIR] Parent folders initialized and new item NOT in list.  Adding...");
                var clonedMkdirPidl = TPidl.Clone(userPidl1);
                var newItem = _shellItemFactory.Create(clonedMkdirPidl, parentItem);
                if (newItem is null)
                {
                    Debug.WriteLine("  [MKDIR] CShellItemFactory.Create returned null");
                    return;
                }
                Debug.WriteLine("  [MKDIR] Created newItem: " + newItem.ItemPath);
                AddItem(parentItem, newItem);
            }
            finally
            {
                if (splitPidl.ParentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                if (splitPidl.ChildPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
            }
        }

        private void HandleRenameFolder(IntPtr userPidl1, IntPtr userPidl2)
        {
            Debug.WriteLine("  [RENAMEFOLDER] processing...");
            if (userPidl2 != IntPtr.Zero)
            {
                var item = _hierarchyManager.Find(userPidl1);
                if (item is not null)
                {
                    Debug.WriteLine("  [RENAMEFOLDER] Found item: " + item.ItemPath + ". New PIDL: " + userPidl2.ToString("X"));
                    HandleRenamed(item, userPidl2);
                }
                else
                {
                    Debug.WriteLine("  [RENAMEFOLDER] Item NOT found.");
                }
            }
            else
            {
                Debug.WriteLine("  [RENAMEFOLDER] dwItem2 is Zero.");
            }
        }

        private void HandleRmdirOrDriveRemoved(IntPtr userPidl1)
        {
            Debug.WriteLine("  [RMDIR/DRIVEREMOVED] processing...");
            var parent = TPidl.TrimLast(userPidl1);
            try
            {
                var parentItem = _hierarchyManager.Find(parent);
                if (parentItem is not null)
                {
                    Debug.WriteLine("  [RMDIR] Parent found: " + parentItem.ItemPath);
                    if (parentItem.Directories is not null)
                    {
                        CShellItem? itemToRemove = parentItem.Directories[userPidl1];
                        if (itemToRemove != null)
                        {
                            Debug.WriteLine("  [RMDIR] Found item in DirectoryList. Removing: " + itemToRemove.ItemPath);
                            RemoveItem(parentItem, itemToRemove);
                        }
                        else
                        {
                            Debug.WriteLine("  [RMDIR] Item NOT found in DirectoryList.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("  [RMDIR] DirectoryList is null.");
                        if (!IsVistaOrAbove())
                        {
                            Debug.WriteLine("  [RMDIR] XP path: Updating parent.");
                            DoUpdateUpdated(parentItem);
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("  [RMDIR] Parent NOT found.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(parent);
            }
        }

        private void HandleMediaChange(IntPtr userPidl1)
        {
            Debug.WriteLine("  [MEDIA CHANGE] processing...");
            var mediaCSI = _hierarchyManager.Find(userPidl1);
            if (mediaCSI is not null)
            {
                Debug.WriteLine("  [MEDIA CHANGE] Found item: " + mediaCSI.ItemPath + ". Updating.");
                DoUpdateMediaChange(mediaCSI);
            }
            else
            {
                Debug.WriteLine("  [MEDIA CHANGE] Item NOT found.");
            }
        }

        private void HandleUpdateImage(IntPtr userPidl1)
        {
            Debug.WriteLine("  [UPDATEIMAGE] processing...");
            var imgCSI = _hierarchyManager.Find(userPidl1);
            if (imgCSI is not null)
            {
                Debug.WriteLine("  [UPDATEIMAGE] Found item: " + imgCSI.ItemPath + ". Updating icon.");
                DoUpdateIconChange(imgCSI);
            }
            else
            {
                Debug.WriteLine("  [UPDATEIMAGE] Item NOT found.");
            }
        }

        public void DoUpdateUpdated(CShellItem csi)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateUpdated");
            csi.ResetInfo();
            RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Updated));
        }

        public void DoUpdateDeleted(CShellItem csi)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateDeleted");
            //if it's a real deletion, we'd want to run dispose recursively but it could be a move so we can't do that.
            //maybe we should do it anyway and lete the lazy initialization of Files and Directories handle any attempts to read them again.
            var parent = csi?.Parent;
            RemoveItem(parent, csi, raiseEvent: false);
            RaiseUpdateEvent(parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Deleted));
            if (csi is not null)
                csi.Parent = null;
        }

        public void DoUpdateIconChange(CShellItem csi)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateIconChange");
            csi.ResetInfo();
            RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.IconChange));
        }

        public void DoUpdateMediaChange(CShellItem csi)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateMediaChange");
            csi.ClearItems(true, true);
            csi.ResetInfo();
            csi.m_FullPath = _shellItemFactory.GetFullPath(csi);
            RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.MediaChange));
        }

        public int DoUpdateDir(CShellItem csi, bool updateFiles = true, bool updateFolders = true)
        {
            if (csi is null) return 0;

            if (_isUpdatingDir)
            {
                Debug.WriteLine("DoUpdateDir called but an update is already in progress for this folder. Ignoring.");
                return 0;
            }
            try
            {
                _isUpdatingDir = true;
                if (TPidl.ResolvesToSamePathOrName(csi.PIDL, CShellItemFactory.RecycleBin.PIDL)) return 0; //ignore recycle bin

                var count = SelectiveFolderUpdate(csi, true, true);
                Debug.WriteLine("DoUpdateDir end - " + csi.Text + " - " + DateTime.Now.ToString("HH:mm:ss.fff"));
                return count;
            }
            finally
            {
                _isUpdatingDir = false;
            }
        }

        public bool RemoveItem(CShellItem parent, CShellItem item, bool raiseEvent = true)
        {
            bool changed = false;
            if (parent == null || item == null) return false;

            try
            {
                if (parent.IsFolder)
                {
                    lock (parent)
                    {
                        if (item.IsFolder && parent.DirectoriesInitialized)
                        {
                            parent.Directories.Remove(item);
                            changed = true;
                        }

                        if (!item.IsFolder && parent.FilesInitialized)
                        {
                            parent.Files.Remove(item);
                            changed = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in CShellItemUpdateLogic.RemoveItem -- " + ex.ToString());
            }

            if (changed && raiseEvent)
            {
                RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted));
            }

            if (changed)
                item.Parent = null;

            return changed;
        }

        public void RaiseUpdateEvent(object sender, ShellItemUpdateEventArgs e)
        {
            UpdateEvent?.Invoke(sender, e);
        }

        public bool HandleRenamed(CShellItem oldCsi, IntPtr newPidl)
        {
            var splitPidl = TPidl.Split(newPidl);
            
            try
            {
                if (!CPidl.ResolvesToSamePathOrName(splitPidl.ParentPidl, oldCsi.Parent.PIDL))
                {
                    return HandleMoved(oldCsi, newPidl);
                }

                // Capture old path before mutation
                string? oldPath = oldCsi.FullPath;

                if (!CShellItemFactory.Exists(newPidl))
                {
                    Debug.WriteLine("CShellItemUpdateLogic.DoRename: The new pidl could not be proven to exist on this computer.");
                    return false;
                }

                // Derive new path from the changed PIDL
                string? newPath = TPidl.GetFileSystemPath(newPidl);
                if (newPath is null)
                {
                    // Virtual item — fall back to parsing name
                    IntPtr pName = IntPtr.Zero;
                    try
                    {
                        if (SHGetNameFromIDList(newPidl, SIGDN.DESKTOPABSOLUTEPARSING, out pName) == S_OK && pName != IntPtr.Zero)
                            newPath = Marshal.PtrToStringUni(pName);
                        else
                        {
                            Debug.WriteLine("CShellItemUpdateLogic.DoMove: The new pidl could not be proven to exist on this computer.");
                            return false;
                        }
                    }
                    finally
                    {
                        if (pName != IntPtr.Zero) Marshal.FreeCoTaskMem(pName);
                    }
                }

                var renamedCsi = _hierarchyManager.UpdateRenamedItem(oldCsi, newPidl);
                if (renamedCsi is null)
                    return false;

                RaiseUpdateEvent(renamedCsi.Parent, new ShellItemUpdateEventArgs(renamedCsi, CShItemUpdateType.Renamed)
                {
                    OldPath = oldPath,
                    NewPath = newPath
                });
                return true;
            }
            finally
            {
                Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
            }
        }

        public bool HandleMoved(CShellItem oldCsi, IntPtr newPidl)
        {
            var splitPidl = TPidl.Split(newPidl);

            try
            {
                if (!CShellItemFactory.Exists(newPidl))
                {
                    Debug.WriteLine("CShellItemUpdateLogic.DoMove: The new pidl could not be proven to exist on this computer.");
                    return false;
                }

                // Derive new path from the changed PIDL
                string? newPath = TPidl.GetFileSystemPath(newPidl);
                if (newPath is null)
                {
                    // Virtual item — fall back to parsing name
                    IntPtr pName = IntPtr.Zero;
                    try
                    {
                        if (SHGetNameFromIDList(newPidl, SIGDN.DESKTOPABSOLUTEPARSING, out pName) == S_OK && pName != IntPtr.Zero)
                            newPath = Marshal.PtrToStringUni(pName);
                        else
                        {
                            Debug.WriteLine("CShellItemUpdateLogic.DoMove: The new pidl could not be proven to exist on this computer.");
                            return false;
                        }
                    }
                    finally
                    {
                        if (pName != IntPtr.Zero) Marshal.FreeCoTaskMem(pName);
                    }
                }

                // Capture old path before mutation
                string? oldPath = oldCsi.FullPath;
                var oldParentCsi = oldCsi.Parent;

                var newParentCsi = _hierarchyManager.Find(splitPidl.ParentPidl);
                if (newParentCsi is null) //moved to somewhere not in the hierarchy
                {
                    RemoveItem(oldCsi.Parent, oldCsi);
                    oldCsi.Parent = null;
                    oldCsi.m_Pidl = TPidl.Copy(newPidl);
                    RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(oldCsi, CShItemUpdateType.Moved)
                    {
                        OldPath = oldPath,
                        NewPath = newPath
                    });
                    return false;
                }
                else
                {
                    /* There is a problem with instance state validity during a move operation.
                        * The item that is moved is owned at multiple locations - in the hierarchy as well as possibly in multiple controls.
                        * So if an item (CSI) is moved, it's path is changed different.  The controls which are tied to
                        * specific paths will be confused because path in the CSI will have been changed from under their feet.
                        * For example, if the control tries to search for that moved item in their caches indexed by path, it will fail 
                        * because the new path is wrong.
                        * 
                        * To get around this problem, I have decided to strip down the old CSI and create a new
                        * CSI to represent the new state of the moved item.  The new csi will shallow copy all the imporant 
                        * values of the original CSI but the pidl and path will be updated.  The original CSI will be stripped
                        * down by calling "Ghostify" on it.  Ghostify will remove all the children but keep path and pidl the same.
                        * The new csi will be sent to event handlers for the destination folder and the old csi will be sent to 
                        * event handlers for the original folder.
                    */
                    RemoveItem(oldParentCsi, oldCsi); //update hierarchy

                    var newCsi = oldCsi.ShallowCopy();
                    oldCsi.Ghostify();
                    newCsi.Parent = newParentCsi;
                    newCsi.m_Pidl = TPidl.Clone(newPidl);
                    newCsi.ReloadInfo();

                    AddItem(newParentCsi, newCsi); //update hierarchy

                    if (newCsi.IsFolder) //recursively update child paths
                    {
                        if (newCsi.FilesInitialized)
                        {
                            foreach (CShellItem item in oldCsi.Files)
                                item.UpdateFolderPidlAndPath();
                        }
                        if (newCsi.DirectoriesInitialized)
                        {
                            foreach (CShellItem item in oldCsi.Directories)
                                item.UpdateFolderPidlAndPath();
                        }
                    }

                    //we raise two update events.  one for the old folder so controls can remove the old item.
                    //The second for the new folder so controls can add the new item.
                    RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(oldCsi, CShItemUpdateType.Moved)
                    {
                        OldPath = oldPath,
                        NewPath = newPath
                    });
                    RaiseUpdateEvent(newParentCsi, new ShellItemUpdateEventArgs(newCsi, CShItemUpdateType.Moved)
                    {
                        OldPath = oldPath,
                        NewPath = newPath
                    });

                    return false;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
            }
        }

        private int SelectiveFolderUpdate(CShellItem? csiFolder, bool updateFiles = true, bool updateFolders = true)
        {
            if (csiFolder is null) return 0;
            if (!csiFolder.m_IsFolder) return 0;

            Debug.WriteLine("SelectiveFolderUpdate begin - " + csiFolder.Text + " - " + DateTime.Now.ToString("HH:mm:ss.fff"));

            var attrFlag = SHCONTF.INCLUDEHIDDEN;
            if (csiFolder.FilesInitialized && updateFiles)
                attrFlag = attrFlag | SHCONTF.NONFOLDERS;
            if (csiFolder.DirectoriesInitialized && updateFolders)
                attrFlag = attrFlag | SHCONTF.FOLDERS;
            if (attrFlag == SHCONTF.INCLUDEHIDDEN)
                return 0;

            var newRelPidls = _shellItemFactory.GetPidlsOfFolder(csiFolder, attrFlag);

            List<(CShellItem, CShItemUpdateType)> operations;
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(csiFolder, TimeSpan.FromMilliseconds(1), ref lockTaken);
                if (!lockTaken)
                    return 0;

                operations = CrossCheckOldAndNewFolderContents(csiFolder, updateFiles, updateFolders, newRelPidls);
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(csiFolder);
            }

            if (operations.Count > 0)
            {
                foreach (var (item, type) in operations)
                {
                    RaiseUpdateEvent(csiFolder, new ShellItemUpdateEventArgs(item, type));
                }
            }

            return operations.Count;
        }

        private List<(CShellItem, CShItemUpdateType)> CrossCheckOldAndNewFolderContents(CShellItem csiFolder, bool UpdateFiles, bool UpdateFolders, List<IntPtr> newRelPidls)
        {
            Debug.WriteLine("CrossCheckOldAndNewFolderContents begin");
            var operations = new List<(CShellItem Item, CShItemUpdateType Type)>();

            if (!csiFolder.IsFileSystem)
                return operations;

            lock (_hierarchyManager.Lock)
            {
                if (newRelPidls.Count < 1)
                {
                    var invalidItems = new List<CShellItem>();

                    if (csiFolder.FilesInitialized && UpdateFiles)
                        invalidItems.AddRange(csiFolder.Files.ToArray());
                    if (csiFolder.DirectoriesInitialized && UpdateFolders)
                        invalidItems.AddRange(csiFolder.Directories.ToArray());

                    if (invalidItems.Count > 0)
                    {
                        foreach (var item in invalidItems)
                        {
                            RemoveItem(csiFolder, item);
                            operations.Add((item, CShItemUpdateType.Deleted));
                        }
                    }
                }
                else
                {
                    var oldCsiDic = new Dictionary<string, CShellItem>();
                    if (csiFolder.DirectoriesInitialized && UpdateFolders)
                    {
                        foreach (var item in csiFolder.Directories.Items)
                            oldCsiDic.TryAdd(TPidl.GetDisplayNameFull(item.LastPIDL) ?? string.Empty, item);
                    }
                    if (csiFolder.FilesInitialized && UpdateFiles)
                    {
                        foreach (var item in csiFolder.Files.Items)
                        {
                            oldCsiDic.TryAdd(TPidl.GetDisplayNameFull(item.LastPIDL) ?? string.Empty, item);
                        }
                    }

                    Dictionary<string, IFileSystemEntry> fileInfos = null;
                    if (csiFolder.IsFileSystem)
                    {
                        fileInfos = _fileSystem.GetFileSystemInfos(csiFolder.FullPath).ToDictionary(file => file.Name, file => file);
                    }

                    for (int i = 0; i < newRelPidls.Count; i++)
                    {
                        IntPtr newRelPidl = newRelPidls[i];
                        if (newRelPidl == IntPtr.Zero) continue;

                        string newFileName = TPidl.GetDisplayNameFull(newRelPidl) ?? string.Empty;
                        
                        if (oldCsiDic.TryGetValue(newFileName, out CShellItem? oldCsi))
                        {
                            if (oldCsi != null && TPidl.ResolvesToSamePathOrName(oldCsi.LastPIDL, newRelPidl))
                            {
                                if (!ReferenceEquals(oldCsi, CShellItemFactory.RecycleBin))
                                {
                                    bool doupdate = false;

                                    if (oldCsi.ImageIndex == -1) //new item without image yet
                                    {
                                        oldCsi.NeedsRefresh = true;
                                        doupdate = true; 
                                    }
                                    else if (csiFolder.IsFileSystem && fileInfos != null)
                                    {
                                        if (fileInfos.TryGetValue(newFileName, out IFileSystemEntry fi))
                                        {
                                            // Compare the on-disk LastWriteTime time to the CHILD's cached LastWriteTime, not the
                                            // folder's. Comparing against csiFolder.LastWriteTime (the folder) causes
                                            // nearly every child to be flagged as "updated" whenever the folder's
                                            // own LastWriteTime is stale, which then calls oldCsi.ResetInfo() -> resets
                                            // ImageIndex to -1 on every item, blanking icons/thumbnails on the
                                            // next repaint.
                                            if (fi.LastWriteTime > oldCsi.LastWriteTime)
                                                doupdate = true;
                                        }
                                        else doupdate = true;
                                    }
                                    else doupdate = true;

                                    if (doupdate)
                                    {
                                        oldCsi.ResetInfo();
                                        if (oldCsi.IsFolder) oldCsi.ResetChildren();
                                        operations.Add((oldCsi, CShItemUpdateType.Updated));
                                    }
                                }

                                Marshal.FreeCoTaskMem(newRelPidl);
                                newRelPidls[i] = IntPtr.Zero;
                                oldCsiDic.Remove(newFileName);
                                continue;
                            }
                        }
                        
                        var newItem = _shellItemFactory.Create(newRelPidl, csiFolder);
                        Marshal.FreeCoTaskMem(newRelPidl);
                        if (newItem is null)
                        {
                            newRelPidls[i] = IntPtr.Zero;
                            continue;
                        }
                        var result = _hierarchyManager.Add(newItem);
                        if (result != null)
                        {
                            operations.Add((newItem, CShItemUpdateType.Created));
                        }
                    }

                    if (oldCsiDic.Count > 0)
                    {
                        foreach (var item in oldCsiDic.Values)
                        {
                            RemoveItem(csiFolder, item);
                            operations.Add((item, CShItemUpdateType.Deleted));
                        }
                    }
                }
            }

            return operations;
        }

        internal void AddItem(CShellItem parent, CShellItem item)
        {
            bool changed = false;
            try
            {
                item.Parent = parent;
                if (parent.IsFolder)
                {
                    lock (parent)
                    {
                        if (item.IsFolder) 
                        {
                            if (!parent.Directories.Contains(item.PIDL))
                            {
                                parent.Directories.Add(item);
                                changed = true;
                            }
                        }
                        else { 
                            if (!parent.Files.Contains(item.PIDL))
                            {
                                parent.Files.Add(item);
                                changed = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in CShellItemUpdateLogic.AddItem -- " + ex.ToString());
            }

            if (changed)
            {
                RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Created));
            }
        }

        private bool IsItemNotificationEvent(SHCNE lEvent)
        {
            return !((lEvent & (SHCNE.ASSOCCHANGED | SHCNE.EXTENDED_EVENT | SHCNE.FREESPACE | SHCNE.DRIVEADDGUI | SHCNE.SERVERDISCONNECT)) > 0);
        }

        private static bool IsExcludedSystemFolder(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) return false;

            var name = TPidl.GetDisplayNameFull(pidl);
            if (name.ToUpper().Contains("$RECYCLE.BIN")) return true;
            if (name.Contains("System Volume Information")) return true;
            if (name.Contains(CShellItemFactory.WindowsDir)) return true;
            if (name.Contains(CShellItemFactory.TempFolder)) return true;

            var recycleBinPidl = CShellItemFactory.RecycleBin.PIDL;
            if (recycleBinPidl == IntPtr.Zero) throw new Exception("The Recycle Bin PIDL has not been set up.");

            if (name.Contains(CShellItemFactory.StrRecycleBin))
                return true;
            
            return false;
        }
    }
}
