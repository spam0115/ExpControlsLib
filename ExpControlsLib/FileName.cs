using WindowsApiLib.ShellDll;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using static WindowsApiLib.ShellDll.ShellAPI;
using static WindowsApiLib.ShellDll.ShellHelper;
using WindowsApiLib;
using ExpTreeLib;
using static WindowsApiLib.ShellDll.CShellItem;


namespace ExpListLib
{
    /// <summary>
    ///     This Form is a fully working start point for any form which requires an ExplorerTree and
    ///     ListView with enough room left for application specific controls.
    ///     ...
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class ExpList
    {
        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        private readonly DateTime EmptyTimeValue = new DateTime(1, 1, 1, 0, 0, 0);

        private CShellItem LastSelectedCSI;
        private CDragWrapper DW;
        private ClvDropWrapper DropWrap;
        private bool m_CreateNew = false;
        private ThumbnailImageListManager _thumbnailManager;

        public delegate void ExpListItemDoubleClickEventHandler(string SelPath, CShellItem Item);
        public event ExpListItemDoubleClickEventHandler ExpListItemDoubleClick;

        public delegate void ExpListItemMouseMBUpEventHandler(string SelPath, CShellItem Item);
        public event ExpListItemMouseMBUpEventHandler ExpListItemMouseMBUp;

        public delegate void ExpListItemArrowKeyUpEventHandler(string SelPath, CShellItem Item);
        public event ExpListItemArrowKeyUpEventHandler ExpListItemArrowKeyUp;

        public delegate void ExpListItemsChangedEventHandler(string SelPath, CShellItem Item);
        public event ExpListItemsChangedEventHandler ExpListItemsChanged;

        public event ExpListItemGetSelItemsEventHandler ExpListItemGetSelItems;
        public delegate void ExpListItemGetSelItemsEventHandler(ListView.SelectedListViewItemCollection listViewItemCollection);

        private const int InitialLoadLimit = 256;
        private const int EM_SETSEL = 0xB1;
        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETEDITCONTROL = LVM_FIRST + 24;

        /// <summary>
        /// Gets the pixel size for a given thumbnail display mode.
        /// </summary>
        private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        { ... }

        /// <summary>
        /// Loads thumbnails into image lists. Then assigns the image list to the ListView.
        /// </summary>
        private void LoadThumbnails(int thumbnailSize)
        { ... }

        /// <summary>
        /// Initializes a new instance of <see cref="ExpList"/>, wires up all event handlers
        /// for the control and its child <see cref="ExpFileList"/> ListView.
        /// </summary>
        public ExpList()
        {
            InitializeComponent();

            Load += ExpList_Load;
            VisibleChanged += ExpList_VisibleChanged;

            ExpFileList.HandleCreated += ExpFileList_HandleCreated;
            ExpFileList.DoubleClick += ExpFileList_DoubleClick;
            ExpFileList.Leave += ExpFileList_Leave;
            ExpFileList.BeforeLabelEdit += ExpFileList_BeforeLabelEdit;
            ExpFileList.AfterLabelEdit += ExpFileList_AfterLabelEdit;
            ExpFileList.MouseLeave += ExpFileList_MouseLeave;
            ExpFileList.MouseDown += ExpFileList_MouseDown;
            ExpFileList.MouseUp += ExpFileList_MouseUp;
            ExpFileList.KeyUp += ExpList_KeyUp;
            ExpFileList.KeyDown += ExpFileList_KeyDown;
        }

        #region Public Properties

        /// <summary>
        /// Gets or sets the display mode used to present items in the list view.
        /// </summary>
        [Browsable(true), Category("Appearance"),
         Description("Selects one of 8 different views that items can be shown in."),
         DefaultValue(View.Details)]
        public ListViewDisplayMode DisplayMode
        {
            get; set
            {
                if (value <= ListViewDisplayMode.Tile)
                    ExpFileList.View = (View)value;
                SetupImageListsForListView(value);
                field = value;
            }
        }

        /// <summary>
        /// Configures the image lists bound to the ListView for the given display mode.
        /// For built-in Windows view modes (Details, List, LargeIcon, Tile), the system image
        /// list is applied and each item's <see cref="ListViewItem.ImageIndex"/> is refreshed.
        /// For custom thumbnail modes, the ListView is switched to LargeIcon view and
        /// <see cref="LoadThumbnails"/> is called to populate thumbnail images.
        /// </summary>
        /// <param name="value">The <see cref="ListViewDisplayMode"/> to configure for.</param>
        private void SetupImageListsForListView(ListViewDisplayMode value)
        {
            if (value <= ListViewDisplayMode.Tile)
            {
                if (value == ListViewDisplayMode.LargeIcon)
                    SystemImageListManager.SetListViewImageList(ExpFileList, true, false);
                else
                    SystemImageListManager.SetListViewImageList(ExpFileList, false, false);

                bool large = (value == ListViewDisplayMode.LargeIcon);
                ExpFileList.BeginUpdate();
                try
                {
                    foreach (ListViewItem lvi in ExpFileList.Items)
                    {
                        if (lvi.Tag is CShellItem csi)
                            lvi.ImageIndex = SystemImageListManager.GetIconIndex(csi, large);
                        else
                            lvi.ImageIndex = -1;
                    }
                }
                finally { ExpFileList.EndUpdate(); }
            }
            else
            {
                ExpFileList.View = View.LargeIcon;
                LoadThumbnails(GetThumbnailSizeForMode(value));
            }
        }

        /// <summary>
        /// Gets or sets the current file system path displayed in <see cref="ExpFileList"/>.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current path of ExpFileList"),
         DefaultValue("")]
        public string CurrentPath
        {
            get => _CurrentPath;
            set => _CurrentPath = value;
        }
        private string _CurrentPath = "Desktop";

