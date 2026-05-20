using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.SystemImageListManager;
using System.ComponentModel;

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
                csi1 = CShellItem.FindCShItem(shNotify.dwItem1);
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
                csi2 = CShellItem.FindCShItem(shNotify.dwItem2);    // 5/26/2012
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
            var hLock = SHChangeNotification_Lock(m.WParam, (uint)m.LParam, ref ppidl, ref msgID);
            if (hLock != IntPtr.Zero)
            {
                try
                {
                    if (IsItemNotificationEvent(msgID))
                    {
                        msgID &= SHCNE.ALLEVENTS;
                        shNotify = (SHNOTIFYSTRUCT)Marshal.PtrToStructure(ppidl, shNotify.GetType());

#if DEBUG
                        // var UArgs = new CShItemUpdateEventArgs(shNotify, msgID, ref counter);
                        // Debug.WriteLine("Enter WndProc -- Counter = " & UArgs.Tag & " - " & [Enum].GetName(GetType(SHCNE), CType(msgid, SHCNE)))
                        // EventDump("Enter WndProc", shNotify, UArgs, msgID)
                        Debug.Write("CShellItemUpdated.WndProc, Msg: " + msgID.ToString());
#endif

                        // In the below test, only UPDATEDIR will ever give me just the Desktop's PIDL - which will appear as an Empty PIDL to IsPidlEmpty
                        // If (Not CShellItem.IsPidlEmpty(shNotify.dwItem1)) OrElse (msgID = SHCNE.UPDATEDIR AndAlso shNotify.dwItem1 <> IntPtr.Zero) Then '5/21/2012
                        if (shNotify.dwItem1 == IntPtr.Zero) return;

#if DEBUG
                        Debug.WriteLine(", dwItem1: " + shNotify.dwItem1.ToString("X"));
#endif

                        switch (msgID)
                        {
                            // Item Changes
                            case SHCNE.CREATE:
                                {
                                    IntPtr realRel;
                                    var splitPidl = CPidl.Split(shNotify.dwItem1);

                                    var parentItem = CShellItem.FindCShItem(splitPidl.ParentPidl);
                                    if (!(parentItem == null))
                                    {
                                        if (parentItem.FilesInitialized && !parentItem.FileList.Contains(shNotify.dwItem1))
                                        {
                                            if (SHGetRealIDL(parentItem.Folder, splitPidl.ChildPidl, out realRel) == S_OK)
                                            {
                                                var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                if (newItem is not null)
                                                    parentItem.AddItem(newItem);
                                            }
                                            Marshal.FreeCoTaskMem(realRel);
                                        }
                                    }
                                    Marshal.FreeCoTaskMem(splitPidl.ParentPidl);
                                    Marshal.FreeCoTaskMem(splitPidl.ChildPidl);

                                    break;
                                }

                            case SHCNE.DELETE:
                                {
                                    var parent = CPidl.TrimLast(shNotify.dwItem1);
                                    CShellItem parentItem;
                                    parentItem = CShellItem.FindCShItem(parent);
                                    if (!(parentItem == null))
                                    {
                                        if (parentItem.FilesInitialized && parentItem.FileList.Contains(shNotify.dwItem1))
                                        {
                                            var childItem = parentItem.FileList[shNotify.dwItem1];
#if DEBUG
                                            Debug.WriteLine("Received DELETE message: '" + childItem.FullPath + "'");
#endif
                                            parentItem.RemoveItem(childItem);
                                        }
                                    }
                                    Marshal.FreeCoTaskMem(parent);
                                    break;
                                }

                            case SHCNE.RENAMEITEM:
                                {
                                    if (shNotify.dwItem2 != IntPtr.Zero)     // 5/26/2012
                                    {
                                        var item = CShellItem.FindCShItem(shNotify.dwItem1);
                                        if (item is not null)
                                        {
                                            item.Update(shNotify.dwItem2, CShellItem.CShItemUpdateType.Renamed);
                                        }
                                    }
                                    break;
                                }

                            case SHCNE.UPDATEDIR:
                                {
                                    if (shNotify.dwItem1 == IntPtr.Zero || CPidl.SegmentCount(shNotify.dwItem1) == 0)
                                    {
                                        if (HierachyManager?.CurrentFolder != null)
                                        {
                                            Debug.WriteLine("Recieved UPDATEDIR message with no location specified.  Trying to update current folder if it exists.");
                                            HierachyManager.CurrentFolder.Update(default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                    }
                                    else if (CPidl.SegmentCount(shNotify.dwItem1) == 1) 
                                    {
                                        if (HierachyManager?.CurrentFolder != null && CPidl.IsEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("updating dir from updatedir event");
                                            HierachyManager.CurrentFolder.Update(default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                    }
                                    else
                                    {
                                        var upCSI = CShellItem.FindCShItem(shNotify.dwItem1);
                                        if (upCSI is not null)
                                        {
                                            upCSI.Update(default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                    }

                                    break;
                                }

                            case SHCNE.UPDATEITEM: //this is supposed to be items but that include directories
                                {
                                    if (shNotify.dwItem1 == IntPtr.Zero || CPidl.SegmentCount(shNotify.dwItem1) == 0)
                                    {
                                        Debug.WriteLine("Empty pidl received from UPDATEITEM event");
                                    }
                                    else if (CPidl.SegmentCount(shNotify.dwItem1) == 1)
                                    {
                                        if (HierachyManager?.CurrentFolder != null && CPidl.IsEqual(HierachyManager.CurrentFolder.LastPIDL, shNotify.dwItem1))
                                        {
                                            Debug.WriteLine("updating dir from updateitem event");
                                            HierachyManager.CurrentFolder.Update(default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                    }
                                    else
                                    {
                                        var item = HierachyManager.FindInShellHierarchy(shNotify.dwItem1, out CShellItem parent);
                                        if (item is null) return;
                                        if (item.IsFolder)
                                        {
                                            item.Update(default, CShellItem.CShItemUpdateType.UpdateDir);
                                        }
                                        else
                                        {
                                            item.Update(IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
                                        }
                                    }

                                    break;
                                }

                            // Folder Changes
                            case SHCNE.MKDIR:
                            case SHCNE.DRIVEADD:
                                {
                                    // Make Directory
                                    //IntPtr parent, child = IntPtr.Zero;
                                    //parent = CPidl.SplitPidl(shNotify.dwItem1, ref child);
                                    var splitPidls = CPidl.Split(shNotify.dwItem1);
                                    var parentItem = CShellItem.FindCShItem(splitPidls.ParentPidl);
                                    if (parentItem is not null)
                                    {
                                        if (parentItem.FoldersInitialized && !parentItem.DirectoryList.Contains(shNotify.dwItem1))
                                        {
                                            IntPtr realRel;
                                            if (SHGetRealIDL(parentItem.Folder, splitPidls.ChildPidl, out realRel) == S_OK)
                                            {
                                                var newItem = CShellItemFactory.CreateCShItem(realRel, parentItem);
                                                if (newItem is not null)
                                                {
                                                    parentItem.AddItem(newItem);
                                                    // Debug.WriteLine("MKDIR: " & newItem.Path)
                                                }
                                            }
                                            else
                                            {
                                                Debug.WriteLine("***MKDIR - Failed on SHGetRealIDL " + parentItem.DisplayName);
                                            }     // 6/30/2012
                                            Marshal.FreeCoTaskMem(realRel);
                                        }
                                        else if (!IsVistaOrAbove())  // 6/27/2012 - XP will not send an UPDATEITEM for Parent in this case, so we have to
                                        {
                                            parentItem.Update(IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("***MKDIR - Parent Not Found");
                                    }     // 6/30/2012
                                    Marshal.FreeCoTaskMem(splitPidls.ParentPidl);
                                    Marshal.FreeCoTaskMem(splitPidls.ChildPidl);
                                    break;
                                }

                            case SHCNE.RENAMEFOLDER:
                                {
                                    // Renamed Directory
                                    // If Not shNotify.dwItem2 <> IntPtr.Zero Then     '5/26/2012 - Old Code
                                    if (shNotify.dwItem2 != IntPtr.Zero)          // 6/11/2012 - New Code
                                    {
                                        var item = CShellItem.FindCShItem(shNotify.dwItem1);
                                        if (item is not null)
                                        {
                                            item.Update(shNotify.dwItem2, CShellItem.CShItemUpdateType.Renamed);
                                        }
                                    }

                                    break;
                                }

                            case SHCNE.RMDIR:
                            case SHCNE.DRIVEREMOVED:
                                {
                                    // Removed Directory
                                    //IntPtr parent, child = IntPtr.Zero;
                                    //parent = CPidl.SplitPidl(shNotify.dwItem1, ref child);
                                    var parent = CPidl.TrimLast(shNotify.dwItem1);

                                    var parentItem = CShellItem.FindCShItem(parent);
                                    if (parentItem is not null)
                                    {
                                        // From Calum...sometimes when deleting a folder in My Documents 
                                        // parentItem.DirectoryList was Nothing...
                                        if (parentItem.DirectoryList is not null) // Added code from Calum
                                        {
                                            int indx = parentItem.DirectoryList.IndexOf(shNotify.dwItem1);
                                            if (indx > -1)
                                            {
                                                parentItem.RemoveItem(parentItem.DirectoryList[indx]);   // 7/2/2012 - incorrectly used Directories
                                            }
                                        }
                                        else if (!IsVistaOrAbove())  // 6/27/2012 - XP will not send an UPDATEITEM for Parent in this case, so we have to
                                        {
                                            parentItem.Update(IntPtr.Zero, CShellItem.CShItemUpdateType.Updated);
                                        }
                                    }
                                    //Marshal.FreeCoTaskMem(child);
                                    Marshal.FreeCoTaskMem(parent);
                                    break;
                                }
                            case SHCNE.MEDIAINSERTED:
                            case SHCNE.MEDIAREMOVED:
                                {
                                    var mediaCSI = CShellItem.FindCShItem(shNotify.dwItem1);
                                    if (mediaCSI is not null)
                                    {
                                        mediaCSI.Update(default, CShellItem.CShItemUpdateType.MediaChange);
                                    }

                                    break;
                                }
                            case SHCNE.UPDATEIMAGE:
                                {
                                    var imgCSI = CShellItem.FindCShItem(shNotify.dwItem1);
                                    if (imgCSI is not null)
                                    {
                                        imgCSI.Update(default, CShellItem.CShItemUpdateType.IconChange);
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
