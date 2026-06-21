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
        private readonly LruConcurrentDictionary<IntPtr, bool> _activeDeletes = new(1000);
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

        public void HandleNotification(IntPtr wParam, IntPtr lParam)
        {
            if (!AllowUpdates) return;

            IntPtr ppidl = IntPtr.Zero;
            var msgID = default(SHCNE);
            SHNOTIFYSTRUCT shNotify = default;
            var hLock = _shellApi.SHChangeNotification_Lock(wParam, (uint)lParam, ref ppidl, ref msgID);
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

                if (shNotify.dwItem1 != IntPtr.Zero)
                {
                    Debug.WriteLine(", dwItem1: " + _shellApi.GetPidlName(shNotify.dwItem1));
                }

                lock (_hierarchyManager.Lock)
                {
                    CShellItem? parentItem = null;
                    IntPtr parentPidl = IntPtr.Zero;

                    switch (msgID)
                    {
                        case SHCNE.CREATE:
                            {
                                Debug.WriteLine("  [CREATE] processing...");
                                IntPtr realRel;
                                var splitPidl = _shellApi.SplitPidl(shNotify.dwItem1);

                                parentItem = _hierarchyManager.Find(splitPidl.ParentPidl);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [CREATE] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.FilesInitialized)
                                    {
                                        if (!parentItem.m_files.ContainsEquivalentAbsolutePidl(shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("  [CREATE] Parent files initialized and item NOT in list. Adding.");
                                            var newItem = _shellItemFactory.Create(shNotify.dwItem1, parentItem);
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
                                        else
                                        {
                                            Debug.WriteLine("  [CREATE] Item already in FileList");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [CREATE] Parent files NOT initialized. Skipping add.");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [CREATE] Parent NOT found in hierarchy.");
                                }
                                Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                                Marshal.FreeCoTaskMem(splitPidl.ChildPidl);

                                break;
                            }
                        case SHCNE.DELETE:
                            Debug.WriteLine("  [DELETE] processing...");

                            if (shNotify.dwItem1 == IntPtr.Zero)
                            {
                                Debug.WriteLine("  [DELETE] message with no location specified. Skipping.");
                                return;
                            }

                            if (_activeDeletes.ContainsKey(shNotify.dwItem1))
                            {
                                Debug.WriteLine("  [DELETE] Already processing delete for this item. Skipping to avoid duplicate work.");
                                return;
                            }

                            try
                            {
                                _activeDeletes.Add(shNotify.dwItem1, true);

                                var splitResult = _shellApi.SplitPidl(shNotify.dwItem1);
                                parentPidl = splitResult.ParentPidl;
                                var relPidl = splitResult.ChildPidl;
                                Debug.WriteLine($"  {_shellApi.GetPidlName(shNotify.dwItem1)}");
                                Debug.WriteLine($"  {_shellApi.GetPidlName(parentPidl)}");
                                parentItem = _hierarchyManager.Find(parentPidl);

                                if (parentItem != null)
                                {
                                    Debug.WriteLine("  [DELETE] Parent found: " + parentItem.ItemPath);
                                    CShellItem childItem = null;

                                    if (parentItem.m_files != null)
                                        childItem = parentItem.m_files[relPidl];

                                    if (childItem == null && parentItem.DirectoriesCollection != null)
                                        childItem = parentItem.DirectoriesCollection[relPidl];

                                    if (childItem != null)
                                    {
                                        Debug.WriteLine("  [DELETE] Child item found: " + childItem.ItemPath + ". Updating as deleted.");
                                        DoUpdate(childItem, IntPtr.Zero, CShItemUpdateType.Deleted);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [DELETE] Child item NOT found in parent's lists.");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [DELETE] Parent NOT found.");
                                }

                                Marshal.FreeCoTaskMem(parentPidl);
                                Marshal.FreeCoTaskMem(relPidl);
                            }
                            finally
                            {
                                _activeDeletes.Remove(shNotify.dwItem1);
                            }

                            break;
                        case SHCNE.RENAMEITEM:
                            Debug.WriteLine("  [RENAMEITEM] processing...");
                            if (shNotify.dwItem2 != IntPtr.Zero)
                            {
                                var item = _hierarchyManager.Find(shNotify.dwItem1);
                                if (item is not null)
                                {
                                    Debug.WriteLine("  [RENAMEITEM] Item found: " + item.ItemPath + ". New PIDL: " + shNotify.dwItem2.ToString("X"));
                                    DoUpdate(item, shNotify.dwItem2, CShItemUpdateType.Renamed);
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
                                if (shNotify.dwItem1 == IntPtr.Zero || _shellApi.GetPidlSegmentCount(shNotify.dwItem1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEDIR] message with no location specified.");
                                    return;
                                }
                                else
                                {
                                    var upCSI = _hierarchyManager.Find(shNotify.dwItem1);
                                    if (upCSI is not null)
                                    {
                                        Debug.WriteLine("  [UPDATEDIR] Found item: " + upCSI.ItemPath + ".  Updating dir.");
                                        DoUpdate(upCSI, default, CShItemUpdateType.UpdateDir);
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
                                if (shNotify.dwItem1 == IntPtr.Zero || _shellApi.GetPidlSegmentCount(shNotify.dwItem1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEITEM] Empty pidl received from UPDATEITEM event");
                                }
                                else
                                {
                                    var item = _hierarchyManager.Find(shNotify.dwItem1);
                                    if (item is null)
                                    {
                                        Debug.WriteLine("  [UPDATEITEM] item was not found " + DateTime.Now.ToString("HH:mm:ss.fff"));
                                        return;
                                    }

                                    Debug.WriteLine("  [UPDATEITEM] Found item: " + item.ItemPath + (item.IsFolder ? " (Folder)" : " (File)"));
                                    if (item.IsFolder)
                                    {
                                        DoUpdate(item, default, CShItemUpdateType.UpdateDir);
                                    }
                                    else
                                    {
                                        DoUpdate(item, IntPtr.Zero, CShItemUpdateType.Updated);
                                    }
                                }
                                break;
                            }

                        case SHCNE.MKDIR:
                        case SHCNE.DRIVEADD:
                            {
                                Debug.WriteLine("  [MKDIR/DRIVEADD] processing... " + DateTime.Now.ToString("HH:mm:ss.fff"));
                                var splitPidls = _shellApi.SplitPidl(shNotify.dwItem1);
                                parentItem = _hierarchyManager.Find(splitPidls.ParentPidl);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [MKDIR] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.FoldersInitialized)
                                    {
                                        if (!parentItem.DirectoriesCollection.Contains(shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("  [MKDIR] Parent folders initialized and NOT in list. Adding.");
                                            var newItem = _shellItemFactory.Create(shNotify.dwItem1, parentItem);
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
                                            DoUpdate(parentItem, IntPtr.Zero, CShItemUpdateType.Updated);
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [MKDIR] Parent Not Found " + DateTime.Now.ToString("HH:mm:ss.fff"));
                                }
                                Marshal.FreeCoTaskMem(splitPidls.ParentPidl);
                                Marshal.FreeCoTaskMem(splitPidls.ChildPidl);
                                break;
                            }
                        case SHCNE.RENAMEFOLDER:
                            Debug.WriteLine("  [RENAMEFOLDER] processing...");
                            if (shNotify.dwItem2 != IntPtr.Zero)
                            {
                                var item = _hierarchyManager.Find(shNotify.dwItem1);
                                if (item is not null)
                                {
                                    Debug.WriteLine("  [RENAMEFOLDER] Found item: " + item.ItemPath + ". New PIDL: " + shNotify.dwItem2.ToString("X"));
                                    DoUpdate(item, shNotify.dwItem2, CShItemUpdateType.Renamed);
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
                            {
                                Debug.WriteLine("  [RMDIR/DRIVEREMOVED] processing...");
                                var parent = _shellApi.TrimLastPidl(shNotify.dwItem1);

                                parentItem = _hierarchyManager.Find(parent);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [RMDIR] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.DirectoriesCollection is not null)
                                    {
                                        CShellItem? itemToRemove = parentItem.DirectoriesCollection[shNotify.dwItem1];
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
                                            DoUpdate(parentItem, IntPtr.Zero, CShItemUpdateType.Updated);
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  [RMDIR] Parent NOT found.");
                                }
                                Marshal.FreeCoTaskMem(parent);
                                break;
                            }
                        case SHCNE.MEDIAINSERTED:
                        case SHCNE.MEDIAREMOVED:
                            Debug.WriteLine("  [MEDIA CHANGE] processing...");
                            var mediaCSI = _hierarchyManager.Find(shNotify.dwItem1);
                            if (mediaCSI is not null)
                            {
                                Debug.WriteLine("  [MEDIA CHANGE] Found item: " + mediaCSI.ItemPath + ". Updating.");
                                DoUpdate(mediaCSI, default, CShItemUpdateType.MediaChange);
                            }
                            else
                            {
                                Debug.WriteLine("  [MEDIA CHANGE] Item NOT found.");
                            }

                            break;
                        case SHCNE.UPDATEIMAGE:
                            Debug.WriteLine("  [UPDATEIMAGE] processing...");
                            var imgCSI = _hierarchyManager.Find(shNotify.dwItem1);
                            if (imgCSI is not null)
                            {
                                Debug.WriteLine("  [UPDATEIMAGE] Found item: " + imgCSI.ItemPath + ". Updating icon.");
                                DoUpdate(imgCSI, default, CShItemUpdateType.IconChange);
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
            finally
            {
                _shellApi.SHChangeNotification_Unlock(hLock);
            }
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
                    lock (_hierarchyManager.Lock)
                    {
                        if (parent.FoldersInitialized && parent.m_directories.Contains(item))
                        {
                            parent.m_directories.Remove(item);
                            changed = true;
                        }

                        if (parent.FilesInitialized && parent.m_files.Contains(item))
                        {
                            parent.m_files.Remove(item);
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

        internal void DoUpdate(CShellItem csi, IntPtr changedPidl, CShItemUpdateType changeType)
        {
            Debug.WriteLine("Entered CShellItemUpdateLogic.Update: " + changeType.ToString());
            switch (changeType)
            {
                case CShItemUpdateType.UpdateDir:
                    {
                        DoUpdateDir(csi);
                        break;
                    }
                case CShItemUpdateType.Updated:
                    {
                        csi.ResetInfo();
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
                case CShItemUpdateType.Deleted:
                    {
                        RemoveItem(csi?.Parent, csi);
                        break;
                    }
                case CShItemUpdateType.Renamed:
                case CShItemUpdateType.Moved:
                    {
                        DoRenameOrMove(csi, changedPidl, changeType);
                        break;
                    }
                case CShItemUpdateType.IconChange:
                    {
                        csi.ResetInfo();
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
                case CShItemUpdateType.MediaChange:
                    {
                        csi.ClearItems(true, true);
                        csi.ResetInfo();
                        csi.m_Path = _shellItemFactory.GetFullPath(csi);
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
            }
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
                    RemoveItem(csi.Parent, csi);
                    csi.m_Parent = null;
                    csi.m_Pidl = changedPidl;
                    return false;
                }
                else
                {
                    IntPtr newIShellFolderPtr = IntPtr.Zero;
                    var oldParentCsi = csi.Parent;

                    //Marshal.FreeCoTaskMem(csi.m_Pidl);
                    //csi.m_Pidl = TPidl.Concatenate(splitPidl.ParentPidl, pidlRel);

                    if (CPidl.ResolvesToSamePathOrName(allegedParentCsi.PIDL, csi.Parent.PIDL)) //rename
                    {
                        csi.ReloadInfo();
                        RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Renamed));
                        return true;
                    }
                    else //move
                    {
                        RemoveItem(csi.Parent, csi);

                        AddItem(allegedParentCsi, csi);

                        csi.m_Parent = allegedParentCsi;

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

                            if (csi.m_files is not null)
                            {
                                foreach (CShellItem item in csi.m_files)
                                    item.UpdateFolderPidlAndPath();
                            }
                            if (csi.m_directories is not null)
                            {
                                foreach (CShellItem item in csi.m_directories)
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
            if (csi.m_files is not null && updateFiles)
                attrFlag = attrFlag | SHCONTF.NONFOLDERS;
            if (csi.m_directories is not null && updateFolders)
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

                    if (csi.m_files is not null && UpdateFiles)
                        invalidItems.AddRange(csi.m_files.ToArray());
                    if (csi.m_directories is not null && UpdateFolders)
                        invalidItems.AddRange(csi.m_directories.ToArray());

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
                    if (csi.m_directories is not null && UpdateFolders)
                    {
                        foreach (var item in csi.m_directories.Items)
                            oldCsiDic.TryAdd(TPidl.ToString(item.LastPIDL, false) ?? string.Empty, item);
                    }
                    if (csi.m_files is not null && UpdateFiles)
                    {
                        foreach (var item in csi.m_files.Items)
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
                item.m_Parent = parent;
                if (parent.IsFolder)
                {
                    lock (parent)
                    {
                        if (!parent.DirectoriesCollection.Contains(item.PIDL))
                        {
                            parent.m_directories.Append(item);
                            changed = true;
                        }
                        if (!parent.m_files.Contains(item.PIDL))
                        {
                            parent.m_files.Add(item);
                            changed = true;
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