        /// <summary>
        /// Gets the <see cref="CShellItem"/> that corresponds to the currently displayed folder.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current CSI of ExpFileList"),
         DefaultValue("")]
        public CShellItem CurrentCSI => LastSelectedCSI;

        #endregion

        #region Form Load/VisibleChanged ExpFileList HandleCreated

        /// <summary>
        /// Handles the <see cref="Control.Load"/> event.
        /// Initialises drag-and-drop wrappers, the thumbnail manager, shell change
        /// notifications, removes unwanted columns, and sets the initial display mode.
        /// </summary>
        private void ExpList_Load(object sender, EventArgs e)
        {
            DW = new CDragWrapper(ExpFileList);
            DropWrap = new ClvDropWrapper(ExpFileList);
            _thumbnailManager = new ThumbnailImageListManager(ExpFileList);
            CShItemUpdate += UpdateInvoke;

            for (int i = ExpFileList.Columns.Count - 1; i >= 0; i--)
            {
                var column = ExpFileList.Columns[i];
                if (column.Text == "Type" || column.Text == "Attributes")
                    ExpFileList.Columns.RemoveAt(i);
            }

            DisplayMode = (ListViewDisplayMode)ExpFileList.View;
        }

        /// <summary>
        /// Handles the <see cref="Control.HandleCreated"/> event for <see cref="ExpFileList"/>.
        /// Reserved for future image-list initialisation that must occur after the window handle exists.
        /// </summary>
        private void ExpFileList_HandleCreated(object sender, EventArgs e)
        {
            // Reserved – image list setup is deferred to ExpList_VisibleChanged / SetupImageListsForListView.
        }

        /// <summary>
        /// Handles the <see cref="Control.VisibleChanged"/> event.
        /// Re-applies the current image lists whenever the control becomes visible,
        /// ensuring icons/thumbnails are correctly bound after the window handle is ready.
        /// </summary>
        private void ExpList_VisibleChanged(object sender, EventArgs e)
        {
            SetupImageListsForListView(DisplayMode);
        }

        #endregion

        #region ExplorerTree Event Handling -- AfterNodeSelect

        /// <summary>
        /// Overrides the default window procedure to forward owner-draw and menu-character
        /// messages to the active shell context menu (<see cref="m_WindowsContextMenu"/>).
        /// This is required so that shell context menus with custom-drawn items (e.g. the
        /// "New" submenu on Vista+) render and respond correctly.
        /// </summary>
        protected override void WndProc(ref Message m)
        { ... }

        /// <summary>
        /// Populates <see cref="ExpFileList"/> with the contents of the specified shell folder.
        /// Directories are listed first (when <paramref name="includeFolder"/> is <c>true</c>),
        /// followed by files, both sorted alphabetically.  Icons for the first
        /// <see cref="InitialLoadLimit"/> items are fetched before <c>EndUpdate</c>; the
        /// remainder are fetched afterwards to keep the UI responsive.  If a thumbnail view
        /// mode is active, thumbnails are loaded after the item list is built.
        /// </summary>
        /// <param name="pathName">The file-system path (or display name for virtual folders)
        /// that identifies the folder being displayed.  Stored in <see cref="CurrentPath"/>.</param>
        /// <param name="CSI">The <see cref="CShellItem"/> representing the folder to display.</param>
        /// <param name="includeFolder">
        /// <c>true</c> to include sub-folders in the listing; <c>false</c> to show files only.
        /// </param>
        /// <param name="reload">
        /// When <c>false</c> (default) the method returns immediately if <paramref name="CSI"/>
        /// is the same folder that is already displayed.  Pass <c>true</c> to force a reload.
        /// </param>
        public void DisplayFiles(string pathName, CShellItem CSI, bool includeFolder, bool reload = false)
        { ... }

        #endregion

        #region MakeLVItem

        /// <summary>
        /// Creates a <see cref="ListViewItem"/> for the given <see cref="CShellItem"/>.
        /// The item's sub-items are populated with file size, last-write time, and creation
        /// time.  Each sub-item's <see cref="ListViewItem.ListViewSubItem.Tag"/> is set to
        /// the raw typed value (e.g. <see cref="long"/> for size, <see cref="DateTime"/> for
        /// dates) so that <see cref="LVColSorter"/> can sort columns correctly.
        /// </summary>
        /// <param name="item">The shell item to represent.</param>
        /// <returns>A fully populated <see cref="ListViewItem"/> whose
        /// <see cref="ListViewItem.Tag"/> references <paramref name="item"/>.</returns>
        private ListViewItem MakeLVItem(CShellItem item)
        { ... }

        #endregion

        #region Dynamic Update Handler

        private delegate void InvokeUpdate(object sender, ShellItemUpdateEventArgs e);
        private readonly InvokeUpdate m_InvokeUpdate;

        /// <summary>
        /// Gets the <see cref="CShellItem"/> for the folder currently shown in the list view.
        /// </summary>
        public CShellItem SelectedItem => LastSelectedCSI;

        /// <summary>
        /// Receives shell change-notification events from <see cref="CShItemUpdate"/> and
        /// marshals them to the UI thread if necessary before delegating to
        /// <see cref="DoItemUpdate"/>.  After a <c>Created</c> or <c>Deleted</c> event the
        /// <see cref="ExpListItemsChanged"/> event is raised so that the host application can
        /// react (e.g. update a status bar).
        /// </summary>
        private void UpdateInvoke(object sender, ShellItemUpdateEventArgs e)
        {
            if (InvokeRequired)
                Invoke(m_InvokeUpdate, sender, e);
            else
            {
                DoItemUpdate(sender, e);

                if (e.UpdateType == CShItemUpdateType.Created || e.UpdateType == CShItemUpdateType.Deleted)
                {
                    if (LastSelectedCSI.Path.StartsWith(":"))
                        ExpListItemsChanged?.Invoke(LastSelectedCSI.DisplayName, LastSelectedCSI);
                    else
                        ExpListItemsChanged?.Invoke(LastSelectedCSI.Path, LastSelectedCSI);
                }
            }
        }

        /// <summary>
        /// Applies a single shell change-notification event to <see cref="ExpFileList"/>.
        /// Only events whose parent folder matches <see cref="LastSelectedCSI"/> are processed.
        /// Handles the following update types:
        /// <list type="bullet">
        ///   <item><description><c>Created</c> – adds a new item and optionally begins label edit for "New" items.</description></item>
        ///   <item><description><c>Deleted</c> – removes the corresponding item.</description></item>
        ///   <item><description><c>Renamed</c> – updates the item text and re-inserts it in sorted order, or removes it if it moved out of the current folder.</description></item>
        ///   <item><description><c>Updated</c> – replaces the item in-place with refreshed metadata.</description></item>
        ///   <item><description><c>IconChange</c> – refreshes the item's icon or thumbnail.</description></item>
        ///   <item><description><c>MediaChange</c> – updates the display name and icon (e.g. when removable media is inserted).</description></item>
        /// </list>
        /// </summary>
        private void DoItemUpdate(object sender, ShellItemUpdateEventArgs e)
        { ... }

        /// <summary>
        /// Searches <see cref="ExpFileList"/> for the <see cref="ListViewItem"/> whose
        /// <see cref="ListViewItem.Tag"/> is the same object reference as <paramref name="item"/>.
        /// </summary>
        /// <param name="item">The shell item to locate.</param>
        /// <returns>The matching <see cref="ListViewItem"/>, or <c>null</c> if not found.</returns>
        private ListViewItem FindLVItem(CShellItem item)
        {
            foreach (ListViewItem lvi in ExpFileList.Items)
            {
                if (ReferenceEquals(lvi.Tag, item))
                    return lvi;
            }
            return null;
        }

        /// <summary>
        /// Inserts <paramref name="lvi"/> into <paramref name="lv"/> at the correct sorted
        /// position by comparing its <see cref="CShellItem"/> tag against existing items using
        /// <see cref="CShellItem.CompareTo"/>.  If no existing item sorts after it, the item is
        /// appended.  <see cref="ListViewItem.EnsureVisible"/> is called so the newly inserted
        /// item scrolls into view.
        /// </summary>
        /// <param name="lvi">The item to insert.</param>
        /// <param name="lv">The target <see cref="ListView"/>.</param>
        private void InsertLvi(ListViewItem lvi, ListView lv)
        {
            var item = (CShellItem)lvi.Tag;
            for (int i = 0; i < lv.Items.Count; i++)
            {
                if (((CShellItem)lv.Items[i].Tag).CompareTo(item) > 0)
                {
                    lv.Items.Insert(i, lvi);
                    lvi.EnsureVisible();
                    return;
                }
            }
            lv.Items.Add(lvi);
            lvi.EnsureVisible();
        }

        #endregion

        #region ExpFileList_DoubleClick

        /// <summary>
        /// Handles a double-click on <see cref="ExpFileList"/>.
        /// If the selected item is a folder, raises <see cref="ExpListItemDoubleClick"/> so
        /// the host can navigate into it.  If it is a file, launches it via
        /// <see cref="LaunchFile"/>.
        /// </summary>
        private void ExpFileList_DoubleClick(object sender, EventArgs e)
        { ... }

        #endregion

        #region ExpFileList_Leave

        /// <summary>
        /// Handles the <see cref="Control.Leave"/> event for <see cref="ExpFileList"/>.
        /// Clears the selection when the control loses focus so that no items appear
        /// highlighted while the control is inactive.
        /// </summary>
        private void ExpFileList_Leave(object sender, EventArgs e)
        {
            ExpFileList.SelectedItems.Clear();
        }

        #endregion

        #region LabelEdit Handlers (Item Rename)

        /// <summary>
        /// Handles the <see cref="ListView.BeforeLabelEdit"/> event.
        /// Pre-selects only the base file name (without extension) in the edit box so the
        /// user does not accidentally rename the extension.  Cancels the edit and plays a
        /// beep if the item is not renameable (non-filesystem, disk root, My Documents, or
        /// <see cref="CShellItem.CanRename"/> is <c>false</c>).
        /// </summary>
        private void ExpFileList_BeforeLabelEdit(object sender, LabelEditEventArgs e)
        { ... }

        /// <summary>
        /// Handles the <see cref="ListView.AfterLabelEdit"/> event.
        /// Validates the new name (non-empty, no invalid path characters, must have a parent
        /// path separator) and delegates the actual rename to the shell via
        /// <see cref="IShellFolder.SetNameOf"/>.  Cancels the edit and plays a beep on any
        /// validation failure or shell error.
        /// </summary>
        private void ExpFileList_AfterLabelEdit(object sender, LabelEditEventArgs e)
        { ... }

        #endregion

        #region Context Menu Handlers

        private readonly ExpTreeLib.ContextMenu m_WindowsContextMenu = new ExpTreeLib.ContextMenu();
        private bool m_OutOfRange;
        private readonly InvokeUpdate m_InvokeUpdateInitializer;

        /// <summary>
        /// Determines whether the mouse cursor described by <paramref name="e"/> is within
        /// the client area of <paramref name="ctl"/>.
        /// </summary>
        /// <param name="ctl">The control whose client rectangle is tested.</param>
        /// <param name="e">Mouse event arguments providing the cursor coordinates.</param>
        /// <returns><c>true</c> if the cursor is inside the client rectangle; otherwise <c>false</c>.</returns>
        private bool IsWithin(Control ctl, MouseEventArgs e)
        {
            if (e.X < 0 || e.Y < 0) return false;
            Rectangle cr = ctl.ClientRectangle;
            if (e.X > cr.Width || e.Y > cr.Height) return false;
            return true;
        }

        /// <summary>
        /// Re-sorts all items currently in <see cref="ExpFileList"/> using
        /// <see cref="TagComparer"/>, which compares items by their <see cref="CShellItem"/> tags.
        /// Used after a Refresh command to restore the correct sort order without reloading
        /// from the shell.
        /// </summary>
        private void SortLVItems()
        {
            if (ExpFileList.Items.Count < 2) return;

            ExpFileList.BeginUpdate();
            var tmp = new ListViewItem[ExpFileList.Items.Count];
            ExpFileList.Items.CopyTo(tmp, 0);
            Array.Sort(tmp, new TagComparer());
            ExpFileList.Items.Clear();
            ExpFileList.Items.AddRange(tmp);
            ExpFileList.EndUpdate();
        }

        /// <summary>
        /// Handles the <see cref="Control.MouseLeave"/> event for <see cref="ExpFileList"/>.
        /// Sets <see cref="m_OutOfRange"/> to suppress a right-click context menu that would
        /// otherwise appear when the mouse button is released outside the control.
        /// </summary>
        private void ExpFileList_MouseLeave(object sender, EventArgs e) => m_OutOfRange = true;

        /// <summary>
        /// Handles the <see cref="Control.MouseDown"/> event for <see cref="ExpFileList"/>.
        /// Clears the out-of-range flag when a right mouse button press begins inside the
        /// control, allowing the subsequent <c>MouseUp</c> to show the context menu.
        /// </summary>
        private void ExpFileList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) m_OutOfRange = false;
        }

        /// <summary>
        /// Handles the <see cref="Control.MouseUp"/> event for <see cref="ExpFileList"/>.
        /// <para>
        /// On a right-click that is within the control bounds and not flagged as out-of-range:
        /// <list type="bullet">
        ///   <item><description>If one or more items are selected, shows the shell context menu
        ///   for those items via <see cref="ExpTreeLib.ContextMenu.ShowMenu"/>.</description></item>
        ///   <item><description>Otherwise, shows the custom folder context menu via
        ///   <see cref="ShowAndHandleContextMenu"/>.</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// On a middle-click with a selected item, raises <see cref="ExpListItemMouseMBUp"/>.
        /// </para>
        /// Always raises <see cref="ExpListItemGetSelItems"/> with the current selection.
        /// </summary>
        private void ExpFileList_MouseUp(object sender, MouseEventArgs e)
        { ... }

        /// <summary>
        /// Builds the two native Win32 popup menus used by <see cref="ShowAndHandleContextMenu"/>:
        /// a main context menu and a "View" submenu.
        /// <para>
        /// The View submenu contains radio-checked entries for each supported
        /// <see cref="ListViewDisplayMode"/>.  The main menu contains Refresh, Select All,
        /// Paste, Paste Link, a "New" submenu (for writable folders), and Properties.
        /// Paste and Paste Link are enabled only when the clipboard contains compatible data
        /// as determined by <c>CanDropClipboard</c>.
        /// </para>
        /// </summary>
        /// <param name="comContextMenu">
        /// Receives the handle to the newly created main popup menu (HMENU).
        /// The caller is responsible for releasing this handle.
        /// </param>
        /// <param name="viewSubMenu">
        /// Receives the handle to the newly created View submenu (HMENU).
        /// The caller is responsible for releasing this handle.
        /// </param>
        private void CreateContextMenu(out IntPtr comContextMenu, out IntPtr viewSubMenu)
        { ... }

        /// <summary>
        /// Displays a context menu for the ListView when no items are selected.
        /// ... (existing summary XML doc retained)
        /// </summary>
        private void ShowAndHandleContextMenu(Point pt)
        { ... }

        #endregion

        #region Keyboard Events

        /// <summary>
        /// Handles the <see cref="Control.KeyUp"/> event for <see cref="ExpFileList"/>.
        /// When an arrow key is released and at least one item is selected, raises
        /// <see cref="ExpListItemArrowKeyUp"/> with the path and <see cref="CShellItem"/> of the
        /// first selected item so the host can react to keyboard navigation.
        /// </summary>
        private void ExpList_KeyUp(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                 e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                && ExpFileList.SelectedItems.Count > 0)
            {
                var csi = (CShellItem)ExpFileList.SelectedItems[0].Tag;
                ExpListItemArrowKeyUp?.Invoke(csi.Path, csi);
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.KeyDown"/> event for <see cref="ExpFileList"/>.
        /// Implements the following keyboard shortcuts:
        /// <list type="bullet">
        ///   <item><description><c>Ctrl+A</c> – selects all items.</description></item>
        ///   <item><description><c>Ctrl+X/C/V</c> – cut, copy, paste via <see cref="WinMenu"/>.</description></item>
        ///   <item><description><c>Delete</c> – deletes selected items via <see cref="WinMenu"/>; forces a refresh if more than 150 items were selected.</description></item>
        ///   <item><description><c>F2</c> – begins in-place rename of the first selected item.</description></item>
        ///   <item><description><c>F5</c> – refreshes the folder and re-sorts items.</description></item>
        ///   <item><description><c>Enter</c> – navigates into a folder or launches a file, mirroring double-click behaviour.</description></item>
        /// </list>
        /// </summary>
        private void ExpFileList_KeyDown(object sender, KeyEventArgs e)
        { ... }

        /// <summary>
        /// Launches the file represented by <paramref name="csi"/> using the operating
        /// system's default handler (<see cref="ProcessStartInfo.UseShellExecute"/> = <c>true</c>).
        /// </summary>
        /// <param name="csi">The shell item whose <see cref="CShellItem.Path"/> is to be opened.</param>
        private void LaunchFile(CShellItem csi)
        {
            var psi = new ProcessStartInfo
            {
                FileName = csi.Path,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        /// <summary>
        /// Executes a shell verb (e.g. "cut", "copy", "paste", "delete") on the currently
        /// selected items or the current folder, using the shell's <see cref="IContextMenu"/>
        /// interface.
        /// <para>
        /// For "paste", the verb is invoked on <see cref="LastSelectedCSI"/> (the folder
        /// itself).  For all other verbs, it is invoked on the selected items within the
        /// folder.  Before deleting, each item is checked via <see cref="CShellItem.CanDelete"/>;
        /// a warning is shown and the operation is aborted if any item cannot be deleted.
        /// </para>
        /// </summary>
        /// <param name="cmd">The shell verb string to invoke (e.g. <c>"cut"</c>, <c>"copy"</c>,
        /// <c>"paste"</c>, <c>"delete"</c>).</param>
        private void WinMenu(string cmd)
        { ... }

        /// <summary>
        /// Returns <c>true</c> when the current <see cref="DisplayMode"/> is one of the three
        /// thumbnail modes (<see cref="ListViewDisplayMode.Thumbnail"/>,
        /// <see cref="ListViewDisplayMode.LargeThumbnail"/>, or
        /// <see cref="ListViewDisplayMode.ExtraLargeThumbnail"/>).
        /// </summary>
        private bool IsThumbnailViewMode() =>
            DisplayMode == ListViewDisplayMode.Thumbnail ||
            DisplayMode == ListViewDisplayMode.LargeThumbnail ||
            DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail;

        #endregion
    }
}