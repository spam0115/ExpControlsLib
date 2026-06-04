using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.CShellItem;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.SystemImageListManager;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// CShItemUpdater provides the infrastructure that registers for and receives WM_Notify messages for all changes to the FileSystem and
    /// Virtual Folders known to the local machine. It has knowledge of the internal CShellItem cache. If a change affects that cache, 
    /// it calls the appropriate CShellItem routines to report these changes.
    /// </summary>
    /// <remarks>Only changes of interest to the CShellItem internal cache are reported. All others are ignored.</remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only

    public class CShellItemUpdater : NativeWindow, IDisposable
    {
        private readonly CShellItemHierachyManager HierachyManager;
        private int m_notifyId;
        private uint _eventFlags = 0;
        private Thread _backgroundThread;
        private readonly AutoResetEvent _initializedEvent = new AutoResetEvent(false);

        public static event CShItemUpdateEventHandler UpdateEvent;

        public delegate void CShItemUpdateEventHandler(object sender, ShellItemUpdateEventArgs e);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AllowUpdates { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="root"></param>
        /// <param name="SHCNE_flags"></param>
        public CShellItemUpdater(CShellItemHierachyManager hierachyManager, uint SHCNE_flags)
        {
            HierachyManager = hierachyManager;
            _eventFlags = SHCNE_flags;
            AllowUpdates = false;

            _backgroundThread = new Thread(RunBackgroundMessageLoop)
            {
                IsBackground = true,
                Name = "ShellItemUpdaterThread"
            };
            _backgroundThread.SetApartmentState(ApartmentState.STA);
            _backgroundThread.Start();

            // Wait until the HWND has been created and registered on the background thread
            _initializedEvent.WaitOne();
        }

        private void RunBackgroundMessageLoop()
        {
            // Create a message-only window (HWND_MESSAGE = -3)
            CreateParams cp = new CreateParams();
            cp.Caption = "CShellItemUpdaterMsgWindow";
            cp.ClassName = "Static"; // Use the standard Static window class - always registered
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE - message-only window
            cp.Style = 0;
            cp.ExStyle = 0;
            cp.X = 0;
            cp.Y = 0;
            cp.Width = 0;
            cp.Height = 0;
            CreateHandle(cp);

            // Subscribe to windows events        
            var entry = new SHChangeNotifyEntry()
            {
                pIdl = HierachyManager.Root.PIDL,
                Recursively = true
            };
            m_notifyId = SHChangeNotifyRegister(Handle, SHCNRF.InterruptLevel | SHCNRF.ShellLevel | SHCNRF.NewDelivery
                , (SHCNE)_eventFlags, (WM)((long)WM.USER + 200L), 1, new SHChangeNotifyEntry[] { entry });

            _initializedEvent.Set();

            Application.Run();
        }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == WindowsMessages.WM_DESTROY_THREAD_WINDOW)
            {
                DestroyHandle();
                Application.ExitThread();
                return;
            }

            if (!AllowUpdates) { 
                base.WndProc(ref msg); //the handle in the constructor can't be created unless this is called before exiting this wndproc
                return;
            }

            if (msg.Msg != (long)WM.USER + 200L)
            {
                base.WndProc(ref msg);
                return;
            }
            IntPtr ppidl = IntPtr.Zero;
            var msgID = default(SHCNE);
            SHNOTIFYSTRUCT shNotify = default;
            var hLock = SHChangeNotification_Lock(msg.WParam, (uint)msg.LParam, ref ppidl, ref msgID); //note: we are using the legacy notification struct, not the newer SHCNRF_NewDelivery mode.  While this block of memory is locked, you cannot free it's members.
            if (hLock == IntPtr.Zero) return;

            try
            {
                if (!IsItemNotificationEvent(msgID)) return;
                    
                msgID &= SHCNE.ALLEVENTS;
                shNotify = (SHNOTIFYSTRUCT)Marshal.PtrToStructure(ppidl, shNotify.GetType()); //note: shNotify is managed memory, not COM memory.  However the pointers inside of it still point to COM memory.

                // var UArgs = new CShItemUpdateEventArgs(shNotify, msgID, ref counter);
                // Debug.WriteLine("Enter WndProc -- Counter = " & UArgs.Tag & " - " & [Enum].GetName(GetType(SHCNE), CType(msgid, SHCNE)))
                // EventDump("Enter WndProc", shNotify, UArgs, msgID)
                Debug.Write("CShellItemUpdater.WndProc - Msg: " + msgID.ToString());

                // In the below test, only UPDATEDIR will ever give me just the Desktop's PIDL - which will appear as an Empty PIDL to IsPidlEmpty
                // If (Not CShellItem.IsPidlEmpty(shNotify.dwItem1)) OrElse (msgID = SHCNE.UPDATEDIR AndAlso shNotify.dwItem1 <> IntPtr.Zero) Then '5/21/2012
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
                    Debug.WriteLine(", dwItem1: " + CPidl.ToString(shNotify.dwItem1));
                }

                ///Debug.WriteLine(", dwItem1: " + shNotify.dwItem1.ToString("X"));

                lock (HierachyManager.Lock)
                {
                    CShellItem? parentItem = null;
                    IntPtr parentPidl = IntPtr.Zero;

                    switch (msgID)
                    {
                        // Item Changesq
                        case SHCNE.CREATE:
                            {
                                Debug.WriteLine("  [CREATE] processing...");
                                IntPtr realRel;
                                var splitPidl = CPidl.Split(shNotify.dwItem1);

                                parentItem = HierachyManager.FindItem(splitPidl.ParentPidl);
                                if (!(parentItem == null))
                                {
                                    Debug.WriteLine("  [CREATE] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.FilesInitialized)
                                    {
                                        if (!parentItem.FileList.Contains(shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("  [CREATE] Parent files initialized and item NOT in list. Adding.");
                                            if (SHGetRealIDL(parentItem.IShlFolder, splitPidl.ChildPidl, out realRel) == S_OK)
                                            {
                                                var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                if (newItem is not null)
                                                {
                                                    Debug.WriteLine("  [CREATE] Created newItem: " + newItem.ItemPath);
                                                    AddItem(parentItem, newItem);
                                                }
                                                else
                                                {
                                                    Debug.WriteLine("  [CREATE] CShellItemFactory.CreateCShItem returned null");
                                                }
                                            }
                                            else
                                            {
                                                Debug.WriteLine("  [CREATE] SHGetRealIDL failed");
                                            }
                                            Marshal.FreeCoTaskMem(realRel);
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
                        case SHCNE.DELETE: //keep in mind that windows can send the same delete message multiple times
                            Debug.WriteLine("  [DELETE] processing...");
                            var splitResult = CPidl.Split(shNotify.dwItem1);
                            parentPidl = splitResult.ParentPidl;
                            var relPidl = splitResult.ChildPidl;
                            Debug.WriteLine($"  {CPidl.ToString(shNotify.dwItem1)}");
                            Debug.WriteLine($"  {CPidl.ToString(parentPidl)}");
                            parentItem = HierachyManager.FindItem(parentPidl);

                            if (parentItem != null)
                            {
                                Debug.WriteLine("  [DELETE] Parent found: " + parentItem.ItemPath);
                                CShellItem childItem = null;

                                // Try to find the child item in either files or directories
                                if (parentItem.FileList != null)
                                    childItem = parentItem.FileList[relPidl];

                                if (childItem == null && parentItem.DirectoryList != null)
                                    childItem = parentItem.DirectoryList[relPidl];

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
                            break;
                        case SHCNE.RENAMEITEM:
                            Debug.WriteLine("  [RENAMEITEM] processing...");
                            if (shNotify.dwItem2 != IntPtr.Zero)
                            {
                                var item = HierachyManager.FindItem(shNotify.dwItem1);
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

                                return;
                                if (shNotify.dwItem1 == IntPtr.Zero || CPidl.SegmentCount(shNotify.dwItem1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEDIR] message with no location specified.");
                                    return;
                                }
                                else if (CPidl.SegmentCount(shNotify.dwItem1) == 1)
                                {
                                    if (HierachyManager?.CurrentFolder != null && CPidl.IsBinaryEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                    {
                                        Debug.WriteLine("  [UPDATEDIR] Updating CurrentFolder: " + HierachyManager.CurrentFolder.ItemPath);
                                        DoUpdate(HierachyManager.CurrentFolder, default, CShItemUpdateType.UpdateDir);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [UPDATEDIR] SegmentCount=1 but not CurrentFolder.");
                                    }
                                }
                                else
                                {
                                    var upCSI = HierachyManager.FindItem(shNotify.dwItem1);
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

                        case SHCNE.UPDATEITEM: //this is supposed to be items but that include directories sometimes
                            {
                                Debug.WriteLine("  [UPDATEITEM] processing...");
                                if (shNotify.dwItem1 == IntPtr.Zero || CPidl.SegmentCount(shNotify.dwItem1) == 0)
                                {
                                    Debug.WriteLine("  [UPDATEITEM] Empty pidl received from UPDATEITEM event");
                                }
                                else if (CPidl.SegmentCount(shNotify.dwItem1) == 1)
                                {
                                    if (HierachyManager?.CurrentFolder != null && CPidl.IsBinaryEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                    {
                                        if (shNotify.dwItem2 != IntPtr.Zero) Debug.WriteLine("[UPDATEITEM] : dwItem2=" + CPidl.ToString(shNotify.dwItem2));

                                        //Debug.WriteLine("[UPDATEITEM] Updating CurrentFolder: " + HierachyManager.CurrentFolder.ItemPath);
                                        //HierachyManager.CurrentFolder.Update(default, CShItemUpdateType.UpdateDir); //this is too expensive!  the update event happens too often
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [UPDATEITEM] SegmentCount=1 but not CurrentFolder.");
                                    }
                                }
                                else
                                {
                                    var item = HierachyManager.FindItem(shNotify.dwItem1);
                                    if (item is null)
                                    {
                                        Debug.WriteLine("  [UPDATEITEM] item was not found");
                                        return;
                                    }

                                    Debug.WriteLine("  [UPDATEITEM] Found/Added item: " + item.ItemPath + (item.IsFolder ? " (Folder)" : " (File)"));
                                    if (item.IsFolder)
                                    {
                                        DoUpdate(item, default, CShItemUpdateType.UpdateDir);
                                    }
                                    else
                                    {
                                        DoUpdate(item, IntPtr.Zero, CShItemUpdateType.Updated);
                                    }
                                }
                                //if (shNotify.dwItem1 != IntPtr.Zero) Marshal.FreeCoTaskMem(shNotify.dwItem1); //Do NOT do this.  Crashes the app after startup.  The memory is still locked.
                                break;
                            }

                        // Folder Changes
                        case SHCNE.MKDIR:
                        case SHCNE.DRIVEADD:
                            {
                                Debug.WriteLine("  [MKDIR/DRIVEADD] processing...");
                                // Make Directory
                                //IntPtr parent, child = IntPtr.Zero;
                                //parent = CPidl.SplitPidl(shNotify.dwItem1, ref child);
                                var splitPidls = CPidl.Split(shNotify.dwItem1);
                                parentItem = HierachyManager.FindItem(splitPidls.ParentPidl);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [MKDIR] Parent found: " + parentItem.ItemPath);
                                    if (parentItem.FoldersInitialized)
                                    {
                                        if (!parentItem.DirectoryList.Contains(shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("  [MKDIR] Parent folders initialized and NOT in list. Adding.");
                                            IntPtr realRel;
                                            if (SHGetRealIDL(parentItem.IShlFolder, splitPidls.ChildPidl, out realRel) == S_OK)
                                            {
                                                var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                if (newItem is not null)
                                                {
                                                    Debug.WriteLine("  [MKDIR] Created newItem: " + newItem.ItemPath);
                                                    AddItem(parentItem, newItem);
                                                    // Debug.WriteLine("MKDIR: " & newItem.Path)
                                                }
                                                else
                                                {
                                                    Debug.WriteLine("  [MKDIR] CShellItemFactory.CreateCShItem returned null");
                                                }
                                            }
                                            else
                                            {
                                                Debug.WriteLine("  ***MKDIR - Failed on SHGetRealIDL " + parentItem.DisplayName);
                                            }     // 6/30/2012
                                            Marshal.FreeCoTaskMem(realRel);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [MKDIR] Folder already in DirectoryList");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [MKDIR] Parent folders NOT initialized.");
                                        if (!IsVistaOrAbove())  // 6/27/2012 - XP will not send an UPDATEITEM for Parent in this case, so we have to
                                        {
                                            Debug.WriteLine("  [MKDIR] XP path: Updating parent.");
                                            DoUpdate(parentItem, IntPtr.Zero, CShItemUpdateType.Updated);
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("  ***MKDIR - Parent Not Found");
                                }     // 6/30/2012
                                Marshal.FreeCoTaskMem(splitPidls.ParentPidl);
                                Marshal.FreeCoTaskMem(splitPidls.ChildPidl);
                                break;
                            }
                        case SHCNE.RENAMEFOLDER:
                            Debug.WriteLine("  [RENAMEFOLDER] processing...");
                            // Renamed Directory
                            // If Not shNotify.dwItem2 <> IntPtr.Zero Then     '5/26/2012 - Old Code
                            if (shNotify.dwItem2 != IntPtr.Zero)          // 6/11/2012 - New Code
                            {
                                var item = HierachyManager.FindItem(shNotify.dwItem1);
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
                                // Removed Directory
                                var parent = CPidl.TrimLast(shNotify.dwItem1);

                                parentItem = HierachyManager.FindItem(parent);
                                if (parentItem is not null)
                                {
                                    Debug.WriteLine("  [RMDIR] Parent found: " + parentItem.ItemPath);
                                    // From Calum...sometimes when deleting a folder in My Documents 
                                    // parentItem.DirectoryList was Nothing...
                                    if (parentItem.DirectoryList is not null) // Added code from Calum
                                    {
                                        int indx = parentItem.DirectoryList.IndexOf(shNotify.dwItem1);
                                        if (indx > -1)
                                        {
                                            Debug.WriteLine("  [RMDIR] Found item in DirectoryList. Removing: " + parentItem.DirectoryList[indx].ItemPath);
                                            RemoveItem(parentItem, parentItem.DirectoryList[indx]);   // 7/2/2012 - incorrectly used Directories
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [RMDIR] Item NOT found in DirectoryList.");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [RMDIR] DirectoryList is null.");
                                        if (!IsVistaOrAbove())  // 6/27/2012 - XP will not send an UPDATEITEM for Parent in this case, so we have to
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
                            var mediaCSI = HierachyManager.FindItem(shNotify.dwItem1);
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
                            var imgCSI = HierachyManager.FindItem(shNotify.dwItem1);
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
                bool result = SHChangeNotification_Unlock(hLock) > 0;
                if (!result)
                {
                    Debug.WriteLine("UnLock Failed " + hLock.ToString());
                }
            }

            base.WndProc(ref msg);
        }

        //todo:move this into ShellController and CShellHierarchyManager
        /// <summary>For internal use only<br />
        /// Update is called by the CShItemUpdater Class when that Class receives a WM_Notify message. The purpose 
        /// of this Class is to translate the information passed to it into the appropriate set of actions needed 
        /// to maintain the internal cache and to, directly or indirectly (thru the routines it calls), Raise 
        /// CShItemUpdate events to notify the using application of changes.
        /// </summary>
        /// <param name="changedPidl">The absolute PIDL of the affected item. The definition of "affected item" 
        ///     varies with the type of change being reported.  This is only needed if the pidl changed due to 
        ///     rename, move, etc.</param>
        /// <param name="changeType">The type of change.</param>
        /// <remarks>Serves as a bridge between CShItemUpdater and the CShellItem that should handle a change.</remarks>
        internal void DoUpdate(CShellItem csi, IntPtr changedPidl, CShItemUpdateType changeType)
        {
            Debug.WriteLine("Entered CShellItemUpdater.Update: " + changeType.ToString());
            switch (changeType)
            {
                case CShItemUpdateType.UpdateDir: // raised when content of a dir changes
                    {
                        DoUpdateDir(csi); //todo: might want to keep track of possibly 'dirty' folders for UpdateDirs that come in with a blank pidl
                        break;
                    }
                case CShItemUpdateType.Updated: // raised when Attributes (Item or Items under a Folder) change
                    {
                        csi.ResetInfo();
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
                case CShItemUpdateType.Deleted:
                    {
                        RemoveItem(csi?.Parent, csi);
                        //UpdateEvent?.Invoke(this, new ShellItemUpdateEventArgs(this, changeType)); //removeitem will invoke the event
                        break;
                    }
                case CShItemUpdateType.Renamed:      // Item has been renamed or moved
                    {
                        bool flowControl = DoRenameOrMove(csi, changedPidl, changeType);
                        break;
                    }
                case CShItemUpdateType.IconChange:
                    {
                        // Debug.WriteLine("IconChange for " & Me.Path)
                        csi.ResetInfo();
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        
                        break;
                    }
                case CShItemUpdateType.MediaChange:          // CD/DVD/External Drive/Etc Added or Removed
                    {
                        // Debug.WriteLine("MediaChange for " & Me.Path)
                        csi.ClearItems(true, true);
                        csi.ResetInfo();
                        csi.m_Path = CShellItemFactory.GetFullPath(csi);
                        RaiseUpdateEvent(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
            }
        }

        /// <summary>
        /// Windows has this weird thing where a rename event can be either a rename or a move.  
        /// In the case of a move, the item is removed from the old location and added to the new location.  
        /// In the case of a rename, the item stays in the same location but changes name.  
        /// The PIDL passed to us in a rename event is the new PIDL, which is not necessarily the same as the old PIDL.  
        /// This function determines whether this is a move or a rename and updates the internal cache accordingly.  
        /// It also raises appropriate events to notify clients of changes.
        /// </summary>
        /// <param name="csi"></param>
        /// <param name="changedPidl"></param>
        /// <param name="changeType"></param>
        /// <returns>True for rename, false for move</returns>
        private bool DoRenameOrMove(CShellItem csi, nint changedPidl, CShItemUpdateType changeType)
        {
            IntPtr pidlRel = IntPtr.Zero, newIShellFolderPtr = IntPtr.Zero;
            var splitPidl = CPidl.Split(changedPidl);
            var oldParentCsi = csi.Parent;    // Save in case "renamed" to a new directory
            var allegedParentCsi = ShellController.Instance.HierachyManager.FindItem(splitPidl.ParentPidl);

            try
            {
                if (allegedParentCsi is null) // moved to a dir that is not yet in internal tree
                {
                    RemoveItem(csi.Parent, csi);
                    csi.m_Parent = null;
                    csi.m_Pidl = changedPidl;
                    return false;
                    //todo: shouldn't we add the newParentCsi to the tree at this point?
                }
                else if (SHGetRealIDL(allegedParentCsi.IShlFolder, splitPidl.ChildPidl, out pidlRel) == S_OK) // new parent of this item IS in internal tree, fix up and update any files/folders of THIS item
                {
                    Marshal.FreeCoTaskMem(csi.m_Pidl);
                    csi.m_Pidl = CPidl.Concatenate(splitPidl.ParentPidl, pidlRel);  //Must do this!  newPidlRel is a "simple" PIDL rather than a regular 1-item SHITEMID //don't do this: m_Pidl = changedPidl;

                    if (ReferenceEquals(allegedParentCsi, csi.Parent)) //renamed (does this really work?)
                    {
                        csi.ResetInfo();         // Added for fix to the fix
                        csi.m_Path = CShellItemFactory.GetFullPath(csi); ;
                        RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Renamed));
                        return true;
                    }
                    else // item was moved, not renamed
                    {
                        RemoveItem(csi.Parent, csi);
                        AddItem(allegedParentCsi,csi);

                        csi.m_Parent = allegedParentCsi;

                        csi.ResetInfo();
                        csi.m_Path = CShellItemFactory.GetFullPath(csi);

                        if (csi.IsFolder) //update children for folders
                        {
                            if (allegedParentCsi.IShlFolder.BindToObject(pidlRel, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref newIShellFolderPtr) != S_OK) //get new ishellfolder interface object
                            {
                                Marshal.Release(newIShellFolderPtr);
                                return false;
                            }
                            csi.m_IShellFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(newIShellFolderPtr, typeof(IShellFolder));
                            Marshal.Release(newIShellFolderPtr);

                            if (csi.m_Files is not null)
                            {
                                foreach (CShellItem item in csi.m_Files)
                                    item.UpdateFolderPidlAndPath(); //update child paths
                            }
                            if (csi.m_Directories is not null)
                            {
                                foreach (CShellItem item in csi.m_Directories)
                                    item.UpdateFolderPidlAndPath(); //update child paths
                            }
                        }
                        RaiseUpdateEvent(oldParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Moved)); //tell both old and new locations about the change
                        RaiseUpdateEvent(allegedParentCsi, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.Moved));
                        
                        return false;
                    }
                }
                else
                {
                    throw new Exception("Unhandaled condition in DoRenameOrMove");
                }
            }
            finally
            {
                // Note: FreeCoTaskMem will ignore IntPtr.Zero
                if (pidlRel != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidlRel);
                }
                Marshal.FreeCoTaskMem(splitPidl.ChildPidl);
                Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
            }

        }

        private bool _isUpdatingDir = false; //this is to prevent multiple simultaneous updates on the same folder which can cause problems.  We will ignore any update requests that come in while an update is already in progress.  This can happen when there are multiple changes to a folder in a short period of time, which causes multiple WM_UPDATEDIR messages to be fired before the first one has finished processing.
        /// <summary>
        /// The DoUpdateDir function is called when a WM_UPDATEDIR message is received, indicating 
        /// that the contents of a folder have changed. It compares the current content of the folder 
        /// with the internal cache (m_Directories and m_Files) and raises appropriate events for any 
        /// changes detected. The function takes parameters to specify whether to update files, folders, 
        /// or both. It returns the count of changes made. If an update is already in progress for the 
        /// same folder (possible because of multiple windows messages causing re-entrancey), it will 
        /// ignore subsequent update requests to prevent conflicts.
        /// </summary>
        /// <param name="csi"></param>
        /// <param name="updateFiles"></param>
        /// <param name="updateFolders"></param>
        /// <returns></returns>
        public int DoUpdateDir(CShellItem csi, bool updateFiles = true, bool updateFolders = true)
        {
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

        /// <summary>
        /// The UpdateRefresh function compares the Current content of the Folder with the
        /// current state of m_Directories and m_Files, adding/deleting CShItems as appropriate  (thus causing
        /// appropriate events to be raised for listening clients. 
        /// Called internally to handle WM_UPDATEDIR messages which map to CShItemUpdateType.UpdateDir. 
        /// This message indicates that the Contents of this Folder has changed.  Typically, it is fired 
        /// when multiple items are added/deleted. In practice, several explicit add/delete notification 
        /// messages are fired followed by WM_UPDATEDIR to indicate that there are more changes. 
        /// Certain other types of file operations (eg Save) use only WM_UPDATEDIR rather than WM_CREATE.
        /// </summary>
        /// <param name="updateFiles">True to examine Files of this folder for changes.</param>
        /// <param name="updateFolders">True to examine sub-directories of this folder for changes.</param>
        /// <returns>True if changes have been made, False otherwise</returns>
        /// <remarks>If m_Directories or m_Files is Nothing, then no attempt is made to compare with current 
        /// contents.  That is, if m_files is Nothing then it is not updated, m_Directories is treated the same.
        /// Note that m_xxxx.Count=0 is not the same thing as m_xxxx is Nothing! m_xxxx = Nothing means
        /// no one cares about the content.  m_xxxx.Count = 0 means that someone does care, but there were 
        /// no such items known until (perhaps) now.</remarks>
        /// <summary>
        /// Refreshes the information for this item from the shell and raises an Update event.
        /// </summary>
        private int SelectiveFolderUpdate(CShellItem? csi, bool updateFiles = true, bool updateFolders = true)
        {
            if (csi is null) return 0;
            if (!csi.m_IsFolder) return 0;

            Debug.WriteLine("SelectiveFolderUpdate begin - " + csi.Text + " - " + DateTime.Now.ToString("HH:mm:ss.fff"));

            var attrFlag = SHCONTF.INCLUDEHIDDEN;
            if (csi.m_Files is not null && updateFiles)
                attrFlag = attrFlag | SHCONTF.NONFOLDERS;
            if (csi.m_Directories is not null && updateFolders)
                attrFlag = attrFlag | SHCONTF.FOLDERS;
            if (attrFlag == SHCONTF.INCLUDEHIDDEN)
                return 0; // nothing loaded in the given csi yet.  we ignore csi's that haven't been loaded yet (usually loaded in the UI) because they are folders the user hasn't browsed to yet

            var newPidls = CShellItemFactory.GetPidlsOfFolder(csi, attrFlag); // Relative PIDLs of current content

            //the next bit is to prevent multiple instances of this function running at the same time on the same folder
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

            // 6/18/2012 - If something changed in this Folder, then Raise an Updated Event AFTER all Adds, Deletes, etc have been posted
            // 6/18/2012 - One was previously Raised when working down the Tree from Me's Parent, but Adds, Deletes, etc details had not been posted
            // 6/18/2012 - at that time. The App did not know HOW this Folder had changed (except for attributes)
            // these invokes MUST be within the lock or else you will get delete all items in the folder from memory for unknown reasons
            if (operations.Count > 0)
            {
                var folder = csi.IsFolder ? csi : csi.Parent;

                //if (operations.Count < 400)
                //{
                    foreach (var (item, type) in operations)
                    {
                        switch (type)
                        {
                            case CShItemUpdateType.Created:
                                RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Created));
                                break;
                            case CShItemUpdateType.Deleted:
                                RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted));
                                break;
                            case CShItemUpdateType.Renamed:
                                RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Renamed));
                                break;
                            case CShItemUpdateType.IconChange:
                            case CShItemUpdateType.Updated:
                            case CShItemUpdateType.MediaChange:
                            default:
                                RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Updated));
                                break;
                        }
                    }
                //}
                //else
                //{
                //    RaiseUpdateEvent(csi, new ShellItemUpdateEventArgs(null, CShItemUpdateType.UpdateDir));
                //}
            }

            return operations.Count;
        }

        private List<(CShellItem, CShItemUpdateType)> CrossCheckOldAndNewFolderContents(CShellItem csi, bool UpdateFiles, bool UpdateFolders, List<nint> newPidls)
        {
            Debug.WriteLine("CrossCheckOldAndNewFolderContents begin");
            var operations = new List<(CShellItem Item, CShItemUpdateType Type)>();

            lock (HierachyManager.Lock)
            {
                try
                {
                    if (newPidls.Count < 1) // no items currently in Folder, so wipe prior contents
                    {
                        var invalidItems = new List<CShellItem>(); // Holds CShItems no longer present

                        if (csi.m_Files is not null && UpdateFiles)
                            invalidItems.AddRange(csi.m_Files.ToArray());
                        if (csi.m_Directories is not null && UpdateFolders)
                            invalidItems.AddRange(csi.m_Directories.ToArray());

                        // any not found should be removed from my collections (raising event)
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
                        // Optimization: Use a dictionary to avoid O(N*M) search complexity.
                        var oldCsiDic = new Dictionary<string, CShellItem>();
                        if (csi.m_Directories is not null && UpdateFolders)
                        {
                            foreach (var item in csi.m_Directories.Items)
                                oldCsiDic.TryAdd(CPidl.ToString(item.LastPIDL, false) ?? string.Empty, item); //might want to save this dic between calls?  The problem with this is that we have to determine which items are orphans and that would require build a new dic to do the work in O(n) time so there's no benefit
                        }
                        if (csi.m_Files is not null && UpdateFiles)
                        {
                            foreach (var item in csi.m_Files.Items)
                            {
                                oldCsiDic.TryAdd(CPidl.ToString(item.LastPIDL, false) ?? string.Empty, item); //might want to save this dic between calls?  The problem with this is that we have to determine which items are orphans and that would require build a new dic to do the work in O(n) time so there's no benefit
                            }
                        }

#if DEBUG
                        Debug.WriteLine("\toldCsiDic size: " + oldCsiDic.Count());
                        Debug.WriteLine("\tnewPidls size: " + newPidls.Count());
#endif

                        Dictionary<string, FileInfo> fileInfos = null;
                        if (csi.IsFileSystem)
                        {
                            DirectoryInfo directoryInfo = new DirectoryInfo(csi.FullPath);

                            // Get all files in the directory
                            fileInfos = directoryInfo.GetFiles().ToDictionary(file => file.Name, file => file);
                        }

                        Debug.WriteLine("\tfetch fileinfo done - " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        for (int i = 0; i < newPidls.Count; i++)
                        {
                            IntPtr newPidl = newPidls[i];
                            if (newPidl == IntPtr.Zero) continue;
                            //uint hash = CPidl.HashPidlFastLastFull(newPidl);

                            string newFileName = CPidl.ToString(newPidl, false) ?? string.Empty;
                            if (oldCsiDic.TryGetValue(newFileName, out CShellItem? oldCsi))
                            {
                                if (oldCsi is null)
                                {
                                    Debug.WriteLine("ERROR: oldCsiDic contained a null value for key '" + newFileName + "'");
                                    continue;
                                }
                                
                                if (CPidl.IsBinaryEqual(oldCsi.LastPIDL, newPidl)) //additional check
                                {   // found the same item
                                    if (!ReferenceEquals(csi, CShellItemFactory.RecycleBin))
                                    {
                                        bool doupdate = false;
                                        if (csi.IsFileSystem)
                                        {
                                            if (fileInfos.TryGetValue(newFileName, out FileInfo fi))
                                            {
                                                if (fi.LastWriteTime > csi.LastWriteTime)
                                                    doupdate = true;
                                            }
                                            else doupdate = true;
                                            //todo: maybe also do a date check for virtual items since people might be using their onedrives
                                        }
                                        else doupdate = true;

                                        if (doupdate)
                                        {
                                            oldCsi.ResetInfo();
                                            if (oldCsi.IsFolder) oldCsi.ResetChildren();
                                            RaiseUpdateEvent(oldCsi.Parent, new ShellItemUpdateEventArgs(oldCsi, CShItemUpdateType.Updated)); //this happens even for items that aren't actually updated!
                                            operations.Add((oldCsi, CShItemUpdateType.Updated));
                                        }
                                    }

                                    Marshal.FreeCoTaskMem(newPidl);
                                    newPidls[i] = IntPtr.Zero; // Mark as processed
                                    oldCsiDic.Remove(newFileName);

                                    continue;
                                }
                            }
                            else //new item
                            {
                                var newItem = CShellItemFactory.CreateCShItem(newPidl, csi);
                                var result = HierachyManager.Add(newItem);
                                if (result is null) //this can happen for files that are deleted from outside this app
                                {
                                    //HierachyManager.Remove(newItem); not sure if we need this yet
                                }
                                else { 
                                    operations.Add((newItem, CShItemUpdateType.Created));
                                }
                            }
                        }

                        Debug.WriteLine("\tadditions done - " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        //any items remaining in the dictionary have no match with the current state of the folder.  Remove.
                        if (oldCsiDic.Count > 0)
                        {
                            foreach (var item in oldCsiDic.Values)
                            {
                                RemoveItem(csi, item);
                                operations.Add((item, CShItemUpdateType.Deleted));
                                Debug.WriteLine("\tremoved item from hierarchy '" + item.DisplayName + "'");
                            }
                            Debug.WriteLine("\tremovals done - " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        }
                    }
                }
                finally
                {
                }
            } //end lock

            return operations;
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        internal void AddItem(CShellItem parent, CShellItem item)
        {
            bool Changed = false;
            lock (HierachyManager.Lock)
            {
                try
                {
                    item.m_Parent = parent;
                    if (parent.IsFolder)
                    {
                        if (!parent.DirectoryList.Contains(item.PIDL))
                        {
                            parent.m_Directories.Append(item);
                            Changed = true;
                        }
                        if (!parent.FileList.Contains(item.PIDL))
                        {
                            parent.m_Files.Add(item);
                            Changed = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in CShellItem.AddItem -- " + ex.ToString());
                }
            }
            if (Changed)
            {
                CShellItemUpdater.RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Created));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool RemoveItem(CShellItem parent, CShellItem item)
        {
            bool changed = false;
            if (parent == null || item == null) return false;

            lock (HierachyManager.Lock)
            {
                try
                {
                    if (parent.IsFolder)
                    {
                        if (parent.FoldersInitialized && parent.m_Directories.Contains(item))
                        {
                            // Debug.WriteLine("Removing " & item.Path & " From " & Me.Path)
                            parent.m_Directories.Remove(item);
                            changed = true;
                        }

                        if (parent.FilesInitialized && parent.m_Files.Contains(item))
                        {
                            parent.m_Files.Remove(item);
                            changed = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in CShellItem.RemoveItem -- " + ex.ToString());
                }
            }

            if (changed)
            {
                RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted));
                //RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(parent, CShItemUpdateType.Updated));
            }

            return changed;
        }

        public static void RaiseUpdateEvent(object sender, ShellItemUpdateEventArgs e)
        {
            var handlers = UpdateEvent?.GetInvocationList();
            if (handlers == null) return;

            foreach (var handler in handlers)
            {
                if (handler.Target is System.Windows.Forms.Control control && control.InvokeRequired)
                {
                    control.BeginInvoke(handler, new object[] { sender, e });
                }
                else
                {
                    try
                    {
                        handler.DynamicInvoke(sender, e);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error invoking event handler: " + ex.ToString());
                    }
                }
            }
        }

        private bool IsItemNotificationEvent(SHCNE lEvent)
        {
            return !(
                (lEvent & (SHCNE.ASSOCCHANGED | SHCNE.EXTENDED_EVENT | SHCNE.FREESPACE | SHCNE.DRIVEADDGUI | SHCNE.SERVERDISCONNECT)) > 0
                );
        }

        /// <summary>
        /// Returns true if the given absolute PIDL belongs to (is a descendant of) the Recycle Bin,
        /// or IS the Recycle Bin itself.
        /// Compares only the first two PIDL segments (Desktop + Recycle Bin) using raw byte comparison
        /// to avoid shell API calls (ILIsEqual/ILIsParent).
        /// </summary>
        private static unsafe bool IsInRecycleBin(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) return false;

            var name = CPidl.ToString(pidl);

            if (name.ToUpper().Contains("$RECYCLE.BIN")) return true;

            var recycleBinPidl = CShellItemFactory.RecycleBin.PIDL;
            if (recycleBinPidl == IntPtr.Zero) throw new Exception("The Recycle Bin PIDL has not been set up.");

            if (name.Contains(CShellItemFactory.StrRecycleBin))
                return true;
            else return false;
        }

        /// <summary>
        /// CShItemUpdater.WndProc processes WM.SH_NOTIFY messages requested by the SHChangeNotifyRegister 
        /// API call in the CShItemUpdater constructor.
        /// Messages are processed as follows:
        /// 1.Folder/File Create or Delete: If Parent of Item is not in internal tree, ignore message. If
        /// located, then add or remove the item from the internal tree, which raises an appropriate event to
        /// notify interested controls.
        /// 2.Folder/File Rename, Update, UpdateDir, MediaInserted, MediaRemoved: 
        /// If Item itself is not in the internal tree, ignore message. 
        /// If located, then call Item.Update for further processing. 
        /// If appropriate, Item.Update will raise an appropriate event to notify
        /// interested controls.
        /// 
        /// </summary>
        /// <param name="m">A Windows Message</param>
        /// <remarks>The use of SHGetRealIDL appears non-essential and wasteful. It is NOT.
        /// SHGetRealIDL appears specifically designed for use in this situation, returning an 
        /// Absolute real PIDL in CoTaskMemory. The pidls given in dwItem1 and dwItem2 are owned and
        /// released by the Message Class. 
        /// The entire shell messaging system in windows is retarded and lame.  You will get nonsense events
        /// and events that didn't happen.  It will duplicate events.  It will drop events.  It will coalesce 
        /// events.  It will send events under the wrong category.  It will send events in the wrong order.
        /// It will send arguments that are incomplete.
        /// 
        /// </remarks>


        public new void Dispose()
        {
            if (m_notifyId > 0)
            {
                SHChangeNotifyDeregister(m_notifyId);
            }
            if (Handle != IntPtr.Zero)
            {
                PostMessage(Handle, WindowsMessages.WM_DESTROY_THREAD_WINDOW, IntPtr.Zero, IntPtr.Zero);
                if (_backgroundThread != null && _backgroundThread.IsAlive)
                {
                    _backgroundThread.Join(2000);
                }
            }
            _initializedEvent.Dispose();
            GC.SuppressFinalize(this);
        }


#if DEBUG
        private int counter;

        private bool EventDump(string txtID, SHNOTIFYSTRUCT shNotify, CShItemUpdateEventArgs e, SHCNE msgID)
        {
            bool EventDumpRet = default;
            EventDumpRet = false;
            string id = " -- Counter = " + e.Tag + " ";
            Debug.WriteLine(txtID + id + Enum.GetName(typeof(SHCNE), msgID));
            CShellItem csi1, csi2;
            var parent1 = default(CShellItem);
            if (shNotify.dwItem1 != IntPtr.Zero)     // 5/26/2012
            {
                csi1 = HierachyManager.FindItem(shNotify.dwItem1);
                if (csi1 is not null)
                {
                    // If csi1.Path.IndexOf("ntuser.dat", StringComparison.InvariantCultureIgnoreCase) > -1 Then  '6/6/2012 - No longer needed
                    // Return True
                    // End If
                    parent1 = csi1.Parent;
                    Debug.WriteLine(id + "dwItem1: " + " (" + shNotify.dwItem1.ToString() + ")" + csi1.ItemPath);
                    // DumpPidl(shNotify.dwItem1)
                    if (parent1 is not null)
                    {
                        Debug.WriteLine(id + "parent1: " + parent1.ItemPath);
                    }
                }
                else
                {
                    Debug.WriteLine(id + "dwItem1: " + " (" + shNotify.dwItem1.ToString() + ")" + " Not Found");
                    // DumpPidl(shNotify.dwItem1)
                    if (parent1 is not null)
                    {
                        Debug.WriteLine(id + "parent1: " + parent1.ItemPath);
                    }
                }
            }
            else
            {
                Debug.WriteLine(id + "dwItem1: Is Empty");
            }
            if (shNotify.dwItem2 != IntPtr.Zero)     // 5/26/2012
            {
                csi2 = HierachyManager.FindItem(shNotify.dwItem2);    // 5/26/2012
                if (csi2 is not null)
                {
                    Debug.WriteLine(id + "dwItem2: " + " (" + shNotify.dwItem2.ToString() + ")" + csi2.ItemPath);
                }
                else
                {
                    Debug.WriteLine(id + "dwItem2: " + " (" + shNotify.dwItem2.ToString() + ")" + " Not Found");
                }
            }
            else
            {
                Debug.WriteLine(id + "dwItem2: Is Empty");
            }

            return EventDumpRet;

        }
#endif

    }

#if DEBUG
    /// <summary>
    /// CShItemUpdateEventArgs is only used for development. It provides a container for information used to track the handling of
    /// WM_Notify messages.
    /// </summary>
    /// <remarks></remarks>
    internal class CShItemUpdateEventArgs : EventArgs
    {

        private SHNOTIFYSTRUCT m_shNotifyParams;

        public SHNOTIFYSTRUCT NotifyParams
        {
            get
            {
                return m_shNotifyParams;
            }
        }

        private SHCNE m_updateType;
        public SHCNE UpdateType
        {
            get
            {
                return m_updateType;
            }
        }

        private int m_Tag;
        public int Tag
        {
            get
            {
                return m_Tag;
            }
        }

        internal CShItemUpdateEventArgs(SHNOTIFYSTRUCT shNotifyParams, SHCNE updateType, ref int tag)
        {
            m_updateType = updateType;
            m_shNotifyParams = shNotifyParams;
            tag += 1;
            m_Tag = tag;
        }
    }
#endif



    /// <summary>
    /// CShItemUpdateType is an Enum of the various types of change that will be reported in a ShellItemUpdateEventArgs.
    /// </summary>
    /// <remarks>This Enum is also used by the CShItemUpdater Class to report change types to CShellItem.Update which passes it 
    ///          on to the Application.</remarks>
    public enum CShItemUpdateType
    {
        Created,
        IconChange,
        Updated,
        UpdateDir,
        Moved,
        Renamed,
        Deleted,
        MediaChange
    }

}
