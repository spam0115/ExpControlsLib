using System.Collections.Concurrent;
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
        private readonly CShellItemUpdateLogic<CPidl> UpdateLogic;
        private int m_notifyId;
        private uint _eventFlags = 0;
        private Thread _backgroundThread;
        private readonly AutoResetEvent _initializedEvent = new AutoResetEvent(false);
        private readonly ConcurrentQueue<string> _pendingDirtyFolderRefreshKeys = new();

        public event CShItemUpdateEventHandler UpdateEvent;

        public delegate void CShItemUpdateEventHandler(object sender, ShellItemUpdateEventArgs e);

        /// <summary>
        /// This is a very important property that turns on actions in response to any updates.
        /// Unless this is set to true, CShellItemUpdater will be completely inert and useless.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AllowUpdates
        {
            get => UpdateLogic.AllowUpdates;
            set => UpdateLogic.AllowUpdates = value;
        }

        #region Constructors and setup
        /// <summary>
        /// 
        /// </summary>
        /// <param name="hierachyManager"></param>
        /// <param name="SHCNE_flags"></param>
        public CShellItemUpdater(CShellItemHierachyManager hierachyManager, uint SHCNE_flags)
        {
            HierachyManager = hierachyManager;
            _eventFlags = SHCNE_flags;
            UpdateLogic = new CShellItemUpdateLogic<CPidl>(HierachyManager, postDeferredDirtyFolderRefresh: PostDeferredDirtyFolderRefresh);
            UpdateLogic.UpdateEvent += (s, e) => RaiseUpdateEvent(s, e);

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

        private void PostDeferredDirtyFolderRefresh(string folderKey)
        {
            if (string.IsNullOrWhiteSpace(folderKey)) return;

            _pendingDirtyFolderRefreshKeys.Enqueue(folderKey);
            if (Handle == IntPtr.Zero || !PostMessage(Handle, WindowsMessages.WM_DIRTY_FOLDER_REFRESH, IntPtr.Zero, IntPtr.Zero))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Failed to post deferred dirty folder refresh message for '{folderKey}'.");
            }
        }

        private void RunBackgroundMessageLoop()
        {
            // SHChangeNotifyRegister requires the calling thread to have COM/OLE
            // initialized (STA). SetApartmentState(STA) on the Thread only marks
            // the apartment; it does NOT initialize COM. Without OleInitialize the
            // shell accepts the registration and returns a valid notifyId but never
            // actually delivers change notifications to the registered HWND.
            int oleHr = OleInitialize(IntPtr.Zero);
            try
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
                m_notifyId = SHChangeNotifyRegister(Handle, SHCNRF.InterruptLevel | SHCNRF.ShellLevel | SHCNRF.NewDelivery | SHCNRF.RecursiveInterrupt
                    , (SHCNE)_eventFlags, (WM)((long)WM.USER + 200L), 1, new SHChangeNotifyEntry[] { entry });

                _initializedEvent.Set();

                Application.Run();
            }
            finally
            {
                if (oleHr >= 0)
                {
                    OleUninitialize();
                }
            }
        }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == WindowsMessages.WM_DESTROY_THREAD_WINDOW)
            {
                DestroyHandle();
                Application.ExitThread();
                return;
            }

            if (msg.Msg == WindowsMessages.WM_DIRTY_FOLDER_REFRESH)
            {
                ProcessDeferredDirtyFolderRefreshMessages();
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

            UpdateLogic.HandleNotification(msg.WParam, msg.LParam);

            base.WndProc(ref msg);
        }

        private void ProcessDeferredDirtyFolderRefreshMessages()
        {
            while (_pendingDirtyFolderRefreshKeys.TryDequeue(out var folderKey))
            {
                try
                {
                    UpdateLogic.ProcessDeferredDirtyFolderRefresh(folderKey);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error processing deferred dirty folder refresh for '{folderKey}' -- {ex}");
                }
            }
        }

        #endregion

        #region Public Methods


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
            return UpdateLogic.DoUpdateDir(csi, updateFiles, updateFolders);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool RemoveItem(CShellItem parent, CShellItem item)
        {
            return UpdateLogic.RemoveItem(parent, item);
        }

        public void RaiseUpdateEvent(object sender, ShellItemUpdateEventArgs e)
        {
            var handlers = UpdateEvent?.GetInvocationList();
            if (handlers == null) return;

            foreach (var handler in handlers)
            {
                if (handler.Target is System.Windows.Forms.Control control)
                {
                    if (control.IsDisposed || control.Disposing)
                        continue;

                    if (control.InvokeRequired)
                    {
                        try
                        {
                            control.BeginInvoke(handler, new object[] { sender, e });
                        }
                        catch (InvalidOperationException)
                        {
                            // The control can be disposed between the checks
                            // above and BeginInvoke.  A stale UI subscriber
                            // must not terminate the shell-notification loop.
                        }
                        continue;
                    }
                }

                try
                {
                    handler.DynamicInvoke(sender, e);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error invoking event handler: " + ex.ToString());
                }
            }
        }

        /// <summary>
        /// Fires the events for a file move.
        /// </summary>
        /// <param name="item">the original file with old path</param>
        /// <param name="fullyQualifiedDestinationName">The full path and name of the destination file</param>
        public void OnMoveItem(CShellItem item, string fullyQualifiedDestinationName)
        {
            if (item == null || string.IsNullOrEmpty(fullyQualifiedDestinationName)) return;

            IntPtr newPidl = CPidl.PathToPidl(fullyQualifiedDestinationName);
            try
            {
                UpdateLogic.HandleMoved(item, newPidl);
            }
            finally
            {
                Marshal.FreeCoTaskMem(newPidl);
            }
        }

        public void OnMoveItem(CShellItem item, CShellItem newParent)
        {
            if (item == null || newParent == null) return;

            UpdateLogic.HandleMoved(item, newParent.PIDL);
        }

        #endregion

        public void Dispose()
        {
            UpdateLogic.DisposeDirtyFolderRefreshTimers();

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
