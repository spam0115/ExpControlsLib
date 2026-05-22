using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        private readonly int m_notifyId;
        private uint _eventFlags = 0;

        public static event CShItemUpdateEventHandler UpdateEvent;

        public delegate void CShItemUpdateEventHandler(object sender, ShellItemUpdateEventArgs e);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DoUpdates { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="root"></param>
        /// <param name="SHCNE_flags"></param>
        public CShellItemUpdater(CShellItemHierachyManager hierachyManager, uint SHCNE_flags)
        {
            HierachyManager = hierachyManager;
            _eventFlags = SHCNE_flags;
            DoUpdates = false;

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
        }

        public new void Dispose()
        {
            if (m_notifyId > 0)
            {
                SHChangeNotifyDeregister(m_notifyId);
            }
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
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
                csi1 = HierachyManager.FindCShItem(shNotify.dwItem1);
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
                csi2 = HierachyManager.FindCShItem(shNotify.dwItem2);    // 5/26/2012
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

            var recycleBinPidl = CShellItemFactory.RecycleBin.PIDL;
            if (recycleBinPidl == IntPtr.Zero) return false;

            // Read segment sizes from the Recycle Bin PIDL (cached pointer, stable for app lifetime)
            ushort cb1 = (ushort)Marshal.ReadInt16(recycleBinPidl, 0);
            if (cb1 == 0) return false; // empty pidl
            ushort cb2 = (ushort)Marshal.ReadInt16(recycleBinPidl, cb1);
            if (cb2 == 0) return false; // only one segment (desktop root)
            int totalLen = cb1 + cb2;

            // Verify the incoming PIDL has at least as many bytes
            ushort inCb1 = (ushort)Marshal.ReadInt16(pidl, 0);
            if (inCb1 == 0) return false;
            ushort inCb2 = (ushort)Marshal.ReadInt16(pidl, inCb1);
            if (inCb2 == 0) return false;

            // Raw byte compare of the first two segments
            byte* pRecycle = (byte*)recycleBinPidl;
            byte* pIn = (byte*)pidl;
            for (int i = 0; i < totalLen; i++)
            {
                if (pRecycle[i] != pIn[i]) return false;
            }
            return true;
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
        protected override void WndProc(ref Message m)
        {
            if (!DoUpdates) { 
                base.WndProc(ref m); //the handle in the constructor can't be created unless this is called before exiting this wndproc
                return;
            }

            if (m.Msg != (long)WM.USER + 200L)
            {
                base.WndProc(ref m);
                return;
            }
            IntPtr ppidl = IntPtr.Zero;
            var msgID = default(SHCNE);
            SHNOTIFYSTRUCT shNotify = default;
            var hLock = SHChangeNotification_Lock(m.WParam, (uint)m.LParam, ref ppidl, ref msgID); //note: we are using the legacy notification struct, not the newer SHCNRF_NewDelivery mode.  While this block of memory is locked, you cannot free it's members.
            if (hLock != IntPtr.Zero)
            {
                try
                {
                    if (IsItemNotificationEvent(msgID))
                    {
                        msgID &= SHCNE.ALLEVENTS;
                        shNotify = (SHNOTIFYSTRUCT)Marshal.PtrToStructure(ppidl, shNotify.GetType()); //note: shNotify is managed memory, not COM memory.  However the pointers inside of it still point to COM memory.

#if DEBUG
                        // var UArgs = new CShItemUpdateEventArgs(shNotify, msgID, ref counter);
                        // Debug.WriteLine("Enter WndProc -- Counter = " & UArgs.Tag & " - " & [Enum].GetName(GetType(SHCNE), CType(msgid, SHCNE)))
                        // EventDump("Enter WndProc", shNotify, UArgs, msgID)
                        Debug.Write("CShellItemUpdater.WndProc, Msg: " + msgID.ToString());
#endif

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

#if DEBUG
                        if (shNotify.dwItem1 != IntPtr.Zero)
                        {
                            Debug.WriteLine(", dwItem1: " + CPidl.ToString(shNotify.dwItem1));
                        }
                        ///Debug.WriteLine(", dwItem1: " + shNotify.dwItem1.ToString("X"));
#endif

                        switch (msgID)
                        {
                            // Item Changes
                            case SHCNE.CREATE:
                                {
                                    Debug.WriteLine("  [CREATE] processing...");
                                    IntPtr realRel;
                                    var splitPidl = CPidl.Split(shNotify.dwItem1);

                                    var parentItem = HierachyManager.FindCShItem(splitPidl.ParentPidl);
                                    if (!(parentItem == null))
                                    {
                                        Debug.WriteLine("  [CREATE] Parent found: " + parentItem.ItemPath);
                                        if (parentItem.FilesInitialized)
                                        {
                                            if (!parentItem.FileList.Contains(shNotify.dwItem1))
                                            {
                                                Debug.WriteLine("  [CREATE] Parent files initialized and item NOT in list. Adding.");
                                                if (SHGetRealIDL(parentItem.Folder, splitPidl.ChildPidl, out realRel) == S_OK)
                                                {
                                                    var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                    if (newItem is not null)
                                                    {
                                                        Debug.WriteLine("  [CREATE] Created newItem: " + newItem.ItemPath);
                                                        parentItem.AddItem(newItem);
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

                            case SHCNE.DELETE:
                                {
                                    Debug.WriteLine("  [DELETE] processing...");
                                    var parentPidl = CPidl.TrimLast(shNotify.dwItem1);
                                    var parentItem = HierachyManager.FindCShItem(parentPidl);
                                    
                                    if (parentItem != null)
                                    {
                                        Debug.WriteLine("  [DELETE] Parent found: " + parentItem.ItemPath);
                                        var relPidl = CPidl.ILFindLastID(shNotify.dwItem1);
                                        CShellItem childItem = null;

                                        // Try to find the child item in either files or directories
                                        if (parentItem.FileList != null)
                                            childItem = parentItem.FileList[relPidl];
                                        
                                        if (childItem == null && parentItem.DirectoryList != null)
                                            childItem = parentItem.DirectoryList[relPidl];

                                        if (childItem != null)
                                        {
                                            Debug.WriteLine("  [DELETE] Child item found: " + childItem.ItemPath + ". Updating as deleted.");
#if DEBUG
                                            Debug.WriteLine("Received DELETE/RMDIR message: '" + childItem.FullPath + "'");
#endif
                                            Update(childItem, IntPtr.Zero, CShellItem.CShItemUpdateType.Deleted);
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
                                }

                            case SHCNE.RENAMEITEM:
                                {
                                    Debug.WriteLine("  [RENAMEITEM] processing...");
                                    if (shNotify.dwItem2 != IntPtr.Zero)     // 5/26/2012
                                    {
                                        var item = HierachyManager.FindCShItem(shNotify.dwItem1);
                                        if (item is not null)
                                        {
                                            Debug.WriteLine("  [RENAMEITEM] Item found: " + item.ItemPath + ". New PIDL: " + shNotify.dwItem2.ToString("X"));
                                            Update(item, shNotify.dwItem2, CShellItem.CShItemUpdateType.Renamed);
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
                                }

                            case SHCNE.UPDATEDIR:
                                {
                                    Debug.WriteLine("  [UPDATEDIR] processing...");
                                    if (shNotify.dwItem1 == IntPtr.Zero || CPidl.SegmentCount(shNotify.dwItem1) == 0)
                                    {
                                        if (HierachyManager?.CurrentFolder != null)
                                        {
                                            Debug.WriteLine("  [UPDATEDIR] Recieved UPDATEDIR message with no location specified. Trying to update current folder: " + HierachyManager.CurrentFolder.ItemPath);
                                            Update(HierachyManager.CurrentFolder, default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [UPDATEDIR] No location and no CurrentFolder.");
                                        }
                                    }
                                    else if (CPidl.SegmentCount(shNotify.dwItem1) == 1) 
                                    {
                                        if (HierachyManager?.CurrentFolder != null && CPidl.IsEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("  [UPDATEDIR] Updating CurrentFolder: " + HierachyManager.CurrentFolder.ItemPath);
                                            Update(HierachyManager.CurrentFolder, default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [UPDATEDIR] SegmentCount=1 but not CurrentFolder.");
                                        }
                                    }
                                    else
                                    {
                                        var upCSI = HierachyManager.FindCShItem(shNotify.dwItem1);
                                        if (upCSI is not null)
                                        {
                                            Debug.WriteLine("  [UPDATEDIR] Found item: " + upCSI.ItemPath + ". Updating dir.");
                                            Update(upCSI, default, CShellItem.CShItemUpdateType.UpdateDir);
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
                                        if (HierachyManager?.CurrentFolder != null && CPidl.IsEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                        {
                                            if (shNotify.dwItem2 != IntPtr.Zero) Debug.WriteLine("  [UPDATEITEM] : dwItem2=" + CPidl.ToString(shNotify.dwItem2));

                                            //Debug.WriteLine("  [UPDATEITEM] Updating CurrentFolder: " + HierachyManager.CurrentFolder.ItemPath);
                                            //HierachyManager.CurrentFolder.Update(default, CShellItem.CShItemUpdateType.UpdateDir); //this is too expensive!  the update event happens too often
                                        }
                                        else
                                        {
                                            Debug.WriteLine("  [UPDATEITEM] SegmentCount=1 but not CurrentFolder.");
                                        }
                                    }
                                    else
                                    {
                                        var item = HierachyManager.FindCShItem(shNotify.dwItem1);
                                        if (item is null) 
                                        {
                                            Debug.WriteLine("  [UPDATEITEM] item was not found");
                                            return;
                                        }
                                        
                                        Debug.WriteLine("  [UPDATEITEM] Found/Added item: " + item.ItemPath + (item.IsFolder ? " (Folder)" : " (File)"));
                                        if (item.IsFolder)
                                        {
                                            Update(item, default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                        else
                                        {
                                            Update(item, IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
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
                                    var parentItem = HierachyManager.FindCShItem(splitPidls.ParentPidl);
                                    if (parentItem is not null)
                                    {
                                        Debug.WriteLine("  [MKDIR] Parent found: " + parentItem.ItemPath);
                                        if (parentItem.FoldersInitialized)
                                        {
                                            if (!parentItem.DirectoryList.Contains(shNotify.dwItem1))
                                            {
                                                Debug.WriteLine("  [MKDIR] Parent folders initialized and NOT in list. Adding.");
                                                IntPtr realRel;
                                                if (SHGetRealIDL(parentItem.Folder, splitPidls.ChildPidl, out realRel) == S_OK)
                                                {
                                                    var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                    if (newItem is not null)
                                                    {
                                                        Debug.WriteLine("  [MKDIR] Created newItem: " + newItem.ItemPath);
                                                        parentItem.AddItem(newItem);
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
                                                Update(parentItem, IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
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
                                {
                                    Debug.WriteLine("  [RENAMEFOLDER] processing...");
                                    // Renamed Directory
                                    // If Not shNotify.dwItem2 <> IntPtr.Zero Then     '5/26/2012 - Old Code
                                    if (shNotify.dwItem2 != IntPtr.Zero)          // 6/11/2012 - New Code
                                    {
                                        var item = HierachyManager.FindCShItem(shNotify.dwItem1);
                                        if (item is not null)
                                        {
                                            Debug.WriteLine("  [RENAMEFOLDER] Found item: " + item.ItemPath + ". New PIDL: " + shNotify.dwItem2.ToString("X"));
                                            Update(item, shNotify.dwItem2, CShellItem.CShItemUpdateType.Renamed);
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
                                }

                            case SHCNE.RMDIR:
                            case SHCNE.DRIVEREMOVED:
                                {
                                    Debug.WriteLine("  [RMDIR/DRIVEREMOVED] processing...");
                                    // Removed Directory
                                    var parent = CPidl.TrimLast(shNotify.dwItem1);

                                    var parentItem = HierachyManager.FindCShItem(parent);
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
                                                parentItem.RemoveItem(parentItem.DirectoryList[indx]);   // 7/2/2012 - incorrectly used Directories
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
                                                Update(parentItem, IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
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
                                {
                                    Debug.WriteLine("  [MEDIA CHANGE] processing...");
                                    var mediaCSI = HierachyManager.FindCShItem(shNotify.dwItem1);
                                    if (mediaCSI is not null)
                                    {
                                        Debug.WriteLine("  [MEDIA CHANGE] Found item: " + mediaCSI.ItemPath + ". Updating.");
                                        Update(mediaCSI, default, CShellItem.CShItemUpdateType.MediaChange);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [MEDIA CHANGE] Item NOT found.");
                                    }

                                    break;
                                }
                            case SHCNE.UPDATEIMAGE:
                                {
                                    Debug.WriteLine("  [UPDATEIMAGE] processing...");
                                    var imgCSI = HierachyManager.FindCShItem(shNotify.dwItem1);
                                    if (imgCSI is not null)
                                    {
                                        Debug.WriteLine("  [UPDATEIMAGE] Found item: " + imgCSI.ItemPath + ". Updating icon.");
                                        Update(imgCSI, default, CShellItem.CShItemUpdateType.IconChange);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("  [UPDATEIMAGE] Item NOT found.");
                                    }

                                    break;
                                }
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
            }

            base.WndProc(ref m);
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
        internal void Update(CShellItem csi, IntPtr changedPidl, CShItemUpdateType changeType)
        {
            Debug.WriteLine("Entered CShellItemUpdater.Update: " + changeType.ToString());
            switch (changeType)
            {
                case CShItemUpdateType.UpdateDir: // raised when content of a dir changes
                    {
                        DoUpdateDir(csi); // recursively check this Folder and all known sub-Folders for change     '5/21/2012
                        break;
                    }
                case CShItemUpdateType.Updated: // raised when Attributes (Item or Items under a Folder) change
                    {
                        // Debug.WriteLine("Updated for " & Me.Path)
                        csi.ResetInfo();
                        // Previous versions called ResetChildren. Changed to UpdateRefresh - which impacts performance.
                        // Decided for now (6/12/2012) to do neither, so commented it out. This message is often closely followed or preceeded
                        // by an UPDATEDIR which will, in fact call UpdateRefresh which will also call ResetChildren in many cases.
                        // Performance impact is greatly aggravated by the (common on Win7) closely paired UPDATEDIR and UPDATEITEM messages
                        // on the same Folder, caused by the same change! Removing this code limits the impact.
                        // If Me.IsFolder Then
                        // 'Me.ResetChildren()     'Original code
                        // 'Me.UpdateRefresh()     '6/3/2012
                        // End If
                        UpdateEvent?.Invoke(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        //todo: update thumbnail for item
                        break;
                    }
                case CShItemUpdateType.Deleted:
                    {
                        csi.Parent?.RemoveItem(csi);
                        //UpdateEvent?.Invoke(this, new ShellItemUpdateEventArgs(this, changeType)); //removeitem will invoke the event
                        break;
                    }
                case CShItemUpdateType.Renamed:      // Item has been renamed or moved
                    {
                        IntPtr pidlRel = IntPtr.Zero, newIShellFolderPtr = IntPtr.Zero;
                        var splitPidl = CPidl.Split(changedPidl);
                        var oldParentCsi = csi.Parent;    // Save in case "renamed" to a new directory
                        var allegedParentCsi = ShellController.Instance.HierachyManager.FindCShItem(splitPidl.ParentPidl);

                        try
                        {
                            if (allegedParentCsi is null) // moved to a dir that is not yet in internal tree
                            {
                                csi.Parent.RemoveItem(csi);
                                csi.m_Parent = null;
                                csi.m_Pidl = changedPidl;
                                //todo: shouldn't we add the newParentCsi to the tree at this point?
                            }
                            else if (SHGetRealIDL(allegedParentCsi.Folder, splitPidl.ChildPidl, out pidlRel) == S_OK) // new parent of this item IS in internal tree, fix up and update any files/folders of THIS item
                            {
                                Marshal.FreeCoTaskMem(csi.m_Pidl);
                                csi.m_Pidl = CPidl.Concatenate(splitPidl.ParentPidl, pidlRel);  //Must do this!  newPidlRel is a "simple" PIDL rather than a regular 1-item SHITEMID //don't do this: m_Pidl = changedPidl;

                                if (ReferenceEquals(allegedParentCsi, csi.Parent)) //renamed
                                {
                                    csi.ResetInfo();         // Added for fix to the fix
                                    csi.m_Path = CShellItemFactory.GetFullPath(csi); ;
                                    UpdateEvent?.Invoke(oldParentCsi, new ShellItemUpdateEventArgs(csi, changeType));
                                }
                                else // item was moved, not renamed
                                {
                                    csi.Parent.RemoveItem(csi);
                                    allegedParentCsi.AddItem(csi);

                                    csi.m_Parent = allegedParentCsi;

                                    csi.ResetInfo();
                                    csi.m_Path = CShellItemFactory.GetFullPath(csi);

                                    if (csi.IsFolder) //update children for folders
                                    {
                                        if (allegedParentCsi.Folder.BindToObject(pidlRel, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref newIShellFolderPtr) != S_OK) //get new ishellfolder interface object
                                        {
                                            Marshal.Release(newIShellFolderPtr);
                                            return;
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
                                    UpdateEvent?.Invoke(oldParentCsi, new ShellItemUpdateEventArgs(csi, changeType)); //tell both old and new locations about the change
                                    UpdateEvent?.Invoke(allegedParentCsi, new ShellItemUpdateEventArgs(csi, changeType));
                                }
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
                        break;
                    }
                case CShItemUpdateType.IconChange:
                    {
                        // Debug.WriteLine("IconChange for " & Me.Path)
                        csi.ResetInfo();
                        UpdateEvent?.Invoke(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        //todo: update thumbnail for item
                        break;
                    }
                case CShItemUpdateType.MediaChange:          // CD/DVD/External Drive/Etc Added or Removed
                    {
                        // Debug.WriteLine("MediaChange for " & Me.Path)
                        csi.ClearItems(true, true);
                        csi.ResetInfo();
                        csi.m_Path = CShellItemFactory.GetFullPath(csi);
                        UpdateEvent?.Invoke(csi.Parent, new ShellItemUpdateEventArgs(csi, changeType));
                        break;
                    }
            }
        }

        private void DoUpdateDir(CShellItem CSI)
        {
            if (ReferenceEquals(CSI, CShellItemFactory.RecycleBin)) return;

            SelectiveFolderUpdate(CSI, true, true);
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
        /// <param name="UpdateFiles">True to examine Files of this folder for changes.</param>
        /// <param name="UpdateFolders">True to examine sub-directories of this folder for changes.</param>
        /// <returns>True if changes have been made, False otherwise</returns>
        /// <remarks>If m_Directories or m_Files is Nothing, then no attempt is made to compare with current 
        /// contents.  That is, if m_files is Nothing then it is not updated, m_Directories is treated the same.
        /// Note that m_xxxx.Count=0 is not the same thing as m_xxxx is Nothing! m_xxxx = Nothing means
        /// no one cares about the content.  m_xxxx.Count = 0 means that someone does care, but there were 
        /// no such items known until (perhaps) now.</remarks>
        /// <summary>
        /// Refreshes the information for this item from the shell and raises an Update event.
        /// </summary>
        public int SelectiveFolderUpdate(CShellItem? csi, bool UpdateFiles = true, bool UpdateFolders = true)
        {
            if (csi is null) return 0;
            if (!csi.m_IsFolder) return 0;

            var attrFlag = SHCONTF.INCLUDEHIDDEN;
            if (csi.m_Files is not null && UpdateFiles)
                attrFlag = attrFlag | SHCONTF.NONFOLDERS;
            if (csi.m_Directories is not null && UpdateFolders)
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

                operations = CrossCheckOldAndNewFolderContents(csi, UpdateFiles, UpdateFolders, newPidls);
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

                if (operations.Count < 100) //todo: change this to handle small numbers of changes without a full refresh
                {
                    foreach (var (item, type) in operations)
                    {
                        
                        switch (type)
                        {
                            case CShItemUpdateType.Created:
                                UpdateEvent?.Invoke(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.UpdateDir));
                                break;
                            case CShItemUpdateType.Updated:
                                UpdateEvent?.Invoke(csi, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Updated));
                                break;
                            case CShItemUpdateType.Deleted:
                            default:
                                UpdateEvent?.Invoke(csi.Parent, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.UpdateDir));
                                break;
                        }
                    }
                }
                else
                {
                    UpdateEvent?.Invoke(folder, new ShellItemUpdateEventArgs(csi, CShItemUpdateType.UpdateDir));
                }
            }

            return operations.Count;
        }

        public List<(CShellItem, CShItemUpdateType)> CrossCheckOldAndNewFolderContents(CShellItem csi, bool UpdateFiles, bool UpdateFolders, List<nint> newPidls)
        {
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
                                csi.RemoveItem(item);
                                operations.Add((item, CShItemUpdateType.Deleted));
                            }
                        }
                    }
                    else
                    {
                        // Optimization: Use a dictionary to avoid O(N*M) search complexity.
                        var oldCsiDic = new Dictionary<uint, CShellItem>();
                        if (csi.m_Directories is not null && UpdateFolders)
                        {
                            foreach (var item in csi.m_Directories.Items)
                                oldCsiDic.Add(CPidl.HashPidlFastLastFull(item.LastPIDL), item); //might want to save this dic between calls?  The problem with this is that we have to determine which items are orphans and that would require build a new dic to do the work in O(n) time so there's no benefit
                        }
                        if (csi.m_Files is not null && UpdateFiles)
                        {
                            foreach (var item in csi.m_Files.Items)
                                oldCsiDic.Add(CPidl.HashPidlFastLastFull(item.LastPIDL), item); //might want to save this dic between calls?  The problem with this is that we have to determine which items are orphans and that would require build a new dic to do the work in O(n) time so there's no benefit
                        }

#if DEBUG
                        Debug.WriteLine("oldCsiDic size: " + oldCsiDic.Count());
                        Debug.WriteLine("newPidls size: " + newPidls.Count());
#endif
                        for (int i = 0; i < newPidls.Count; i++)
                        {
                            IntPtr newPidl = newPidls[i];
                            uint hash = CPidl.HashPidlFastLastFull(newPidl);

                            if (oldCsiDic.TryGetValue(hash, out CShellItem oldCsi))
                            {
                                // found the same item
                                if (CPidl.IsEqual(oldCsi.LastPIDL, newPidl))
                                {
                                    if (!ReferenceEquals(csi, CShellItemFactory.RecycleBin))
                                    {
                                        bool doupdate = true;
                                        if (csi.IsFileSystem)
                                        {
                                            if (ShellHelper.TryGetLastWriteTimeForPidl(csi.Folder, newPidl, out FILETIME lastWriteTime))
                                            {
                                                var newTime = ShellHelper.FileTimeToLong(lastWriteTime);
                                                if (newTime <= csi.LastWriteTime.ToFileTimeUtc())
                                                    doupdate = false;
                                            }
                                            //todo: maybe also do a date check for virtual items since people might be using their onedrives
                                        }

                                        if (doupdate)
                                        {
                                            oldCsi.ResetInfo();
                                            if (oldCsi.IsFolder) oldCsi.ResetChildren();
                                            UpdateEvent?.Invoke(oldCsi.Parent, new ShellItemUpdateEventArgs(oldCsi, CShItemUpdateType.Updated)); //this happens even for items that aren't actually updated!
                                            operations.Add((oldCsi, CShItemUpdateType.Updated));
                                        }
                                    }

                                    Marshal.FreeCoTaskMem(newPidl);
                                    newPidls[i] = IntPtr.Zero; // Mark as processed
                                    oldCsiDic.Remove(hash);

                                    continue;
                                }
                            }
                            else //new item
                            {
                                if (newPidl == IntPtr.Zero) continue;

                                try
                                {
                                    var NewItem = CShellItemFactory.CreateCShItem(newPidl, csi);
                                    HierachyManager.Add(NewItem);
                                    operations.Add((NewItem, CShItemUpdateType.Created));
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("ERROR - Failed to add new CShellItem to internal tree.  : " + ex.ToString());
                                }
                            }
                        }

                        //any items remaining in the dictionary have no match with the current state of the folder.  Remove.
                        if (oldCsiDic.Count > 0)
                        {
                            foreach (var item in oldCsiDic.Values)
                            {
                                csi.RemoveItem(item);
                                operations.Add((item, CShItemUpdateType.Deleted));
                                Debug.WriteLine("removed item from hierarchy '" + csi.DisplayName + "'");
                            }
                        }
                    }
                }
                finally
                {
                }
            } //end lock

            return operations;
        }
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

}
