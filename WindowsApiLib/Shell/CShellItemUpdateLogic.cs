using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsApiLib.Util;
using WindowsApiLib;
using static WindowsApiLib.Shell.ShellAPI;

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

        public event CShellItemUpdater.CShItemUpdateEventHandler UpdateEvent;

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

                if (IsInRecycleBin(shNotify.dwItem1))
                {
                    Debug.WriteLine(", dwItem1 is in Recycle Bin (Ignoring)");
                    return;
                }

                userPidl1 = TPidl.Clone(shNotify.dwItem1);
                userPidl2 = shNotify.dwItem2 != IntPtr.Zero ? TPidl.Clone(shNotify.dwItem2) : IntPtr.Zero;

                var pidlName = TPidl.ToString(userPidl1);
                Debug.WriteLine(", dwItem1: " + pidlName);

                lock (_hierarchyManager.Lock)
                {
                    CShellItem? parentItem = null;
                    PidlSplitResult splitPidl = default;

                    switch (msgID)
                    {
                        case SHCNE.CREATE:
                            {
                                Debug.WriteLine("  [CREATE] processing...");
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
                                
                                break;
                            }
                        case SHCNE.DELETE:
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

                            try
                            {
                                _activeDeletes.Add(pidlName, true);
#if DEBUG
                                string? name = TPidl.GetDisplayName(userPidl1);
#endif
                                splitPidl = TPidl.Split(userPidl1);
                                parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);

                                if (parentItem != null)
                                {
                                    Debug.WriteLine("  [DELETE] Parent found: " + parentItem.ItemPath);
                                    
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
                                            childItem.Ghost();
                                            DoUpdateDeleted(childItem);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [DELETE] Child item NOT found in parent's lists.");
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [DELETE] Parent NOT found.");
                                }
                            }
                            finally
                            {
                                if (splitPidl.ParentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                                if (splitPidl.ChildPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                                _activeDeletes.Remove(pidlName);
                            }

                            break;
                        case SHCNE.RENAMEITEM:
                            Debug.WriteLine("  [RENAMEITEM] processing...");
                            if (userPidl2 != IntPtr.Zero)
                            {
                                var item = _hierarchyManager.Find(userPidl1);
                                if (item is not null)
                                {
                                    Debug.WriteLine("  [RENAMEITEM] Item found: " + item.ItemPath + ". New PIDL: " + userPidl2.ToString("X"));
                                    DoUpdateRenamed(item, userPidl2);
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
                            break;
                        case SHCNE.UPDATEDIR:
                            {
                                Debug.WriteLine("  [UPDATEDIR] processing...");
                                if (userPidl1 == IntPtr.Zero || TPidl.SegmentCount(userPidl1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEDIR] message with no location specified.");
                                    return;
                                }
                                else
                                {
                                    var upCSI = _hierarchyManager.Find(userPidl1);
                                    if (upCSI is not null)
                                    {
                                        Debug.WriteLine("  [UPDATEDIR] Found item: " + upCSI.ItemPath + ".  Updating dir.");
                                        DoUpdateDir(upCSI);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [UPDATEDIR] Item NOT found.");
                                    }
                                }

                                break;
                            }

                        case SHCNE.UPDATEITEM:
                            {
                                Debug.WriteLine("  [UPDATEITEM] processing... " + DateTime.Now.ToString("HH:mm:ss.fff"));
                                if (userPidl1 == IntPtr.Zero || TPidl.SegmentCount(userPidl1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEITEM] Empty pidl received from UPDATEITEM event");
                                }
                                else
                                {
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
                                break;
                            }

                        case SHCNE.MKDIR:
                        case SHCNE.DRIVEADD:
                            Debug.WriteLine("  [MKDIR/DRIVEADD] processing... " + DateTime.Now.ToString("HH:mm:ss.fff"));
                            try
                            {
                                splitPidl = TPidl.Split(userPidl1);
                                parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [MKDIR] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.DirectoriesInitialized)
                                    {
                                        if (!parentItem.Directories.Contains(userPidl1))
                                        {
                                            Debug.WriteLine("  [MKDIR] Parent folders initialized and NOT in list. Adding.");
                                            var clonedMkdirPidl = TPidl.Clone(userPidl1);
                                            var newItem = _shellItemFactory.Create(clonedMkdirPidl, parentItem);
                                            if (newItem is not null)
                                            {
                                                Debug.WriteLine("  [MKDIR] Created newItem: " + newItem.ItemPath);
                                                AddItem(parentItem, newItem);
                                            }
                                            else
                                            {
                                                Debug.WriteLine("  [MKDIR] CShellItemFactory.Create returned null");
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [MKDIR] Folder already in DirectoryList");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [MKDIR] Parent folders NOT initialized.");
                                        if (!IsVistaOrAbove())
                                        {
                                            Debug.WriteLine("  [MKDIR] XP path: Updating parent.");
                                            DoUpdateUpdated(parentItem);
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [MKDIR] Parent Not Found " + DateTime.Now.ToString("HH:mm:ss.fff"));
                                }
                            }
                            finally
                            {
                                if (splitPidl.ParentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                                if (splitPidl.ChildPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                            }

                            break;
                        case SHCNE.RENAMEFOLDER:
                            Debug.WriteLine("  [RENAMEFOLDER] processing...");
                            if (userPidl2 != IntPtr.Zero)
                            {
                                var item = _hierarchyManager.Find(userPidl1);
                                if (item is not null)
                                {
                                    Debug.WriteLine("  [RENAMEFOLDER] Found item: " + item.ItemPath + ". New PIDL: " + userPidl2.ToString("X"));
                                    DoUpdateRenamed(item, userPidl2);
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

                            break;
                        case SHCNE.RMDIR:
                        case SHCNE.DRIVEREMOVED:
                            Debug.WriteLine("  [RMDIR/DRIVEREMOVED] processing...");
                            var parent = TPidl.TrimLast(userPidl1);

                            try
                            {
                                parentItem = _hierarchyManager.Find(parent);
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

                            break;
                        case SHCNE.MEDIAINSERTED:
                        case SHCNE.MEDIAREMOVED:
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

                            break;
                        case SHCNE.UPDATEIMAGE:
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
            RemoveItem(csi?.Parent, csi);
            RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Deleted));
        }

        public bool DoUpdateRenamed(CShellItem csi, IntPtr changedPidl)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateRenamed");
            return DoRenameOrMove(csi, changedPidl, CShItemUpdateType.Renamed);
        }

        public bool DoUpdateMoved(CShellItem csi, IntPtr changedPidl)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.DoUpdateMoved");
            return DoRenameOrMove(csi, changedPidl, CShItemUpdateType.Moved);
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
                if (ReferenceEquals(csi, CShellItemFactory.RecycleBin)) return 0;

                var count = SelectiveFolderUpdate(csi, true, true);
                Debug.WriteLine("DoUpdateDir end - " + csi.Text + " - " + DateTime.Now.ToString("HH:mm:ss.fff"));
                return count;
            }
            finally
            {
                _isUpdatingDir = false;
            }
        }

        public bool RemoveItem(CShellItem parent, CShellItem item)
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

            if (changed)
            {
                RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted));
            }

            return changed;
        }

        public void RaiseUpdateEvent(object sender, ShellItemUpdateEventArgs e)
        {
            UpdateEvent?.Invoke(sender, e);
        }

        private bool DoRenameOrMove(CShellItem csi, IntPtr changedPidl, CShItemUpdateType changeType)
        {
            var splitPidl = TPidl.Split(changedPidl);
            var allegedParentCsi = _hierarchyManager.Find(splitPidl.ParentPidl);

            try
            {
                if (!CShellItemFactory.Exists(changedPidl))
                {
                    Debug.WriteLine("CShellItemUpdateLogic.DoRenameOrMove: The given pidl could not be proven to exist on this computer.");
                    return false;
                }

                if (allegedParentCsi is null) //moved to somewhere not in the hierarchy
                {
                    var oldParentCsi = csi.Parent;
                    RemoveItem(csi.Parent, csi);
                    csi.Parent = null;
                    csi.m_Pidl = TPidl.Copy(changedPidl);
                    RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Moved));
                    return false;
                }
                else
                {
                    IntPtr newIShellFolderPtr = IntPtr.Zero;
                    var oldParentCsi = csi.Parent;

                    if (TPidl.ResolvesToSamePathOrName(allegedParentCsi.PIDL, csi.Parent.PIDL)) //rename
                    {
                        csi.m_Pidl = TPidl.Clone(changedPidl);
                        csi.ReloadInfo();
                        RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Renamed));
                        return true;
                    }
                    else //move
                    {
                        RemoveItem(csi.Parent, csi);

                        AddItem(allegedParentCsi, csi);

                        csi.Parent = allegedParentCsi;

                        csi.m_Pidl = TPidl.Clone(changedPidl);
                        csi.ReloadInfo();

                        if (csi.IsFolder)
                        {
                            //var ishellFolder = allegedParentCsi.GetIShellFolder();

                            //if (ishellFolder.BindToObject(splitPidl.ChildPidl, IntPtr.Zero, IID_IShellFolder, ref newIShellFolderPtr) != S_OK)
                            //{
                            //    Marshal.Release(newIShellFolderPtr);
                            //    return false;
                            //}

                            //csi.m_IShellFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(newIShellFolderPtr, typeof(IShellFolder));
                            //Marshal.Release(newIShellFolderPtr);

                            if (csi.FilesInitialized)
                            {
                                foreach (CShellItem item in csi.Files)
                                    item.UpdateFolderPidlAndPath();
                            }
                            if (csi.DirectoriesInitialized)
                            {
                                foreach (CShellItem item in csi.Directories)
                                    item.UpdateFolderPidlAndPath();
                            }
                        }
                        RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Moved));
                        RaiseUpdateEvent(allegedParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Moved));

                        return false;
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
            }
        }

        private int SelectiveFolderUpdate(CShellItem? csi, bool updateFiles = true, bool updateFolders = true)
        {
            if (csi is null) return 0;
            if (!csi.m_IsFolder) return 0;

            Debug.WriteLine("SelectiveFolderUpdate begin - " + csi.Text + " - " + DateTime.Now.ToString("HH:mm:ss.fff"));

            var attrFlag = SHCONTF.INCLUDEHIDDEN;
            if (csi.FilesInitialized && updateFiles)
                attrFlag = attrFlag | SHCONTF.NONFOLDERS;
            if (csi.DirectoriesInitialized && updateFolders)
                attrFlag = attrFlag | SHCONTF.FOLDERS;
            if (attrFlag == SHCONTF.INCLUDEHIDDEN)
                return 0;

            var newPidls = _shellItemFactory.GetPidlsOfFolder(csi, attrFlag);

            List<(CShellItem, CShItemUpdateType)> operations;
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(csi, TimeSpan.FromMilliseconds(1), ref lockTaken);
                if (!lockTaken)
                    return 0;

                operations = CrossCheckOldAndNewFolderContents(csi, updateFiles, updateFolders, newPidls);
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(csi);
            }

            if (operations.Count > 0)
            {
                foreach (var (item, type) in operations)
                {
                    RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(item, type));
                }
            }

            return operations.Count;
        }

        private List<(CShellItem, CShItemUpdateType)> CrossCheckOldAndNewFolderContents(CShellItem csi, bool UpdateFiles, bool UpdateFolders, List<IntPtr> newPidls)
        {
            Debug.WriteLine("CrossCheckOldAndNewFolderContents begin");
            var operations = new List<(CShellItem Item, CShItemUpdateType Type)>();

            lock (_hierarchyManager.Lock)
            {
                if (newPidls.Count < 1)
                {
                    var invalidItems = new List<CShellItem>();

                    if (csi.FilesInitialized && UpdateFiles)
                        invalidItems.AddRange(csi.Files.ToArray());
                    if (csi.DirectoriesInitialized && UpdateFolders)
                        invalidItems.AddRange(csi.Directories.ToArray());

                    if (invalidItems.Count > 0)
                    {
                        foreach (var item in invalidItems)
                        {
                            RemoveItem(csi, item);
                            operations.Add((item, CShItemUpdateType.Deleted));
                        }
                    }
                }
                else
                {
                    var oldCsiDic = new Dictionary<string, CShellItem>();
                    if (csi.DirectoriesInitialized && UpdateFolders)
                    {
                        foreach (var item in csi.Directories.Items)
                            oldCsiDic.TryAdd(TPidl.ToString(item.LastPIDL, false) ?? string.Empty, item);
                    }
                    if (csi.FilesInitialized && UpdateFiles)
                    {
                        foreach (var item in csi.Files.Items)
                        {
                            oldCsiDic.TryAdd(TPidl.ToString(item.LastPIDL, false) ?? string.Empty, item);
                        }
                    }

                    Dictionary<string, IFileInfo> fileInfos = null;
                    if (csi.IsFileSystem)
                    {
                        fileInfos = _fileSystem.GetFiles(csi.FullPath).ToDictionary(file => file.Name, file => file);
                    }

                    for (int i = 0; i < newPidls.Count; i++)
                    {
                        IntPtr newPidl = newPidls[i];
                        if (newPidl == IntPtr.Zero) continue;

                        string newFileName = TPidl.ToString(newPidl, false) ?? string.Empty;
                        if (oldCsiDic.TryGetValue(newFileName, out CShellItem? oldCsi))
                        {
                            if (oldCsi != null && TPidl.ResolvesToSamePathOrName(oldCsi.LastPIDL, newPidl))
                            {
                                if (!ReferenceEquals(csi, CShellItemFactory.RecycleBin))
                                {
                                    bool doupdate = false;
                                    if (csi.IsFileSystem && fileInfos != null)
                                    {
                                        if (fileInfos.TryGetValue(newFileName, out IFileInfo fi))
                                        {
                                            if (fi.LastWriteTime > csi.LastWriteTime)
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

                                Marshal.FreeCoTaskMem(newPidl);
                                newPidls[i] = IntPtr.Zero;
                                oldCsiDic.Remove(newFileName);
                                continue;
                            }
                        }
                        
                        var newItem = _shellItemFactory.Create(newPidl, csi);
                        if (newItem is null)
                        {
                            Marshal.FreeCoTaskMem(newPidl);
                            newPidls[i] = IntPtr.Zero;
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
                            RemoveItem(csi, item);
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
                                parent.Directories.Append(item);
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

        private static unsafe bool IsInRecycleBin(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) return false;

            var name = TPidl.ToString(pidl);
            if (name.ToUpper().Contains("$RECYCLE.BIN")) return true;

            var recycleBinPidl = CShellItemFactory.RecycleBin.PIDL;
            if (recycleBinPidl == IntPtr.Zero) throw new Exception("The Recycle Bin PIDL has not been set up.");

            if (name.Contains(CShellItemFactory.StrRecycleBin))
                return true;
            
            return false;
        }
    }
}
