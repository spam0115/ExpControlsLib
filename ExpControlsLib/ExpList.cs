using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static System.Windows.Forms.ListView;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;
using MethodInvoker = System.Windows.Forms.MethodInvoker;


namespace ExpControlsLib
{
    /// <summary>
    ///     This Form is a fully working start point for any form which requires an ExplorerTree and
    ///     ListView with enough room left for application specific controls.
    ///     
    ///     Explanation about how file icons are handled:
    ///     It is handled by a weird mix of Windows and custom code.  We use the OS's Shell's 
    ///     SystemImageListManager - it caches icons and provides them on demand to the ListView.  
    ///     The Listview is linked to the SystemImageListManager by calling 
    ///     SystemImageListManager.SetListViewImageList(ExpFileList, ...).  However, just setting the image list
    ///     doesn't link the listview items to the image list - you still have to set the ImageIndex of each 
    ///     ListViewItem to the appropriate index in the SystemImageList.  This is done by setting  
    ///     ListViewItem.ImageIndex = SystemImageListManager.GetIconIndex(lvi.Tag) (Tag contains a reference to 
    ///     the CShellItem for each Shell item entity.).
    ///     
    ///     However, for thumbnail display modes, Windows Shell doesn't have native support for that. 
    ///     We implented the ThumbnailImageListManager to make up for that shortfall and fill in the SystemImageListManager.
    /// 
    ///     Item images a draw by passing SystemImageLists into the Windows Shell ListView control.
    ///     
    /// </summary>
    /// <remarks> 
    ///     <para>This template form illustrates the use of:
    ///     <list type="bullet">
    ///     <item><description>Use of the ExpTreeNodeSelected Event Handler.</description></item>
    ///     <item><description>Use of LVColSorter for column sorting. See MakeLviItem for a custom ListViewItem 
    ///     builder which is compatible with and useful for LVColSorter. 
    ///     See Also SortLVItems for how to perform a Refresh of the 
    ///     ListView in response to a Refresh command from the Context Menu.</description></item>
    ///     <item><description>Full Context Menus in the ListView.</description></item>
    ///     <item><description>ListViewItem editing (first SubItem only) if the ListViewItem.Tag is a CShellItem.</description></item>
    ///     <item><description>Handling of dynamic update Events from CShItemUpdate Events.</description></item>
    ///     <item><description>Proper handling of the Delete Key.</description></item>
    ///     <item><description>Shows how to handle a DoubleClick on a ListViewItem.</description></item>
    ///     </list>
    ///     </para>
    /// </remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public partial class ExpList
    {

        #region Private fields

        // InitialLoadLimit is the number of ExpFileList.Items whose IconIndex will be fetched on initial load
        // the balance will be fetched AFTER ExpFileList.EndUpdate
        private const int InitialLoadLimit = 128;

        // For ExpFileList label text selection
        private const int EM_SETSEL = 0xB1;
        private const int LVM_FIRST = 0x1000;
        private const uint LVM_GETEDITCONTROL = LVM_FIRST + 24;

        private ShellController? _shellController = null;
        private HashSet<string> _excludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ThumbnailImageListManager _thumbnailManager; // Manager for thumbnail display modes
        private VirtualListViewWrapper _listViewWrapper;

        // Avoid Globalization problem-- an empty timevalue
        private static readonly DateTime EmptyTimeValue = new DateTime(1, 1, 1, 0, 0, 0);

        private Stack<CShellItem> _backHistory = new();
        private Stack<CShellItem> _forwardHistory = new();
        private bool _isNavigatingHistory = false;

        private CDragWrapper DW;         // Wrapper for Drag ops originating in ExpFileList
        private ClvDropWrapper DropWrap; // Wrapper for Drop ops targeting ExpFileList
        private bool m_CreateNew = false; // Flag for NewMenu processing of "New" item

        // Reentrancy guard: prevents DoItemUpdate from modifying _listView.Items
        // while an enumeration is in progress (Invoke() pumps messages and can trigger
        // reentrant shell notifications on the same UI thread).
        private int _enumerationDepth = 0;
        private readonly Queue<(object sender, ShellItemUpdateEventArgs e)> _deferredUpdates = new();

        // Reentrancy guard for image list modifications. Prevents modifying the 
        // image list while the OS is in the middle of a draw cycle (e.g. RetrieveVirtualItem).
        private int _imageListMutationDepth = 0;
        private readonly Queue<(object sender, ThumbnailReadyEventArgs e)> _deferredThumbnailUpdates = new();

        public bool IsShuttingDown {
            get; 
            set {
                _listViewWrapper.IsShuttingDown = value;
                field = value;
            }
        }


        private CancellationTokenSource? _displayFilesCts;
        private static readonly StaThreadRunner _staRunner = new StaThreadRunner(5, "ExpListStaRunner");

        private void Cleanup()
        {
            _displayFilesCts?.Cancel();
            _displayFilesCts?.Dispose();
            _displayFilesCts = null;
        }

        #endregion

        #region Public fields
        /// <summary>
        /// Delegate for the <see cref="ExpListItemClick"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the clicked item.</param>
        /// <param name="Item">The <see cref="CShellItem"/> that was clicked.</param>
        public delegate void ExpListItemClickEventHandler(ListViewItem lvItem, CShellItem Item);
        /// <summary>
        /// Occurs when an item in the list view is clicked.
        /// </summary>
        [Category("Action")]
        [Description("Fires when an item is clicked")]
        public event ExpListItemClickEventHandler ExpListItemClick;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemDoubleClick"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the double-clicked item.</param>
        /// <param name="Item">The <see cref="CShellItem"/> that was double-clicked.</param>
        public delegate void ExpListItemDoubleClickEventHandler(string SelPath, CShellItem Item);
        /// <summary>
        /// Occurs when an item in the list view is double-clicked.
        /// </summary>
        [Category("Action")]
        [Description("Fires when an item is double clicked")]
        public event ExpListItemDoubleClickEventHandler ExpListItemDoubleClick;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemMouseMBUp"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the item.</param>
        /// <param name="Item">The <see cref="CShellItem"/>.</param>
        public delegate void ExpListItemMouseMBUpEventHandler(string SelPath, CShellItem Item);
        [Category("Action")]
        [Description("Fires when a mouse button is released over an item")]
        /// <summary>
        /// Occurs when the middle mouse button is released over an item.
        /// </summary>
        public event ExpListItemMouseMBUpEventHandler ExpListItemMouseMBUp;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemArrowKeyUp"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the item.</param>
        /// <param name="Item">The <see cref="CShellItem"/>.</param>
        public delegate void ExpListItemArrowKeyUpEventHandler(string SelPath, CShellItem Item);
        /// <summary>
        /// Occurs when an arrow key is released, changing the selection.
        /// </summary>
        [Category("Action")]
        [Description("Fires on arrow key is released over an item")]
        public event ExpListItemArrowKeyUpEventHandler ExpListItemArrowKeyUp;

        /// <summary>
        /// Delegate for the <see cref="ExpListCurrentFolderChanged"/> event.
        /// </summary>
        /// <param name="Item">The <see cref="CShellItem"/> of the folder that was just loaded.</param>
        public delegate void ExpListCurrentFolderChangedEventHandler(CShellItem newCsi, CShellItem oldCsi);
        /// <summary>
        /// Occurs after the currently loaded folder has changed.
        /// </summary>
        [Category("Action")]
        [Description("Fires after the currently loaded folder has changed")]
        public event ExpListCurrentFolderChangedEventHandler ExpListCurrentFolderChanged;

        ///// <summary>
        ///// Delegate for the <see cref="ExpListPathChanged"/> event.
        ///// </summary>
        ///// <param name="Path">The new path of the ExpList.</param>
        //public delegate void ExpListPathChangedEventHandler(string Path);
        ///// <summary>
        ///// Occurs when the <see cref="CurrentPath"/> has changed.
        ///// </summary>
        //[Category("Action")]
        //[Description("Fires when the CurrentPath property has changed")]
        //public event ExpListPathChangedEventHandler ExpListPathChanged;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemsChanged"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the item.</param>
        /// <param name="Item">The <see cref="CShellItem"/>.</param>
        public delegate void ExpListItemsChangedEventHandler(string SelPath, CShellItem Item);
        /// <summary>
        /// Occurs when the items in the list view have changed (e.g., created or deleted).
        /// </summary>
        [Category("Action")]
        [Description("Fires when the items in the list view have changed (e.g., created or deleted)")]
        public event ExpListItemsChangedEventHandler ExpListItemsChanged;

        /// <summary>
        /// Delegate for the <see cref="ExpListMove"/> event.
        /// </summary>
        public delegate void ExpListMoveEventHandler(object sender, ExpListMoveEventArgs e);
        /// <summary>
        /// Occurs when Move is selected from the context menu.
        /// </summary>
        [Category("Action")]
        [Description("Fires when Move is selected from the context menu")]
        public event ExpListMoveEventHandler ExpListMove;

        /// <summary>
        /// Delegate for the <see cref="ExpListCopy"/> event.
        /// </summary>
        public delegate void ExpListCopyEventHandler(object sender, ExpListCopyEventArgs e);
        /// <summary>
        /// Occurs when Copy to Folder is selected from the context menu.
        /// </summary>
        [Category("Action")]
        [Description("Fires when Copy to Folder is selected from the context menu")]
        public event ExpListCopyEventHandler ExpListCopy;

        /// <summary>
        /// Delegate for the <see cref="ExpListSelectedIndexChangedEventHandler"/> event.
        /// </summary>
        /// <param name="items">The collection of selected list view items.</param>
        public delegate void ExpListSelectedIndexChangedEventHandler(SelectedListViewItemCollection items);
        /// <summary>
        /// Occurs when the selection in the list view is requested.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the selection collection changes, not when the selected index changes")]
        public event ExpListSelectedIndexChangedEventHandler SelectedIndexChanged;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemSelectionChangedEventHandler"/> event.
        /// </summary>
        /// <param name="e">The ListViewItem item, ItemIndex, and IsSelected</param>
        public delegate void ExpListItemSelectionChangedEventHandler(ListViewItemSelectionChangedEventArgs e);
        /// <summary>
        /// Occurs when the selection in the list view is requested.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the selected item changes")]
        public event ExpListItemSelectionChangedEventHandler ItemSelectionChanged;

        /// <summary>
        /// Delegate for the <see cref="ExpListItemGetSelItems"/> event.
        /// </summary>
        /// <param name="listViewItemCollection">The collection of selected list view items.</param>
        public delegate void ExpListItemGetSelItemsEventHandler(ListView.SelectedListViewItemCollection listViewItemCollection);
        /// <summary>
        /// Occurs when the selection in the list view is requested.
        /// </summary>
        public event ExpListItemGetSelItemsEventHandler ExpListItemGetSelItems;

        /// <summary>
        /// Delegate for the <see cref="ExpListGetColumnData"/> event.
        /// </summary>
        public delegate void ExpListGetColumnDataEventHandler(object sender, ExpListGetColumnDataEventArgs e);
        /// <summary>
        /// Occurs when data for a custom column is requested.
        /// </summary>
        [Category("Action"), Description("Occurs when data for a custom column is requested.")]
        public event ExpListGetColumnDataEventHandler ExpListGetColumnData;

        /// <summary>
        /// Delegate for the <see cref="DisplayModeChanged"/> event.
        /// </summary>
        /// <param name="newMode">The new <see cref="ListViewDisplayMode"/>.</param>
        public delegate void DisplayModeChangedEventHandler(ListViewDisplayMode newMode);
        /// <summary>
        /// Occurs when the <see cref="DisplayMode"/> has changed.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the DisplayMode property has changed")]
        public event DisplayModeChangedEventHandler DisplayModeChanged;

        /// <summary>
        /// Occurs when the sort column or order has changed.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the sort column or order has changed")]
        public event EventHandler SortOrderChanged;


        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the display mode used to present items in the list view.
        /// The native ListView dates from Windows 95 and doesn't support thumbnails.  Support for thumbnails 
        /// was a kludge introduced in XP.
        /// </summary>
        /// <remarks>Use this property to select among multiple visual representations for items,
        /// including standard views and thumbnail modes. Changing the display mode updates the appearance of the list
        /// view accordingly.</remarks>
        [Browsable(true), Category("Appearance"),
         Description("Selects one of 8 different views that items can be shown in."),
         DefaultValue(View.Details)]
        public ListViewDisplayMode DisplayMode
        {
            get => _listViewWrapper.DisplayMode;
            set
            {
                _listViewWrapper.DisplayMode = value;

                SetImageListForMode(value);
                if (_listViewWrapper.VirtualMode) LoadImagesForVisibleItems();

                DisplayModeChanged?.Invoke(value);
            }
        }


        /// <summary>
        /// Gets the collection of all column headers that appear in the list view.
        /// </summary>
        [Category("Appearance")]
        [Description("The columns displayed in the list view.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ListView.ColumnHeaderCollection Columns => _listView.Columns;

        /// <summary>
        /// Gets the collection of all items that appear in the list view.
        /// </summary>
        [Category("Appearance")]
        [Description("The items displayed in the list view.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ListView.ListViewItemCollection Items => _listView.Items;

        /// <summary>
        /// Gets or sets a value indicating whether multiple items can be selected.
        /// </summary>
        [Category("Behavior")]
        [Description("Allow multiple items to be selected.")]
        [DefaultValue(false)]
        public bool MultiSelect
        {
            get => _listView.MultiSelect;
            set => _listView.MultiSelect = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether clicking an item selects all its subitems.
        /// </summary>
        [Category("Appearance")]
        [Description("Select the entire row when an item is clicked.")]
        [DefaultValue(false)]
        public bool FullRowSelect
        {
            get => _listView.FullRowSelect;
            set => _listView.FullRowSelect = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether grid lines appear between the rows and columns.
        /// </summary>
        [Category("Appearance")]
        [Description("Displays grid lines between rows and columns.")]
        [DefaultValue(false)]
        public bool GridLines
        {
            get => _listView.GridLines;
            set => _listView.GridLines = value;
        }

        /// <summary>
        /// Gets or sets the column header style.
        /// </summary>
        [Category("Appearance")]
        [Description("The style of the column headers.")]
        [DefaultValue(ColumnHeaderStyle.Nonclickable)]
        public ColumnHeaderStyle HeaderStyle
        {
            get => _listView.HeaderStyle;
            set => _listView.HeaderStyle = value;
        }

        /// <summary>
        /// Gets or sets whether to show a minimal context menu by filtering out most 3rd party extensions.
        /// </summary>
        [Category("Behavior")]
        [Description("If true, filters out most 3rd party shell extensions from the context menu.")]
        [DefaultValue(false)]
        public bool MinimalContextMenu { get; set; } = false;

        /// <summary>
        /// Gets or sets the column to sort on.
        /// </summary>
        [Browsable(false)]
        public int SortColumn
        {
            get => _listViewWrapper.SortColumn;
            set => _listViewWrapper.SortColumn = value;
        }

        /// <summary>
        /// Gets the current sort order.
        /// </summary>
        [Browsable(false)]
        public SortOrder SortOrder => _listViewWrapper.SortOrder;

        /// <summary>
        /// Gets or sets a collection of items (by their full path or GUID) to exclude from the list display.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public HashSet<string> ExcludedItems
        {
            get => _excludedItems;
            set => _excludedItems = value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the list view is in virtual mode.
        /// </summary>
        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _listViewWrapper.VirtualMode;
            set
            {
                if (_listViewWrapper.VirtualMode == value) return;
                _listViewWrapper.VirtualMode = value;
            }
        }

        /// <summary>
        /// Gets or sets the current file system path displayed in the list view.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current path of ExpFileList"),
         DefaultValue(null)]
        public string? CurrentPath
        {
            get => CurrentFolderCsi?.FullPath;
        }

        /// <summary>
        /// Gets the <see cref="CShellItem"/> representing the currently selected folder in the tree or the folder being viewed.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current CSI of ExpFileList"),
         DefaultValue("")]
        public CShellItem? SelectedItem;

        private CShellItem? _currentFolderCsi; //todo: get rid of this and just use _listViewWrapper.currentFolderCsi everywhere instead.

        /// <summary>
        /// Gets the <see cref="CShellItem"/> representing the currently loaded/displayed folder.
        /// </summary>
        public CShellItem? CurrentFolderCsi 
        {
            get { return _currentFolderCsi; }
            set 
            {
                bool isDifferent = true;
                if (_currentFolderCsi == null && value == null)
                    isDifferent = false;
                else if ((_currentFolderCsi == null && value != null) || (_currentFolderCsi != null && value == null))
                    isDifferent = true;
                else if (_currentFolderCsi != null && value != null && string.Equals(_currentFolderCsi.FullPath, value.FullPath, StringComparison.OrdinalIgnoreCase))
                    isDifferent = false;

                if (!_isNavigatingHistory && isDifferent && value != null)
                {
                    if ( _currentFolderCsi != null)
                    {
                        _backHistory.Push(_currentFolderCsi);
                        _forwardHistory.Clear();
                    }
                }

                var oldCsi = _currentFolderCsi;
                if (value != null)
                    _currentFolderCsi = _shellController.HierachyManager.Add(value);
                else
                    _currentFolderCsi = value;
                ExpListCurrentFolderChanged?.Invoke(_currentFolderCsi, oldCsi);
            }
        }

        /// <summary>
        /// Gets or sets the vertical scroll position of the list view.
        /// </summary>
        [Browsable(false)]
        public int VerticalScrollPosition
        {
            get
            {
                if (!_listView.IsHandleCreated) return 0;
                return GetScrollPos(_listView.Handle, SB_VERT);
            }
            set
            {
                if (!_listView.IsHandleCreated) return;
                int current = GetScrollPos(_listView.Handle, SB_VERT);
                SendMessage(_listView.Handle, (uint)LVM_SCROLL, 0, value - current);
            }
        }


        /// <summary>
        /// Gets the number of items in the list view.
        /// </summary>
        [Browsable(false)]
        public int Count => _listViewWrapper.Count;

        /// <summary>
        /// Gets the number of selected items in the list view.
        /// </summary>
        [Browsable(false)]
        public int SelectedCount => _listViewWrapper.SelectedCount;

        /// <summary>
        /// Gets the indices of the selected items.
        /// </summary>
        [Browsable(false)]
        public ListView.SelectedIndexCollection SelectedIndices => _listViewWrapper.SelectedIndices;

        /// <summary>
        /// Gets an enumerable collection of selected CShellItems.
        /// ListView.SelectedItems can't be used in virtual mode.
        /// </summary>
        [Browsable(false)]
        public IEnumerable<CShellItem> SelectedCShellItems => _listViewWrapper.SelectedCShellItems;

        /// <summary>
        /// Gets the CShellItem at the specified index.
        /// </summary>
        public CShellItem? GetItem(int index) => _listViewWrapper.GetItem(index);

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public void RemoveAt(int index) => _listViewWrapper.RemoveAt(index);

        /// <summary>
        /// Sets the sort column and order without triggering an actual sort.
        /// This is useful to set at startup before the first location is loaded.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSortState(int column, SortOrder order)
        {
            _listViewWrapper.SetSortState(column, order);
        }

        /// <summary>
        /// Sets the sort column and order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void Sort(int column, SortOrder order)
        {
            Debug.WriteLine("ExpList: SetSort Begin");
            try
            {
                _listViewWrapper.Sort(column, order);
            }
            finally
            {
                Debug.WriteLine("ExpList: SetSort End");
            }
        }


        #endregion


        #region Constructor & Initialization

        /// <summary>
        /// Initializes a new instance of <see cref="ExpList"/>, wires up all event handlers
        /// for the control and its child <see cref="_listView"/> ListView.
        /// </summary>
        public ExpList()
        {
            Debug.WriteLine("ExpList: ExpList Begin");
            try
            {
                InitializeComponent();

                // Initialize thumbnail timer for lazy loading
                _scrollDebounceTimer = new System.Windows.Forms.Timer();
                _scrollDebounceTimer.Interval = 100;
                _scrollDebounceTimer.Tick += (s, e) =>
                {
                    _scrollDebounceTimer.Stop();
                    _thumbnailManager.CancelPendingRequests();
                    LoadImagesForVisibleItems();
                };

                VisibleChanged += ExpList_VisibleChanged;

                _listView.HandleCreated += ExpFileList_HandleCreated;
                _listView.Resize += (s, e) => OnScroll();
                _listView.Click += ExpFileList_Click;
                _listView.DoubleClick += ExpFileList_DoubleClick;
                _listView.BeforeLabelEdit += ExpFileList_BeforeLabelEdit;
                _listView.AfterLabelEdit += ExpFileList_AfterLabelEdit;
                _listView.MouseLeave += ExpFileList_MouseLeave;
                _listView.MouseEnter += ExpFileList_MouseEnter;
                _listView.MouseDown += ExpFileList_MouseDown;
                _listView.MouseUp += ExpFileList_MouseUp;
                _listView.MouseMove += ExpFileList_MouseMove;
                _listView.KeyUp += ExpFileList_KeyUp;
                _listView.KeyDown += ExpFileList_KeyDown;
                _listView.KeyPress += ExpFileList_KeyPress;
                _listView.SelectedIndexChanged += ExpFileList_SelectedIndexChanged;
                _listView.ItemSelectionChanged += ExpFileList_ItemSelectionChanged;

                _listViewWrapper = new VirtualListViewWrapper(this, _listView);
                _listViewWrapper.CreateListviewItemCallback = CreateListviewItemCallback;
                _listViewWrapper.UpdateListviewItemCallback = UpdateListviewItemCallback;
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpList End");
            }
        }

        public void Initialize(ShellController shellController)
        {
            Debug.WriteLine("ExpList: Initialize Begin");
            try
            {
                _shellController = shellController;
            }
            finally
            {
                Debug.WriteLine("ExpList: Initialize End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.Load"/> event of the <see cref="ExpList"/> control.
        /// Initializes drag and drop wrappers, thumbnail manager, and shell item update notifications.
        /// </summary>
        private void ExpList_Load(object sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpList_Load Begin");
            try
            {
                // Setup Drag and Drop Wrappers
                DW = new CDragWrapper(_listView);
                DropWrap = new ClvDropWrapper(_listView);

                // Initialize Thumbnail Manager
                _thumbnailManager = new ThumbnailImageListManager(this);
                _thumbnailManager.ThumbnailReady += ThumbnailManager_ThumbnailReady;

                //set up sorter
                _listViewWrapper.Initialize();
                _listViewWrapper.Sorter.SortOrderChanged += (s, e) =>
                {
                    if (VirtualMode)
                    {
                        _listViewWrapper.Sort(_listViewWrapper.Sorter.SortColumn, _listViewWrapper.Sorter.OrderOfSort);
                    }
                    SortOrderChanged?.Invoke(this, EventArgs.Empty); //what does this do?
                    OnScroll();
                };

                // Setup Change Notification
                CShellItemUpdater.UpdateEvent += UpdateInvoke;

                //DisplayMode = (ListViewDisplayMode)_listView.View;

                //SetImageListForMode(DisplayMode);
                //LoadImagesForItems();
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpList_Load End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.HandleCreated"/> event of the <see cref="_listView"/> ListView.
        /// </summary>
        private void ExpFileList_HandleCreated(object sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_HandleCreated Begin");
            try
            {
                _scrollHook = new ListViewScrollHook(_listViewWrapper, OnScroll);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_HandleCreated End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.VisibleChanged"/> event of the <see cref="ExpList"/> control.
        /// Re-configures image lists for the current display mode when the control becomes visible.
        /// </summary>
        private void ExpList_VisibleChanged(object sender, EventArgs e) //occurs when the control become visible
        {
            Debug.WriteLine("ExpList: ExpList_VisibleChanged Begin");
            try
            {

            }
            finally
            {
                Debug.WriteLine("ExpList: ExpList_VisibleChanged End");
            }
        }

        /// <summary>
        /// Overrides <see cref="Control.WndProc(ref Message)"/> to handle shell context menu messages.
        /// </summary>
        /// <param name="m">The Windows <see cref="Message"/> to process.</param>
        protected override void WndProc(ref Message m)
        {
            //Debug.WriteLine("ExpList: WndProc Begin");

            try
            {
                if (m.Msg == WindowsMessages.WM_QUERYENDSESSION || m.Msg == WindowsMessages.WM_ENDSESSION || m.Msg == WindowsMessages.WM_CLOSE || m.Msg == WindowsMessages.WM_DESTROY) // || m.Msg == WindowsMessages.WM_NCDESTORY WM_NCDESTORY get's called during creation as well as destruction so it's not really usable.
                {
                    IsShuttingDown = true;
                }

                if (IsShuttingDown)
                {
                    base.WndProc(ref m); //must call before exit or you will get form creation errors.
                    return;
                }

                int hr;
                if (m.Msg == (int)WM.INITMENUPOPUP || m.Msg == (int)WM.MEASUREITEM || m.Msg == (int)WM.DRAWITEM)
                {
                    if (m_WindowsContextMenu.cntxMenuExtended != null)
                    {
                        hr = m_WindowsContextMenu.cntxMenuExtended.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                        if (hr == 0) return;
                    }
                    else if ((m.Msg == (int)WM.INITMENUPOPUP && m.WParam == m_WindowsContextMenu.newMenuPtr)
                             || m.Msg == (int)WM.MEASUREITEM || m.Msg == (int)WM.DRAWITEM)
                    {
                        if (m_WindowsContextMenu.newMenuExtended != null)
                        {
                            hr = m_WindowsContextMenu.newMenuExtended.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                            if (hr == 0) return;
                        }
                    }
                }
                else if (m.Msg == (int)WM.MENUCHAR)
                {
                    if (m_WindowsContextMenu.cntxMenuCascading != null)
                    {
                        IntPtr plResult = Marshal.AllocHGlobal(IntPtr.Size);
                        try
                        {
                            hr = m_WindowsContextMenu.cntxMenuCascading.HandleMenuMsg2(m.Msg, m.WParam, m.LParam, plResult);
                            if (hr == 0)
                            {
                                m.Result = Marshal.ReadIntPtr(plResult);
                                return;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(plResult);
                        }
                    }
                }

                base.WndProc(ref m);
            }
            finally
            {
                //Debug.WriteLine("ExpList: WndProc End");
            }
        }


        #endregion


        #region Dynamic Update Handler


        /// <summary>
        /// Invokes a standard shell action (cut, copy, paste, delete) on the selected items.
        /// </summary>
        /// <param name="cmd">The shell verb to invoke (e.g., "cut", "copy", "paste", "delete").</param>
        private async void WinMenu(string cmd)
        {
            Debug.WriteLine("ExpList: WinMenu Begin");
            try
            {
                // Validate preconditions
                if (_currentFolderCsi == null || !_currentFolderCsi.IsFolder)
                {
                    return;
                }

                IShellFolder? folder = null;
                List<IntPtr>? pidls = null;
                CShellItem[] selectedItems = Array.Empty<CShellItem>();

                if (cmd == "paste")
                {
                    // Get the target folder for paste operation
                    try
                    {
                        folder = _currentFolderCsi == ShellController.DesktopCSI
                            ? _currentFolderCsi.IShlFolder
                            : _currentFolderCsi.Parent?.IShlFolder;

                        if (folder == null)
                        {
                            Debug.WriteLine("Failed to get folder interface for paste operation");
                            MessageBox.Show("Cannot paste: folder interface is unavailable.", "Paste Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        IntPtr relPidl = CPidl.ILFindLastID(_currentFolderCsi.PIDL);
                        if (relPidl == IntPtr.Zero)
                        {
                            Debug.WriteLine("Failed to get relative PIDL for current folder");
                            return;
                        }

                        pidls = new List<IntPtr> { relPidl };
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing paste operation: {ex.Message}");
                        MessageBox.Show($"Error preparing paste: {ex.Message}", "Paste Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else // Handle cut, copy, delete operations
                {
                    if (SelectedCount <= 0) return;

                    try
                    {
                        folder = _currentFolderCsi.IShlFolder;
                        if (folder == null)
                        {
                            Debug.WriteLine("Failed to get folder interface for selected items");
                            MessageBox.Show("Cannot perform operation: folder interface is unavailable.", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        if (VirtualMode)
                        {
                            selectedItems = SelectedCShellItems.ToArray(); //materialize selection to array for consistent processing
                        }
                        else
                        {
                            selectedItems = _listView?.SelectedItems?.Cast<ListViewItem>()?.Select(item => item.Tag as CShellItem)?.ToArray() ?? new CShellItem[0];
                        }

                        pidls = new List<IntPtr>(selectedItems.Length);

                        for (int i = 0; i < selectedItems.Length; i++)
                        {
                            var sel = selectedItems[i];
                            if (sel == null)
                            {
                                Debug.WriteLine($"Selected item {i} is null");
                                continue;
                            }

                            // For delete operations, validate that item can be deleted
                            if (cmd == "delete" && !sel.CanDelete)
                            {
                                MessageBox.Show($"Cannot delete: {sel.DisplayName}", "Cannot Delete",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                continue;
                            }

                            IntPtr pidl = sel.LastPIDL;
                            if (pidl == IntPtr.Zero)
                            {
                                Debug.WriteLine($"Failed to get PIDL for item: {sel.DisplayName}");
                                MessageBox.Show($"Failed to get ID for item: {sel.DisplayName}", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                continue;
                            }

                            pidls.Add(CPidl.Copy(pidl));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing {cmd} operation: {ex.Message}");
                        MessageBox.Show($"Error preparing operation: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                // Validate items to process
                if (pidls == null || pidls.Count == 0)
                {
                    Debug.WriteLine("No items to process");
                    return;
                }

                int topItemIndex = -1;
                bool hasItems = VirtualMode ? _listView.VirtualListSize > 0 : _listView.Items.Count > 0;
                if (cmd == "delete" && hasItems)
                { //prevent null references from invalid selections that are about to be deleted
                    topItemIndex = _listViewWrapper.GetTopIndex();
                    _listView.SelectedIndices.Clear();
                    if (!VirtualMode)
                    {
                        _listView.SelectedItems.Clear();
                    }
                }

                // Capture for background thread
                var capturedParentPidl = _currentFolderCsi.PIDL;
                var capturedRelPidls = pidls;

                // Offload shell interaction to background STA thread. 
                // Binding MUST happen on this thread to avoid marshaling back to UI thread.
                var task = _staRunner.EnqueueWork(InvokeMenuCommand(cmd, capturedParentPidl, capturedRelPidls));

                if (cmd == "delete" && hasItems)
                {
                    _listView.BeginUpdate();
                    try
                    {
                        // Batch remove from hierarchy (suppress individual events)
                        _shellController.HierachyManager.RemoveRange(selectedItems, raiseEvents: false);

                        // Batch remove from list view wrapper
                        _listViewWrapper.RemoveItems(selectedItems);

                        if (selectedItems.Length > this._listViewWrapper.GetApproxVisibleCount())
                            OnScroll();

                        // Fire single update event for the folder
                        if (_currentFolderCsi != null)
                        {
                            string path = _currentFolderCsi.FullPath.StartsWith(":")
                                ? _currentFolderCsi.DisplayName
                                : _currentFolderCsi.FullPath;
                            ExpListItemsChanged?.Invoke(path, _currentFolderCsi);
                        }
                    }
                    finally
                    {
                        _listView.EndUpdate();
                    }
                }

                if (topItemIndex >= 0)
                {
                    int count = VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                    if (topItemIndex < count)
                    {
                        if (VirtualMode)
                            _listView.EnsureVisible(topItemIndex);
                        else
                            _listView.Items[topItemIndex].EnsureVisible();
                    }
                }

                //var invokeHR = await task;
                //if (invokeHR != S_OK && invokeHR != -1)
                //{
                //    Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{invokeHR:X8}, cmd='{cmd}'");
                //}
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in WinMenu: {ex.Message}");
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Debug.WriteLine("ExpList: WinMenu End");
            }
        }

        /// <summary>
        /// This invokes the specified shell command on a background STA thread with it's own window handle.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="capturedParentPidl"></param>
        /// <param name="capturedRelPidls"></param>
        /// <returns></returns>
        private static Func<CancellationToken, int> InvokeMenuCommand(string cmd, nint capturedParentPidl, List<nint> capturedRelPidls)
        {
            return _ =>
            {
                IShellFolder desktop = null;
                IShellFolder parentFolder = null;
                IntPtr iUnknownOut = IntPtr.Zero;
                IContextMenu? contextMenu = null;
                IntPtr lpVerbAnsi = IntPtr.Zero;
                IntPtr lpVerbUni = IntPtr.Zero;

                // Create a hidden dummy window on this thread to act as the owner.
                // This ensures the dialog is modal to THIS thread's window and NOT the app's main window.
                using (Control dummy = new Control())
                {
                    IntPtr dummyHandle = dummy.Handle; // Force handle creation

                    try
                    {
                        // 1. Get Desktop Folder on THIS thread
                        int hr = SHGetDesktopFolder(ref desktop);
                        if (hr != S_OK || desktop == null)
                        {
                            Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{hr:X8}, cmd='{cmd}'");
                            return hr;
                        }

                        // 2. Bind to Parent Folder on THIS thread
                        if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                        {
                            parentFolder = desktop;
                        }
                        else
                        {
                            IntPtr folderPtr = IntPtr.Zero;
                            hr = desktop.BindToObject(capturedParentPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref folderPtr);
                            if (hr != S_OK || folderPtr == IntPtr.Zero)
                            {
                                Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{hr:X8}, cmd='{cmd}'");
                                return hr;
                            }
                            parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(folderPtr, typeof(IShellFolder));
                            Marshal.Release(folderPtr);
                        }

                        // 3. Get IContextMenu on THIS thread
                        IntPtr rgfReserved = IntPtr.Zero;
                        hr = parentFolder.GetUIObjectOf(IntPtr.Zero, (uint)capturedRelPidls.Count, capturedRelPidls.ToArray(),
                            IID_IContextMenu, rgfReserved, out iUnknownOut);

                        if (hr != S_OK || iUnknownOut == IntPtr.Zero)
                        {
                            Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{hr:X8}, cmd='{cmd}'");
                            return hr;
                        }

                        contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(iUnknownOut, typeof(IContextMenu));

                        // 4. Invoke Command
                        lpVerbAnsi = Marshal.StringToHGlobalAnsi(cmd);
                        lpVerbUni = Marshal.StringToHGlobalUni(cmd);

                        var cmi = new CMInvokeCommandInfoEx
                        {
                            cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                            hwnd = dummyHandle, // Dialog is modal to this hidden window on the background thread
                            nShow = (int)SW.SHOWNORMAL,
                            fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                            ptInvoke = new Point(0, 0),
                            lpVerb = lpVerbAnsi,
                            lpVerbW = lpVerbUni
                        };

                        hr = contextMenu.InvokeCommand(cmi);
                        if (hr != S_OK && hr != -1)
                        {
                            Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{hr:X8}, cmd='{cmd}'");
                        }
                        return hr;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in background shell operation: {ex.Message}");
                        return -1;
                    }
                    finally
                    {
                        if (lpVerbAnsi != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbAnsi);
                        if (lpVerbUni != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbUni);
                        if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                        if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                        if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                        if (desktop != null) Marshal.ReleaseComObject(desktop);

                        foreach (var pidl in capturedRelPidls) Marshal.FreeCoTaskMem(pidl);
                    }
                }
            };
        }


        /// <summary>
        /// Creates a native Windows context menu for the current folder.
        /// </summary>
        /// <param name="comContextMenu">Output parameter for the main context menu handle.</param>
        /// <param name="viewSubMenu">Output parameter for the View submenu handle.</param>
        private void CreateContextMenu(out IntPtr comContextMenu, out IntPtr viewSubMenu, out IntPtr sortSubMenu)
        {
            Debug.WriteLine("ExpList: CreateContextMenu Begin");
            try
            {
                comContextMenu = CreatePopupMenu();
                viewSubMenu = CreatePopupMenu();
                sortSubMenu = CreatePopupMenu();

                // Create and insert the "View" submenu item into the main context menu.
                var itemInfo = new MENUITEMINFO("View")
                {
                    fMask = (int)(MIIM.SUBMENU | MIIM.STRING),
                    hSubMenu = viewSubMenu
                };
                InsertMenuItem(comContextMenu, 0, true, ref itemInfo);

                // Create and insert the "Sort by" submenu item into the main context menu.
                var sortInfo = new MENUITEMINFO("Sort by")
                {
                    fMask = (int)(MIIM.SUBMENU | MIIM.STRING),
                    hSubMenu = sortSubMenu
                };
                InsertMenuItem(comContextMenu, 1, true, ref sortInfo);

                // Add view mode options to the View submenu with radio button indicators.
                uint checkedFlag;
                uint checkedValue = (uint)(MFT.RADIOCHECK | MFT.CHECKED);

                checkedFlag = (DisplayMode == ListViewDisplayMode.Details) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.DETAILS, "Details");

                checkedFlag = (DisplayMode == ListViewDisplayMode.Thumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (uint)CMD.THUMBNAILS, "Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.LargeThumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (uint)CMD.LARGE_THUMBNAILS, "Large Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.EXTRA_LARGE_THUMBNAILS, "Extra Large Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.LargeIcon) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.LARGEICON, "Large Icons");

                checkedFlag = (DisplayMode == ListViewDisplayMode.List) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.LIST, "List");

                checkedFlag = (DisplayMode == ListViewDisplayMode.Tile) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.TILES, "Tiles");

                // Add sorting options to the Sort by submenu.
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                {
                    int currentSortCol = sorter.SortColumn;
                    for (int i = 0; i < _listView.Columns.Count; i++)
                    {
                        uint sortChecked = (i == currentSortCol) ? checkedValue : (uint)MFT.BYCOMMAND;
                        AppendMenu(sortSubMenu, sortChecked, (uint)((int)CMD.SORT_BY_BASE + i), _listView.Columns[i].Text);
                    }
                }

                // Add separator and standard folder operations to the main context menu.
                AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.REFRESH, "Refresh (F5)");
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.SELECT_ALL, "Select All (Ctrl+A)");
                AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);

                // Determine if Paste operations are allowed by checking clipboard contents.
                // CanDropClipboard() returns the DragDropEffects supported by the target folder.
                var enabled = (uint)MFT.GRAYED;
                DragDropEffects effects = DragDropEffects.None;

                if (_currentFolderCsi == null)
                {
                    enabled = (uint)MFT.BYCOMMAND;
                }
                else
                {
                    effects = CanDropClipboard(_currentFolderCsi);
                    if ((effects & DragDropEffects.Copy) == DragDropEffects.Copy ||
                        (effects & DragDropEffects.Move) == DragDropEffects.Move)
                    {
                        enabled = (uint)MFT.BYCOMMAND;
                    }
                }

                // Add Paste menu item, enabled only if clipboard contents are compatible.
                AppendMenu(comContextMenu, enabled, (int)CMD.PASTE, "Paste (Ctrl+V)");

                // Add additional paste and context operations if a folder is selected.
                if (_currentFolderCsi != null)
                {
                    enabled = (uint)MFT.GRAYED;
                    if ((effects & DragDropEffects.Link) == DragDropEffects.Link)
                        enabled = (int)MFT.BYCOMMAND;

                    AppendMenu(comContextMenu, enabled, (uint)CMD.PASTELINK, "Paste Link");
                    AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);

                    // Add New menu for writable folders (excluding special shell folders like ::).
                    // The "New" submenu is managed by m_WindowsContextMenu.SetUpNewMenu(),
                    // which adds file creation options for the selected folder.
                    if (_currentFolderCsi.IsFolder &&
                        ((!_currentFolderCsi.FullPath.StartsWith("::")) || _currentFolderCsi == ShellController.DesktopCSI))
                    {
                        int xIndex = GetMenuItemCount(comContextMenu.ToInt32());
                        m_WindowsContextMenu.SetUpNewMenu(_currentFolderCsi, comContextMenu, xIndex);
                        AppendMenu(comContextMenu, (int)MFT.SEPARATOR, 0, string.Empty);
                    }

                    AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.PROPERTIES, "Properties");
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: CreateContextMenu End");
            }
        }

        /// <summary>
        /// Displays a context menu for the ListView when no items are selected.
        /// This menu includes view options (Tiles, Large Icons, List, Details), 
        /// refresh, select all, paste operations, and new item creation.
        /// </summary>
        /// <param name="pt">The point (in screen coordinates) where the menu should be displayed.</param>
        /// <remarks>
        /// This function handles the creation and management of Windows popup menus.
        /// It directly manages native menu handles via Win32 API calls and must properly
        /// release all COM objects and menu handles to avoid memory leaks and access violations.
        /// 
        /// Key operations:
        /// 1. Creates two popup menus: a main context menu and a View submenu
        /// 2. Populates menus with commands and their checked states
        /// 3. Determines menu item availability based on clipboard contents
        /// 4. Invokes the selected command on shell objects (IShellFolder, IContextMenu)
        /// 5. Releases all COM interfaces and menu handles in the CLEANUP section
        /// 
        /// Memory safety note: Menu handles (comContextMenu, viewSubMenu) must be released
        /// via Marshal.Release() after TrackPopupMenuEx returns. COM objects (IContextMenu, 
        /// IShellFolder) must be released by ReleaseComObject() to prevent heap corruption.
        /// Mixing release mechanisms or skipping releases can cause access violations.
        /// </remarks>
        private async void ShowAndHandleContextMenu(Point pt)
        {
            Debug.WriteLine("ExpList: ShowAndHandleContextMenu Begin");
            try
            {
                int MIN = 1;
                var cmi = new CMInvokeCommandInfoEx();

                // Create three native Windows popup menu handles.
                IntPtr comContextMenu;
                IntPtr viewSubMenu;
                IntPtr sortSubMenu;

                CreateContextMenu(out comContextMenu, out viewSubMenu, out sortSubMenu);

                // Display the context menu and capture the user's selection.
                int cmdID = TrackPopupMenuEx(comContextMenu, (int)TPM.RETURNCMD, pt.X, pt.Y, Handle, IntPtr.Zero);

                // Process the user's menu selection.
                if (cmdID >= MIN)
                {
                    // Handle sorting commands.
                    if (cmdID >= (int)CMD.SORT_BY_BASE)
                    {
                        int colIndex = cmdID - (int)CMD.SORT_BY_BASE;
                        if (_listView.ListViewItemSorter is LVColSorter sorter)
                        {
                            sorter.SortColumn = colIndex;
                        }
                        goto CLEANUP;
                    }

                    // Initialize the CMInvokeCommandInfoEx structure used for shell command invocation.
                    cmi = new CMInvokeCommandInfoEx
                    {
                        cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                        hwnd = IntPtr.Zero,
                        nShow = (int)SW.SHOWNORMAL,
                        fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                        ptInvoke = pt
                    };

                    // Handle view mode changes and built-in operations.
                    var cmdEnum = (CMD)cmdID;
                    switch (cmdEnum)
                    {
                        case CMD.TILES:
                            DisplayMode = ListViewDisplayMode.Tile;
                            goto CLEANUP;
                        case CMD.LIST:
                            DisplayMode = ListViewDisplayMode.List;
                            goto CLEANUP;
                        case CMD.DETAILS:
                            DisplayMode = ListViewDisplayMode.Details;
                            goto CLEANUP;
                        case CMD.LARGEICON:
                            this.DisplayMode = ListViewDisplayMode.LargeIcon;
                            goto CLEANUP;
                        case CMD.THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.Thumbnail;
                            goto CLEANUP;
                        case CMD.LARGE_THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.LargeThumbnail;
                            goto CLEANUP;
                        case CMD.EXTRA_LARGE_THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.ExtraLargeThumbnail;
                            goto CLEANUP;
                        case CMD.REFRESH:
                            // Refresh the folder contents and re-sort the ListView items.
                            _shellController.ShellUpdater.DoUpdateDir(_currentFolderCsi);
                            _listViewWrapper.Sort();
                            goto CLEANUP;
                        case CMD.SELECT_ALL:
                            // Select all items in the ListView.
                            if (VirtualMode)
                            {
                                _listView.BeginUpdate(); //is this needed?
                                try
                                {
                                    for (int i = 0; i < _listView.VirtualListSize; i++)
                                        _listView.SelectedIndices.Add(i);
                                }
                                finally
                                {
                                    _listView.EndUpdate();
                                }
                            }
                            else
                            {
                                EnterListViewEnumeration();
                                try
                                { //should we use beginupdate here?
                                    foreach (ListViewItem item in _listView.Items)
                                    {
                                        if (item is null) continue;
                                        item.Selected = true;
                                    }
                                }
                                finally
                                {
                                    ExitListViewEnumeration();
                                }
                            }
                            goto CLEANUP;
                        case CMD.PASTE:
                        case CMD.PASTELINK:
                        case CMD.PROPERTIES:
                            if (_currentFolderCsi != null)
                            {
                                string verb = cmdEnum switch
                                {
                                    CMD.PASTE => "paste",
                                    CMD.PASTELINK => "pastelink",
                                    CMD.PROPERTIES => "properties",
                                    _ => ""
                                };
                                cmi.lpVerb = Marshal.StringToHGlobalAnsi(verb);
                                cmi.lpVerbW = Marshal.StringToHGlobalUni(verb);
                            }
                            else
                            {
                                goto CLEANUP;
                            }
                            break;
                        default:
                            // Handle commands from the "New" submenu.
                            cmdID -= 1;
                            cmi.lpVerb = (IntPtr)cmdID;
                            cmi.lpVerbW = (IntPtr)cmdID;
                            m_CreateNew = true;
                            
                            var newMenuBase = m_WindowsContextMenu.newMenuBase;
                            var cmi_new = cmi;
                            await _staRunner.EnqueueWork(_ =>
                            {
                                return newMenuBase.InvokeCommand(cmi_new);
                            });
                            goto CLEANUP;
                    }

                    if (_currentFolderCsi != null)
                    {
                        IntPtr parentPidl = _currentFolderCsi == ShellController.DesktopCSI
                            ? _currentFolderCsi.PIDL
                            : _currentFolderCsi.Parent.PIDL;

                        IntPtr relPidl = CPidl.ILFindLastID(_currentFolderCsi.PIDL);
                        var capturedRelPidl = relPidl != IntPtr.Zero ? CPidl.Copy(relPidl) : IntPtr.Zero;
                        var capturedParentPidl = parentPidl;
                        var cmi_shell = cmi;

                        await _staRunner.EnqueueWork(_ =>
                        {
                            IShellFolder desktop = null;
                            IShellFolder parentFolder = null;
                            IntPtr iUnknownOut = IntPtr.Zero;
                            IContextMenu? contextMenu = null;

                            // Create a hidden dummy window on this thread to act as the owner.
                            using (Control dummy = new Control())
                            {
                                IntPtr dummyHandle = dummy.Handle;
                                cmi_shell.hwnd = dummyHandle;

                                try
                                {
                                    SHGetDesktopFolder(ref desktop);
                                    if (desktop == null) return -1;

                                    if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                                        parentFolder = desktop;
                                    else
                                    {
                                        IntPtr folderPtr = IntPtr.Zero;
                                        if (desktop.BindToObject(capturedParentPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref folderPtr) != S_OK) return -1;
                                        parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(folderPtr, typeof(IShellFolder));
                                        Marshal.Release(folderPtr);
                                    }

                                    IntPtr rgfReserved = IntPtr.Zero;
                                    var relPidls = new IntPtr[] { capturedRelPidl };
                                    if (parentFolder.GetUIObjectOf(IntPtr.Zero, 1, relPidls, IID_IContextMenu, rgfReserved, out iUnknownOut) != S_OK) return -1;

                                    contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(iUnknownOut, typeof(IContextMenu));

                                    return contextMenu.InvokeCommand(cmi_shell);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error in background context menu shell operation: {ex.Message}");
                                    return -1;
                                }
                                finally
                                {
                                    if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                                    if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                                    if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                                    if (desktop != null) Marshal.ReleaseComObject(desktop);
                                    if (capturedRelPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(capturedRelPidl);
                                }
                            }
                        });

                        // Clean up allocated strings
                        if (cmi.lpVerb != IntPtr.Zero && cmi.lpVerb.ToInt64() > 0xFFFF) Marshal.FreeHGlobal(cmi.lpVerb);
                        if (cmi.lpVerbW != IntPtr.Zero && cmi.lpVerbW.ToInt64() > 0xFFFF) Marshal.FreeHGlobal(cmi.lpVerbW);
                    }
                }

            CLEANUP:
                m_WindowsContextMenu.ReleaseNewMenu();

                if (comContextMenu != IntPtr.Zero)
                {
                    DestroyMenu(comContextMenu);
                    comContextMenu = IntPtr.Zero;
                }

                // Note: viewSubMenu and sortSubMenu are destroyed when comContextMenu is destroyed.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in ShowAndHandleContextMenu: {ex.Message}");
            }
            finally
            {
                Debug.WriteLine("ExpList: objects End");
            }
        }

        private delegate void InvokeUpdate(object sender, ShellItemUpdateEventArgs e);

        /// <summary>
        /// Exposes the SelectedItems collection of the internal ListView to allow external handlers to access the currently selected items.
        /// </summary>
        public ListView.SelectedListViewItemCollection SelectedItems => _listView.SelectedItems;

        /// <summary>
        /// Marshals shell item update events to the UI thread.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void UpdateInvoke(object sender, ShellItemUpdateEventArgs e)
        {
            //Debug.WriteLine("ExpList: UpdateInvoke Begin");
            try
            {
                if (sender is null || IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                if (e is null)
                {
                    Console.WriteLine("Event arguments cannot be null in UpdateInvoke.");
                    return;
                }

                if (InvokeRequired)
                {
                    try
                    {
                        BeginInvoke((InvokeUpdate)DoItemUpdate, sender, e);
                    }
                    catch (InvalidOperationException) { } // Handle race condition where control is disposed just after check
                }
                else
                {
                    DoItemUpdate(sender, e);
                }
            }
            finally
            {
                //Debug.WriteLine("ExpList: UpdateInvoke End");
            }
        }

        private LruConcurrentDictionary<String, bool> _activeDeletes = new(1000);
        /// <summary>
        /// Performs the actual update of list view items in response to shell changes.
        /// Handles creation, deletion, renaming, and other updates of files and folders.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private async void DoItemUpdate(object sender, ShellItemUpdateEventArgs e)
        {
            try
            {
                if (sender is null || _currentFolderCsi == null || e?.Item is null) return;

                Debug.WriteLine($"ExpList: DoItemUpdate Begin - {e.UpdateType.ToString()}, {e.Item.Name}");

                // If an enumeration is in progress, defer this update to prevent reentrant
                // mutation of _listView.Items (which causes null items during foreach).
                if (_enumerationDepth > 0)
                {
                    _deferredUpdates.Enqueue((sender, e));
                    return;
                }

                var senderCsi = e.Item;

                // For Created/Deleted/UpdateDir, sender is the Folder containing the item.
                // For Updated/Renamed/IconChange, sender is the Item itself.
                bool isTargetFolder = CPidl.ResolvesToSamePathOrName(senderCsi.PIDL, _currentFolderCsi.PIDL);
                bool isTargetItem = senderCsi.Parent != null && CPidl.ResolvesToSamePathOrName(senderCsi.Parent.PIDL, _currentFolderCsi.PIDL);

                if (!isTargetFolder && !isTargetItem) return;

                try
                {
                    switch (e.UpdateType)
                    {
                        case CShItemUpdateType.Created:
                            {
                                if (!isTargetFolder) return;
                                if (IsExcluded(e.Item)) return;

                                _listViewWrapper.InsertSorted(e.Item);
                                m_CreateNew = false; //I don't think this is even used?

                                break;
                            }

                        case CShItemUpdateType.Deleted:
                            if (e.Item is null)
                            {
                                Debug.WriteLine("ExpList received DELETED event but no item was specified.");
                                return;
                            }

                            if (_activeDeletes.ContainsKey(e.Item.FullPath))
                            {
                                Debug.WriteLine("  [DELETE] Already processing delete for this item. Skipping to avoid duplicate work.");
                                return;
                            }

                            try
                            {
                                _activeDeletes.Add(e.Item.FullPath, true);
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    //bool wasSelected = _listViewWrapper.IsItemSelected(e.Item);
                                    _listViewWrapper.RemoveAt(index);

                                    //if (wasSelected && SelectedCount == 0 && Count > 0)
                                    //{
                                    //    int nextIndex = Math.Min(index, Count - 1);
                                    //    var nextLvi = _listViewWrapper.GetListViewItem(nextIndex);
                                    //    if (nextLvi != null)
                                    //    {
                                    //        nextLvi.Selected = true;
                                    //        nextLvi.Focused = true;
                                    //    }
                                    //}
                                }
                            }
                            finally
                            {
                                _activeDeletes.Remove(e.Item.FullPath);
                            }
                            break;

                        case CShItemUpdateType.Renamed: // This event can be raised in various rename scenarios - file rename, folder rename, drag-drop move with rename, etc.  The structure of the event (which properties are populated) can vary based on the scenario, so the handling needs to be robust to these variations.
                            {
                                var csi = e.Item;

                                if (e.Item.Parent.FullPath != _currentFolderCsi.FullPath) return;

                                int index = -1;
                                if (VirtualMode)
                                {
                                    index = _listViewWrapper.FindInsertionPoint(csi);
                                }
                                else
                                {
                                    var lvi = csi.LVItem;
                                    if (lvi is null) throw new Exception("ListViewItem not found for renamed item");
                                    index = lvi.Index;
                                }

                                if (index >= 0)
                                {
                                    _listViewWrapper.RemoveAt(index);
                                    if (!IsExcluded(csi))
                                    {
                                        _listViewWrapper.InsertSorted(csi);
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.Updated:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    _listViewWrapper.RedrawItem(index);
                                }

                                break;
                            }

                        case CShItemUpdateType.UpdateDir:
                            Debug.WriteLine("\tUpdateDir");
                            await LoadDirectory(_currentFolderCsi, true, reload: true);
                            break;

                        case CShItemUpdateType.IconChange:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    if (IsThumbnailViewMode())
                                        _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode(), index);
                                    else
                                        _listViewWrapper.RedrawItem(index);
                                }
                                break;
                            }

                        case CShItemUpdateType.MediaChange:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    if (IsThumbnailViewMode())
                                        _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode(), index);
                                    else
                                        _listViewWrapper.RedrawItem(index);
                                }
                                break;
                            }
                    }

                    // Fire ExpListItemsChanged for Created/Deleted events.
                    // This was previously in UpdateInvoke but must be here since
                    // BeginInvoke is now used (the marshaling path wouldn't fire it).
                    if (e.UpdateType == CShItemUpdateType.Created || e.UpdateType == CShItemUpdateType.Deleted)
                    {
                        if (_currentFolderCsi != null)
                        {
                            if (_currentFolderCsi.FullPath.StartsWith(":"))
                                ExpListItemsChanged?.Invoke(_currentFolderCsi.DisplayName, _currentFolderCsi);
                            else
                                ExpListItemsChanged?.Invoke(_currentFolderCsi.FullPath, _currentFolderCsi);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in frmTemplate -- ExpFileList updater -- " + ex);
                }
                finally
                {
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EXCEPTION: DoItemUpdate -- " + ex.ToString());
            }
            finally
            {
                //Debug.WriteLine("ExpList: DoItemUpdate End");
            }
        }


        /// <summary>
        /// Refreshes the display of a single item whose underlying filesystem data has changed.
        /// </summary>
        public void UpdateListviewItemCallback(ListViewItem lvi, CShellItem csi)
        {
            //Debug.WriteLine("ExpList: UpdateLviUsingCsi Begin");
            try
            {
                if (lvi == null || csi == null) return;

                // Update primary text
                lvi.Text = csi.DisplayName;
                lvi.Name = csi.FullPath;
                lvi.Tag = csi;
                csi.LVItem = lvi;

                PopulateColumnData(lvi, csi); //you need this even in non-details mode to facilitate sorting

                if (IsThumbnailViewMode())
                {
                    //int index = _thumbnailManager.GetThumbnailIndex(csi, GetThumbnailSizeForMode()); //do not do this because sometimes windows will request all items from the listview for no reason
                    lvi.ImageIndex = -1;
                }
                else
                    lvi.ImageIndex = SystemImageListManager.GetIconIndex(csi, _listViewWrapper.DisplayMode == ListViewDisplayMode.LargeIcon);
            }
            finally
            {
                //Debug.WriteLine("ExpList: UpdateLviUsingCsi End");
            }
        }

        private void PopulateColumnData(ListViewItem lvi, CShellItem item)
        {
            for (int i = 1; i < _listView.Columns.Count; i++)
            {
                ColumnHeader col = _listView.Columns[i];

                var data = GetColumnData(item, col);
                if (lvi.SubItems.Count <= i)
                {
                    var si = lvi.SubItems.Add(new ListViewItem.ListViewSubItem());
                    si.Text = data.Text;
                    si.Tag = data.Tag;
                }
                else
                {
                    lvi.SubItems[i].Text = data.Text;
                    lvi.SubItems[i].Tag = data.Tag;
                }
            }
        }

        /// <summary>
        /// Populates the text and tag for a single given column based on the provided shell item and column header.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="col"></param>
        /// <param name="text"></param>
        /// <param name="tag"></param>
        internal ListViewSubitemData GetColumnData(CShellItem item, ColumnHeader col)
        {
            return GetColumnData(item, col.Text, col.Index, col.Tag?.ToString().Trim() ?? string.Empty);
        }

        internal ListViewSubitemData GetColumnData(CShellItem item, string colText, int colIndex, string mapping)
        {
            //Debug.WriteLine("ExpList: GetColumnData Begin");
            try
            {
                string text = string.Empty;
                object? tag = null;

                // 1. Try Tag Mapping
                if (!string.IsNullOrEmpty(mapping) && mapping.StartsWith("."))
                {
                    string propName = mapping.Substring(1);
                    // Optimization: Check for common properties directly
                    switch (propName)
                    {
                        case "DisplayName":
                            text = item.DisplayName;
                            return new ListViewSubitemData(text, null);
                        case "TypeName":
                            text = item.TypeName;
                            return new ListViewSubitemData(text, null);
                        case "Size":
                            if (!item.IsDisk && item.IsFileSystem && !item.IsFolder)
                            {
                                text = item.Size;
                                tag = item.Length;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                        case "LastWriteTime":
                            if (!item.IsDisk && item.LastWriteTime != EmptyTimeValue)
                            {
                                text = item.LastWriteTime.ToString("MM/dd/yyyy HH:mm:ss");
                                tag = item.LastWriteTime;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                        case "CreationTime":
                            if (!item.IsDisk && item.CreationTime != EmptyTimeValue)
                            {
                                text = item.CreationTime.ToString("MM/dd/yyyy HH:mm:ss");
                                tag = item.CreationTime;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                    }


                    if (item.ColumnDic.TryGetValue(colText, out ListViewSubitemData propInfo)) //maybe it was already fetched before
                        return propInfo;

                    // Fallback to reflection for other properties
                    if (mapping.StartsWith(".Tag")) //get the value from one of the fields within the custom Tag object property
                    {
                        if (item.Tag != null)
                        {
                            string memberName = mapping.Substring(4);
                            if (string.IsNullOrEmpty(memberName)) return new ListViewSubitemData(string.Empty, null);

                            Type tagType = item.Tag.GetType();
                            // Try Field first
                            FieldInfo field = tagType.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (field != null)
                            {
                                object val = field.GetValue(item.Tag);
                                text = val?.ToString() ?? string.Empty;
                                tag = val;
                                goto END;
                            }
                            // Then try Property
                            PropertyInfo prop = tagType.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (prop != null)
                            {
                                object val = prop.GetValue(item.Tag);
                                text = val?.ToString() ?? string.Empty;
                                tag = val;
                                goto END;
                            }
                        }
                    }
                    else
                    {
                        PropertyInfo prop = item.GetType().GetProperty(propName);
                        if (prop != null)
                        {
                            object val = prop.GetValue(item);
                            text = val?.ToString() ?? string.Empty;
                            tag = val;
                            goto END;
                        }
                    }
                }

                if (colIndex == 0)
                {
                    text = item.DisplayName;
                }
                else
                {
                    // 2. Try bulk fetch if still not found
                    EnsureCustomColumnDataFetched(item, colText);
                    if (item.ColumnDic.TryGetValue(colText, out ListViewSubitemData propInfo))
                        return propInfo;
                }

            END:
                var result = new ListViewSubitemData(text, tag);

                item.ColumnDic.TryAdd(colText, result); //save for future use

                return result;
            }
            finally
            {
                //Debug.WriteLine("ExpList: GetColumnData End");
            }
        }

        /// <summary>
        /// Loads all special custom column data that isn't part of CShellItem's default properties by firing
        /// the ExpListGetColumnData event.  This allows external handlers to provide bulk data for all columns 
        /// in one shot, which is more efficient than firing GetColumnData for each individual column.
        /// </summary>
        /// <param name="item"></param>
        private void EnsureCustomColumnDataFetched(CShellItem item, string? requiredColumn = null)
        {
            if (ExpListGetColumnData == null) return;

            if (requiredColumn is null) { 
                if (item.ColumnDic.ContainsKey("DbId")) return;
            }
            else if (item.ColumnDic.ContainsKey(requiredColumn)) 
                return; // If the required column is already in the dictionary, we're good.

            // Otherwise, fire the event to fetch ALL custom columns at once.
            var args = new ExpListGetColumnDataEventArgs(item);
            ExpListGetColumnData(this, args);

            foreach (ColumnHeader col in _listView.Columns)
            {
                if (args.ColumnData.TryGetValue(col.Text, out var value))
                {
                    item.ColumnDic[col.Text] = value;
                }
            }
        }

        public void RefreshItemByFullPath(string path)
        {
            _listViewWrapper.RefreshItemByFullPath(path);
        }

        public void RefreshItem(CShellItem? item)
        {
            _listViewWrapper.RefreshItem(item);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="pathName">The display path of the folder.</param>
        /// <param name="csi">The <see cref="CShellItem"/> representing the folder to display.</param>
        /// <param name="includeFolder">True to include subdirectories in the list.</param>
        /// <param name="reload">True to force a reload even if the same item was previously selected.</param>
        public async Task LoadDirectory(string pathName, bool includeFolder = true, bool reload = false)
        {
            if (!reload && (_currentFolderCsi is not null && pathName == CurrentPath)) return;

            CShellItem csi;
            if (pathName == null) 
                csi = null;
            else
                csi = CShellItemFactory.CreateCShItem(pathName);

            await LoadDirectoryBaseAsync(csi, includeFolder);

            CurrentFolderCsi = csi;
        }

        public async Task LoadDirectory(CShellItem csi, bool includeFolder = true, bool reload = false)
        {
            if (!reload && (_currentFolderCsi is not null && csi.FullPath == CurrentPath)) return;

            await LoadDirectoryBaseAsync(csi, includeFolder);

            CurrentFolderCsi = csi;
        }

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/> asynchronously.
        /// </summary>
        public async Task LoadDirectoryBaseAsync(CShellItem? csi, bool includeFolder = true)
        {
            Debug.WriteLine("LoadDirectoryAsync: " + csi?.FullPath);
            
            _displayFilesCts?.Cancel();
            _displayFilesCts = new CancellationTokenSource();
            var token = _displayFilesCts.Token;

            // Capture sort settings and create comparer on UI thread to ensure thread-safe access to ColumnHeader properties
            int sortCol = _listViewWrapper.SortColumn;
            SortOrder sortOrder = _listViewWrapper.SortOrder;
            ColumnHeader colHeader = (sortCol >= 0 && sortCol < _listView.Columns.Count) ? _listView.Columns[sortCol] : null;
            CShellItemComparer comparer = null;
            if (sortOrder != SortOrder.None && colHeader != null)
            {
                comparer = new CShellItemComparer(this, sortCol, sortOrder, colHeader);
            }

            //CurrentPath = csi.FullPath; // Update immediately for UI/Settings consistency

            try
            {
                bool samePath = false;
                CShellItem oldCsi = null;
                if (_backHistory.Count == 0)
                    samePath = false;
                else
                {
                    oldCsi = _backHistory.Peek();
                    if (oldCsi == null && csi == null)
                        samePath = true;
                    else if (oldCsi == null || csi == null)
                        samePath = false;
                    else
                        samePath = CPidl.ResolvesToSamePathOrName(oldCsi.PIDL, csi.PIDL);
                }

                if (csi == null)
                {
                    _listView.BeginUpdate();
                    try
                    {
                        CurrentFolderCsi = null;
                        _listViewWrapper.Clear();
                        _listView.Tag = null;
                    }
                    finally
                    {
                        _listView.EndUpdate();
                    }
                }

                var result = await _staRunner.EnqueueWork(t =>
                {
                    var hierarchyCsi = _shellController.LoadFolderContents(csi, SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);
                    if (hierarchyCsi == null) return null;

                    var dirList = new List<CShellItem>();
                    var fileList = new List<CShellItem>();

                    if (includeFolder)
                    {
                        foreach (var dir in hierarchyCsi.Directories)
                        {
                            if (!IsExcluded(dir)) dirList.Add(dir);
                        }
                    }

                    if (!hierarchyCsi.DisplayName.Equals(CShellItemFactory.StrMyComputer))
                    {
                        foreach (var file in hierarchyCsi.Files)
                        {
                            if (!IsExcluded(file)) fileList.Add(file);
                        }
                    }

                    fileList.Sort();
                    if (includeFolder) dirList.Sort();

                    var combined = new List<CShellItem>(dirList.Count + fileList.Count);
                    if (includeFolder) combined.AddRange(dirList);
                    combined.AddRange(fileList);

                    // Warming up
                    bool isLarge = (_listViewWrapper.DisplayMode == ListViewDisplayMode.LargeIcon);
                    foreach (var item in combined)
                    {
                        if (t.IsCancellationRequested) return null;
                        
                        EnsureCustomColumnDataFetched(item); // Pre-fetch custom column data (e.g. NSFW scores)
                        
                        // Icon index
                        if (!IsThumbnailViewMode())
                        {
                            item.ImageIndex = SystemImageListManager.GetIconIndex(item, isLarge);
                        }
                    }

                    // Sort according to current settings after data is fetched
                    if (comparer != null)
                    {
                        combined.Sort(comparer);
                    }

                    return new
                    {
                        Items = combined,
                        FolderCsi = hierarchyCsi,
                        IsSamePath = samePath
                    };
                }, token);

                if (token.IsCancellationRequested) return;

                _listView.BeginUpdate();
                try
                {
                    if (result != null)
                    {
                        _listViewWrapper.Clear();
                        _listViewWrapper.AddRange(result.Items);
                        _listView.Tag = _currentFolderCsi;

                        OnScroll();
                    }
                    else
                    {
                        throw new Exception("ERROR: LoadDirectoryAsync - Failed to load directory contents.");
                    }
                }
                finally
                {
                    _listView.EndUpdate();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in DisplayFilesAsync: " + ex.ToString());
            }
            Debug.WriteLine("DisplayFilesAsync: done.");
        }



        /// <summary>
        /// Gets the zero-based index of the item identified by the specified full path.
        /// </summary>
        /// <remarks>Lookup is performed against an internal dictionary; -1 indicates no entry exists for
        /// the provided path.  Probably only works for virtual mode.
        /// </remarks>
        /// <param name="fullPath">The full path identifying the item to look up.</param>
        /// <returns>The zero-based index of the item if found; otherwise -1.</returns>
        public int GetIndexFromFullPath(string fullPath)
        {
            return _listViewWrapper.GetIndexFromFullPath(fullPath);
        }

        /// <summary>
        /// Finds a ListViewItem by its display name (case-insensitive).
        /// </summary>
        public ListViewItem FindItemByName(string name)
        {
            var fullPath = CurrentPath + name;
            return FindItemByPath(fullPath);
        }

        /// <summary>
        /// Finds a ListViewItem by its Shell ID (PIDL).
        /// </summary>
        /// <remarks>This is inefficient and takes O(n) time.</remarks>
        public ListViewItem FindItemByPidl(IntPtr pidl)
        {
            Debug.WriteLine("ExpList: FindItemByPidl Begin");
            try
            {
                for (int i = 0; i < _listViewWrapper.Count; i++)
                {
                    var item = _listViewWrapper.GetItem(i);
                    if (item != null && (CPidl.IsBinaryEqual(item.PIDL, pidl) || CPidl.ResolvesToSamePathOrName(item.PIDL, pidl)))
                        return _listViewWrapper.GetListViewItem(i);
                }
                return null;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindItemByPidl End");
            }
        }

        /// <summary>
        /// Finds a ListViewItem by its full filesystem path.
        /// </summary>
        public ListViewItem FindItemByPath(string path)
        {
            Debug.WriteLine("ExpList: FindItemByPath Begin");
            try
            {
                int index = _listViewWrapper.GetIndexFromFullPath(path);
                if (index >= 0)
                    return _listViewWrapper.GetListViewItem(index);
                return null;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindItemByPath End");
            }
        }

        #endregion


        #region Private Methods

        private bool IsExcluded(CShellItem item)
        {
            if (_excludedItems.Count == 0 || item == null) return false;
            var path = (item.FullPath ?? "").Trim(':', '{', '}');
            return _excludedItems.Contains(path);
        }

        /// <summary>
        /// Creates a <see cref="ListViewItem"/> for a given <see cref="CShellItem"/>.
        /// Populates columns based on <see cref="ExpListGetColumnData"/> event or <see cref="ColumnHeader.Tag"/> mapping.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to create the list view item for.</param>
        /// <returns>A configured <see cref="ListViewItem"/>.</returns>
        private ListViewItem CreateListviewItemCallback(CShellItem item)
        {
            try
            {
                if (item == null) return new ListViewItem("Error: no CShellItem provided to MakeLVItem()");

                Debug.WriteLine("ExpList: MakeLVItem Begin - " + item.DisplayName);

                ListViewItem lvi = new ListViewItem(item.DisplayName);

                UpdateListviewItemCallback(lvi, item);

                return lvi;
            }
            finally
            {
                //Debug.WriteLine("ExpList: MakeLVItem End");
            }
        }

        private bool _refreshing = false; //This variable is prevent reentrancy problems on the ui thread
        private bool _refreshPending = false;
        private bool _refetchImages = false;
        private ListViewItem[]? _pendingItems = null;

        /// <summary>
        /// Increments the enumeration depth counter. While depth > 0, DoItemUpdate will
        /// defer shell item modifications to prevent reentrant mutation of _listView.Items.
        /// Must be paired with <see cref="ExitListViewEnumeration"/>.
        /// </summary>
        private void EnterListViewEnumeration()
        {
            Debug.WriteLine("ExpList: EnterListViewEnumeration Begin");
            try
            {
                _enumerationDepth++;
            }
            finally
            {
                Debug.WriteLine("ExpList: EnterListViewEnumeration End");
            }
        }

        /// <summary>
        /// Decrements the enumeration depth counter. When it reaches 0, any deferred
        /// shell item updates are drained and applied.
        /// Must be paired with <see cref="EnterListViewEnumeration"/>.
        /// </summary>
        private void ExitListViewEnumeration()
        {
            Debug.WriteLine("ExpList: ExitListViewEnumeration Begin");
            try
            {
                _enumerationDepth--;
                if (_enumerationDepth <= 0)
                {
                    _enumerationDepth = 0;
                    DrainDeferredUpdates();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExitListViewEnumeration End");
            }
        }

        /// <summary>
        /// Processes all deferred shell item updates that were queued while an enumeration was in progress.
        /// </summary>
        private void DrainDeferredUpdates()
        {
            Debug.WriteLine("ExpList: DrainDeferredUpdates Begin");
            try
            {
                while (_deferredUpdates.Count > 0)
                {
                    var (sender, e) = _deferredUpdates.Dequeue();
                    DoItemUpdate(sender, e);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: DrainDeferredUpdates End");
            }
        }

        /// <summary>
        /// Increments the image list mutation depth counter. While depth > 0, 
        /// ThumbnailManager_ThumbnailReady will defer image list modifications 
        /// to prevent reentrancy during OS draw cycles.
        /// Must be paired with <see cref="ExitImageListMutation"/>.
        /// </summary>
        internal void EnterImageListMutation()
        {
            _imageListMutationDepth++;
        }

        /// <summary>
        /// Decrements the image list mutation depth counter. When it reaches 0, 
        /// any deferred thumbnail updates are drained and applied.
        /// Must be paired with <see cref="EnterImageListMutation"/>.
        /// </summary>
        internal void ExitImageListMutation()
        {
            _imageListMutationDepth--;
            if (_imageListMutationDepth <= 0)
            {
                _imageListMutationDepth = 0;
                DrainDeferredThumbnailUpdates();
            }
        }

        /// <summary>
        /// Processes all deferred thumbnail updates that were queued while an image list 
        /// mutation guard was active.
        /// </summary>
        private void DrainDeferredThumbnailUpdates()
        {
            while (_deferredThumbnailUpdates.Count > 0)
            {
                var (sender, e) = _deferredThumbnailUpdates.Dequeue();
                ThumbnailManager_ThumbnailReady(sender, e);
            }
        }

        /// <summary>
        /// Executes the action immediately if no enumeration is in progress, otherwise
        /// defers it via BeginInvoke to run after the enumeration completes.
        /// Use this for ListView modification operations outside of DoItemUpdate.
        /// </summary>
        private void InvokeWhenListViewReady(Action action)
        {
            Debug.WriteLine("ExpList: InvokeWhenListViewReady Begin");
            try
            {
                if (_enumerationDepth > 0)
                {
                    BeginInvoke(() => InvokeWhenListViewReady(action));
                    return;
                }
                action();
            }
            finally
            {
                Debug.WriteLine("ExpList: InvokeWhenListViewReady End");
            }
        }

        //private void RepopulateListViewCore()
        //{
        //    Debug.WriteLine("ExpList: RefreshListViewCore Begin");
        //    try
        //    {
        //        // If an enumeration is in progress (reentrancy via message pumping),
        //        // defer this refresh to after the enumeration completes.
        //        if (_enumerationDepth > 0)
        //        {
        //            BeginInvoke(new MethodInvoker(RepopulateListViewCore));
        //            return;
        //        }

        //        try
        //        {
        //            // snapshot old position safely
        //            int topIndex = 0;
        //            if (_listView.Items.Count > 0)
        //            {
        //                topIndex = GetTopIndex();
        //            }

        //            var newItems = _pendingItems ?? Array.Empty<ListViewItem>();

        //            Console.WriteLine("Begin loading items into listview...");
        //            _listView.BeginUpdate();
        //            try
        //            {
        //                if (VirtualMode)
        //                {
        //                    int count = newItems == null ? 0 : newItems.Length;

        //                    _listView.VirtualListSize = count;
        //                    _listView.Tag = _currentFolderCsi;
        //                    _listView.Refresh();
        //                }
        //                else
        //                {
        //                    _listView.Items.Clear();
        //                    _listView.Items.AddRange(newItems);

        //                    if (_listView.Items.Count > 0)
        //                    {
        //                        _listView.Tag = _currentFolderCsi; // For ClvDropWrapper

        //                        topIndex = Math.Max(0, Math.Min(topIndex, _listView.Items.Count - 1));
        //                        _listView.EnsureVisible(topIndex);

        //                        if (_refetchImages) LoadImagesForItems();
        //                    }
        //                }
        //            }
        //            finally
        //            {
        //                _listView.EndUpdate();
        //            }
        //            Console.WriteLine("End loading items into listview");
        //        }
        //        finally
        //        {
        //            _refreshing = false;
        //            _refreshPending = false;
        //        }
        //    }
        //    finally
        //    {
        //        Debug.WriteLine("ExpList: RefreshListViewCore End");
        //    }
        //}

        /// <summary>
        /// Launches a file using the default system handler.
        /// </summary>
        /// <param name="csi">The <see cref="CShellItem"/> to launch.</param>
        private void LaunchFile(CShellItem csi)
        {
            Debug.WriteLine("ExpList: LaunchFile Begin");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = csi.FullPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            finally
            {
                Debug.WriteLine("ExpList: LaunchFile End");
            }
        }

        /// <summary>
        /// Determines if the current display mode is a thumbnail-based view.
        /// </summary>
        /// <returns>True if in a thumbnail view mode.</returns>
        private bool IsThumbnailViewMode() => DisplayMode == ListViewDisplayMode.Thumbnail || DisplayMode == ListViewDisplayMode.LargeThumbnail || DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail;

        /// <summary>
        /// Determines if the mouse coordinates are within the client area of the specified control.
        /// </summary>
        /// <param name="ctl">The control to check.</param>
        /// <param name="e">The <see cref="MouseEventArgs"/> containing the mouse position.</param>
        /// <returns>True if the mouse is within the control's client area.</returns>
        private bool IsWithin(Control ctl, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: IsWithin Begin");
            try
            {
                if (e.X < 0 || e.Y < 0) return false;
                Rectangle cr = ctl.ClientRectangle;
                if (e.X > cr.Width || e.Y > cr.Height) return false;
                return true;
            }
            finally
            {
                Debug.WriteLine("ExpList: IsWithin End");
            }
        }

        /////// <summary>
        /////// Sorts the items in the list view based on their tags (CShellItem).
        /////// </summary>
        //private void SortLVItems()
        //{
        //    Debug.WriteLine("ExpList: SortLVItems Begin");
        //    try
        //    {
        //        if (VirtualMode)
        //        {
        //            if (_listView.ListViewItemSorter is LVColSorter sorter)
        //            {
        //                _listViewWrapper.Sort(sorter.SortColumn, sorter.OrderOfSort);
        //            }
        //            return;
        //        }

        //        if (_listView.Items.Count < 2) return;

        //        EnterListViewEnumeration();
        //        try
        //        {
        //            _listView.BeginUpdate();
        //            var tmp = new ListViewItem[_listView.Items.Count];
        //            _listView.Items.CopyTo(tmp, 0);
        //            Array.Sort(tmp, new TagComparer());
        //            _listView.Items.Clear();
        //            _listView.Items.AddRange(tmp);
        //            _listView.EndUpdate();
        //        }
        //        finally
        //        {
        //            ExitListViewEnumeration();
        //        }
        //    }
        //    finally
        //    {
        //        Debug.WriteLine("ExpList: SortLVItems End");
        //    }
        //}

        #endregion

        #region Navigation

        /// <summary>
        /// Navigates back to the previous folder in the history.
        /// </summary>
        public async void GoBack()
        {
            Debug.WriteLine("ExpList: GoBack Begin");
            try
            {
                if (_backHistory.Count > 0)
                {
                    _forwardHistory.Push(_currentFolderCsi);
                    var prev = _backHistory.Pop();
                    _isNavigatingHistory = true;
                    try
                    {
                        await LoadDirectory(prev, true);
                    }
                    finally
                    {
                        _isNavigatingHistory = false;
                    }
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: GoBack End");
            }
        }

        /// <summary>
        /// Navigates forward to the next folder in the history.
        /// </summary>
        public async void GoForward()
        {
            Debug.WriteLine("ExpList: GoForward Begin");
            try
            {
                if (_forwardHistory.Count > 0)
                {
                    _backHistory.Push(_currentFolderCsi);
                    var next = _forwardHistory.Pop();
                    _isNavigatingHistory = true;
                    try
                    {
                        await LoadDirectory(next, true);
                    }
                    finally
                    {
                        _isNavigatingHistory = false;
                    }
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: GoForward End");
            }
        }

        /// <summary>
        /// Navigates to the parent folder of the currently loaded folder.
        /// </summary>
        public async void GoUp()
        {
            Debug.WriteLine("ExpList: GoUp Begin");
            try
            {
                if (_currentFolderCsi?.Parent != null)
                {
                    var parent = _currentFolderCsi.Parent;
                    await LoadDirectory(parent, true);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: GoUp End");
            }
        }

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate back to.
        /// </summary>
        public bool CanGoBack => _backHistory.Count > 0;

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate forward to.
        /// </summary>
        public bool CanGoForward => _forwardHistory.Count > 0;

        /// <summary>
        /// Gets a value indicating whether the current folder has a parent folder to navigate to.
        /// </summary>
        public bool CanGoUp => _currentFolderCsi?.Parent != null;

        #endregion

        #region Event Handlers
        private void ExpFileList_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_Click Begin");
            try
            {
                ListView listView = (ListView)sender;

                if (listView.SelectedIndices.Count == 0) return;

                CShellItem? csi = null;
                if (listView.FocusedItem != null) //could be selected OR deselected
                {
                    csi = GetItem(listView.FocusedItem.Index);
                    if (csi == null) return;

                    if (csi.ImageIndex == -1)
                    {
                        _thumbnailManager.RequestThumbnail(csi, GetThumbnailSizeForMode(), listView.FocusedItem.Index);
                    }

                    ExpListItemClick?.Invoke(listView.FocusedItem, csi);
                }
                else
                {
                    ExpListItemClick?.Invoke(null, null);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_Click End");
            }
        }

        /// <summary>
        /// Handles double-click events on list view items. 
        /// Folders are navigated into, while files are launched.
        /// </summary>
        private async void ExpFileList_DoubleClick(object sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_DoubleClick Begin");
            try
            {
                if (_listView.SelectedIndices.Count <= 0) return;

                CShellItem? csi = null;
                if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                    csi = GetItem(_listView.FocusedItem.Index);
                else
                    csi = GetItem(_listView.SelectedIndices[0]);

                if (csi == null) return;

                if (csi.IsFolder)
                {
                    try
                    {
                        // Navigate into the folder
                        await LoadDirectory(csi, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error in starting application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (csi.FullPath.StartsWith(":"))
                    ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                else
                    ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_DoubleClick End");
            }
        }

        private void ExpFileList_SelectedIndexChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged Begin");

            if (IsShuttingDown) return;

            try
            {
                if (_listView.SelectedIndices.Count > 0)
                {
                    // If current SelectedItem is still selected, keep it.
                    // This handles the case where multiple items are selected and we don't want to 
                    // jump back to the first one in the list.
                    if (SelectedItem != null && _listViewWrapper.IsItemSelected(SelectedItem))
                    {
                        // keep SelectedItem as is
                    }
                    else if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                    {
                        SelectedItem = GetItem(_listView.FocusedItem.Index);
                    }
                    else
                    {
                        SelectedItem = GetItem(_listView.SelectedIndices[0]);
                    }
                }
                else
                {
                    SelectedItem = null;
                }

                if (VirtualMode)
                {
                    // In virtual mode, we pass null to because there are no items in _listView.SelectedItems
                    // Consumers should use SelectedCShellItems property instead.
                    SelectedIndexChanged?.Invoke(null);
                }
                else
                {
                    SelectedIndexChanged?.Invoke(_listView.SelectedItems);
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine("ExpList: InvalidOperationException in ExpFileList_SelectedIndexChanged: " + ex.ToString());
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine("ExpList: NullReferenceException in ExpFileList_SelectedIndexChanged: " + ex.ToString());
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged End");
            }
        }

        private void ExpFileList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged Begin");
            try
            {
                if (e.IsSelected)
                {
                    SelectedItem = GetItem(e.ItemIndex);
                }
                ItemSelectionChanged?.Invoke(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.Leave"/> event of the <see cref="_listView"/> ListView.
        /// Clears the current selection.
        /// </summary>
        /// what the hell good is this?  It makes it impossible to use any selections to do anything.
        //private void ExpFileList_Leave(object sender, EventArgs e)
        //{
        //    ExpFileList.SelectedItems.Clear();
        //}

        /// <summary>
        /// Handles the <see cref="ListView.BeforeLabelEdit"/> event.
        /// Determines if an item can be renamed and sets up the edit control.
        /// </summary>
        private void ExpFileList_BeforeLabelEdit(object sender, LabelEditEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit Begin");
            try
            {
                IntPtr editWnd = SendMessage(_listView.Handle, LVM_GETEDITCONTROL, 0, IntPtr.Zero);
                var csi = GetItem(e.Item);
                if (csi == null) { e.CancelEdit = true; return; }

                int textLen = Path.GetFileNameWithoutExtension(csi.DisplayName).Length;
                SendMessage(editWnd, EM_SETSEL, IntPtr.Zero, (IntPtr)textLen);

                if ((!csi.IsFileSystem) || csi.IsDisk ||
                    csi.FullPath == CShellItemFactory.CreateCShItem(CSIDL.MYDOCUMENTS).FullPath ||
                    !csi.CanRename)
                {
                    System.Media.SystemSounds.Beep.Play();
                    e.CancelEdit = true;
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit End");
            }
        }

        /// <summary>
        /// Handles the <see cref="ListView.AfterLabelEdit"/> event.
        /// Applies the new name to the shell item.
        /// </summary>
        private void ExpFileList_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit Begin");
            try
            {
                var item = GetItem(e.Item);
                if (item == null || e.Label == null || e.Label == string.Empty) return;

                try
                {
                    string newName = e.Label.Trim();

                    if (newName.Length < 1 || newName.IndexOfAny(Path.GetInvalidPathChars()) != -1)
                    {
                        e.CancelEdit = true;
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }

                    string path = item.FullPath;
                    int index = path.LastIndexOf('\\');
                    if (index == -1)
                    {
                        e.CancelEdit = true;
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }

                    IntPtr newPidl = IntPtr.Zero;
                    if (item.Parent.IShlFolder.SetNameOf(
                            _listView.Handle.ToInt32(),
                            CPidl.ILFindLastID(item.PIDL),
                            newName,
                            SHGDN.NORMAL,
                            newPidl) != S_OK)
                    {
                        System.Media.SystemSounds.Beep.Play();
                        e.CancelEdit = true;
                    }
                }
                catch
                {
                    e.CancelEdit = true;
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit End");
            }
        }

        private void ThumbnailManager_ThumbnailReady(object sender, ThumbnailReadyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => ThumbnailManager_ThumbnailReady(sender, e)));
                else
                    e.Thumbnail?.Dispose();
                return;
            }

            // If a draw cycle or another mutation is in progress, defer this update.
            if (_imageListMutationDepth > 0)
            {
                _deferredThumbnailUpdates.Enqueue((sender, e));
                return;
            }

            EnterImageListMutation();
            try
            {
                if (e.Size != GetThumbnailSizeForMode()) // if the display mode is changed, the thumbnail will have the wrong size. Discard.
                {
                    e.Thumbnail?.Dispose();
                    return;
                }

                if (e.Item == null || e.Item.Parent == null || e.Item.Parent.FullPath != CurrentPath)
                {
                    e.Thumbnail?.Dispose();
                    return;
                }

                int image_index = -1;
                if (e.Thumbnail != null)
                {
                    using (var bitmap = (Bitmap)e.Thumbnail)
                    {
                        image_index = _thumbnailManager.AddThumbnail(e, bitmap);
                    }
                }
                else
                {
                    image_index = _thumbnailManager.AddThumbnail(e, null);
                }

                if (image_index == -1)
                {
                    // Failed to add thumbnail, likely due to disposal or mode change. Just exit.
                    Debug.WriteLine("Failed to add thumbnail for item: " + e.Item.DisplayName);
                    return;
                }

                if (VirtualMode)
                {
                    lock (_listViewWrapper.VirtualItems)
                    {
                        int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                        if (index == -1)
                        {
                            // Failed to find item in listview, possibly due to deletion or move. Just exit.
                            Debug.WriteLine("Failed to find the item in the listview: " + e.Item.DisplayName);
                            return;
                        }
                        _listViewWrapper.GetItem(index).ImageIndex = image_index;
                        //Debug.WriteLine("Redrawing: " + e.Item.DisplayName);
                        _listViewWrapper._ListView.RedrawItems(index, index, false);
                    }
                }
                else
                {
                    int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                    var lvi = _listViewWrapper.GetItem(index);
                    if (lvi != null) lvi.ImageIndex = image_index;
                }
            }
            finally
            {
                ExitImageListMutation();
            }

        }

        #region Context Menu Handlers

        private readonly ExpControlsLib.ContextMenu m_WindowsContextMenu = new ExpControlsLib.ContextMenu();
        private bool m_OutOfRange;

        /// <summary>
        /// Handles the MouseLeave event to track when the mouse is outside the list view.
        /// </summary>
        private void ExpFileList_MouseLeave(object sender, EventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseLeave Begin");
            try
            {
                m_OutOfRange = true;
                OnMouseLeave(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseLeave End");
            }
        }

        private void ExpFileList_MouseEnter(object sender, EventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseEnter Begin");
            try
            {
                OnMouseEnter(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseEnter End");
            }
        }

        /// <summary>
        /// Handles the MouseDown event to reset the out-of-range flag for right-clicks.
        /// </summary>
        private void ExpFileList_MouseDown(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_MouseDown Begin");
            try
            {
                if (e.Button == MouseButtons.Right) m_OutOfRange = false;
                OnMouseDown(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_MouseDown End");
            }
        }

        /// <summary>
        /// Handles the MouseUp event to trigger context menus or middle-click actions.
        /// </summary>
        private async void ExpFileList_MouseUp(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_MouseUp Begin");
            try
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (!IsWithin(_listView, e)) return;
                    if (m_OutOfRange) return;

                    Point pt = new Point(e.X, e.Y);
                    ListViewItem tn = _listView.GetItemAt(e.X, e.Y);

                    if (tn != null && _listView.SelectedIndices.Count > 0)
                    {
                        var itms = SelectedCShellItems.ToArray();

                        CMInvokeCommandInfoEx cmi;
                        bool allowRename = itms.Length <= 1; //Don't allow rename of more than 1 item

                        if (m_WindowsContextMenu.ShowMenu(Handle, itms, MousePosition, allowRename, out cmi, MinimalContextMenu))
                        {
                            int verbId = cmi.lpVerb.ToInt32();
                            if (verbId == 99999)
                            {
                                ExpListMove?.Invoke(this, new ExpListMoveEventArgs(itms));
                            }
                            else if (verbId == 99998)
                            {
                                ExpListCopy?.Invoke(this, new ExpListCopyEventArgs(itms));
                            }
                            else
                            {
                                byte[] cmdBytes = new byte[256];
                                m_WindowsContextMenu.cntxMenuBase.GetCommandString(verbId, (int)GCS.VERBA, 0, cmdBytes, 256);
                                string cmdName = SzToString(cmdBytes).ToLowerInvariant();

                                if (cmdName.Equals("rename"))
                                {
                                    _listView.LabelEdit = true;
                                    tn.BeginEdit();
                                }
                                else
                                {
                                    IntPtr parentPidl = itms[0].Parent == ShellController.DesktopCSI
                                        ? itms[0].PIDL
                                        : itms[0].Parent.PIDL;
                                    
                                    var capturedRelPidls = itms.Select(i => CPidl.Copy(i.LastPIDL)).ToArray();
                                    var capturedParentPidl = parentPidl;

                                    await _staRunner.EnqueueWork(_ =>
                                    {
                                        IShellFolder desktop = null;
                                        IShellFolder parentFolder = null;
                                        IntPtr iUnknownOut = IntPtr.Zero;
                                        IContextMenu? contextMenu = null;

                                        // Create a hidden dummy window on this thread to act as the owner.
                                        using (Control dummy = new Control())
                                        {
                                            IntPtr dummyHandle = dummy.Handle;

                                            try
                                            {
                                                SHGetDesktopFolder(ref desktop);
                                                if (desktop == null) return -1;

                                                if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                                                    parentFolder = desktop;
                                                else
                                                {
                                                    IntPtr folderPtr = IntPtr.Zero;
                                                    if (desktop.BindToObject(capturedParentPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref folderPtr) != S_OK) return -1;
                                                    parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(folderPtr, typeof(IShellFolder));
                                                    Marshal.Release(folderPtr);
                                                }

                                                IntPtr rgfReserved = IntPtr.Zero;
                                                if (parentFolder.GetUIObjectOf(IntPtr.Zero, (uint)capturedRelPidls.Length, capturedRelPidls, IID_IContextMenu, rgfReserved, out iUnknownOut) != S_OK) return -1;

                                                contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(iUnknownOut, typeof(IContextMenu));

                                                var invokeCmi = new CMInvokeCommandInfoEx
                                                {
                                                    cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                                                    hwnd = dummyHandle,
                                                    nShow = (int)SW.SHOWNORMAL,
                                                    fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                                                    ptInvoke = pt,
                                                    lpVerb = (IntPtr)verbId,
                                                    lpVerbW = (IntPtr)verbId
                                                };

                                                return contextMenu.InvokeCommand(invokeCmi);
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine($"Error in background context menu invocation: {ex.Message}");
                                                return -1;
                                            }
                                            finally
                                            {
                                                if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                                                if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                                                if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                                                if (desktop != null) Marshal.ReleaseComObject(desktop);
                                                foreach (var p in capturedRelPidls) Marshal.FreeCoTaskMem(p);
                                            }
                                        }
                                    });
                                }
                            }

                            if (m_WindowsContextMenu.cntxMenuBase != null)
                                Marshal.ReleaseComObject(m_WindowsContextMenu.cntxMenuBase);
                        }
                    }
                    else
                    {
                        ShowAndHandleContextMenu(MousePosition);
                    }
                }

                ExpListItemGetSelItems?.Invoke(_listView.SelectedItems);

                if (e.Button == MouseButtons.Middle && _listView.SelectedIndices.Count > 0)
                {
                    CShellItem? csi = null;
                    if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                        csi = GetItem(_listView.FocusedItem.Index);
                    else
                        csi = GetItem(_listView.SelectedIndices[0]);

                    if (csi != null) ExpListItemMouseMBUp?.Invoke(csi.FullPath, csi);
                }
                OnMouseUp(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_MouseUp End");
            }
        }

        private void ExpFileList_MouseMove(object sender, MouseEventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseMove Begin");
            try
            {
                OnMouseMove(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseMove End");
            }
        }

        #endregion


        /// <summary>
        /// Handles KeyDown events for shortcuts (Ctrl+A, Ctrl+C/V/X, Delete, F2, F5, Enter).
        /// </summary>
        private void ExpFileList_KeyDown(object sender, KeyEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyDown Begin");
            try
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    if (VirtualMode)
                    {
                        EnterListViewEnumeration();
                        _listView.BeginUpdate();
                        try
                        {
                            for (int i = 0; i < _listView.VirtualListSize; i++)
                                _listView.SelectedIndices.Add(i);
                        }
                        finally
                        {
                            _listView.EndUpdate();
                            ExitListViewEnumeration();
                        }
                    }
                    else
                    {
                        EnterListViewEnumeration();
                        try
                        {
                            foreach (ListViewItem item in _listView.Items)
                            {
                                if (item is null) continue;
                                item.Selected = true;
                            }
                        }
                        finally
                        {
                            ExitListViewEnumeration();
                        }
                    }
                    ExpListItemGetSelItems?.Invoke(_listView.SelectedItems);
                }

                if (e.Control)
                {
                    switch (e.KeyCode)
                    {
                        case Keys.X: WinMenu("cut"); break;
                        case Keys.C: WinMenu("copy"); break;
                        case Keys.V: WinMenu("paste"); break;
                        case Keys.Z: MessageBox.Show("Don't support UNDO now!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); break;
                    }
                }

                if (e.KeyCode == Keys.F2 && _listView.SelectedIndices.Count > 0)
                {
                    if (VirtualMode)
                    {
                        // In virtual mode, we must ensure the item is cached or retrieved
                        _listView.FocusedItem?.BeginEdit();
                    }
                    else
                    {
                        _listView.SelectedItems[0].BeginEdit();
                    }
                }

                if (e.KeyCode == Keys.F5)
                {
                    _shellController.ShellUpdater.DoUpdateDir(_currentFolderCsi);
                    _listViewWrapper.Sort();
                }

                if (e.KeyCode == Keys.Enter && _listView.SelectedIndices.Count > 0)
                {
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi == null) return;

                    if (csi.FullPath.StartsWith(":"))
                        ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                    else
                        ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);

                    if (!csi.IsFolder)
                    {
                        try
                        {
                            // LaunchFile(csi); // Let MainForm handle it via the event.
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error in starting application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }

                OnKeyDown(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyDown End");
            }
        }


        /// <summary>
        /// Handles the KeyUp event for navigation keys.
        /// </summary>
        private void ExpFileList_KeyUp(object sender, KeyEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyUp Begin");
            try
            {
                if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                    && _listView.SelectedIndices.Count > 0)
                {
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi != null) ExpListItemArrowKeyUp?.Invoke(csi.FullPath, csi);
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    WinMenu("delete");
                }

                OnKeyUp(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyUp End");
            }
        }

        private void ExpFileList_KeyPress(object sender, KeyPressEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyPress Begin");
            try
            {
                OnKeyPress(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyPress End");
            }
        }


        #endregion Event Handlers

        #region Lazy Thumbnail Loading Support

        /// <summary>
        /// Configures the image lists bound to the ListView for the given display mode.
        /// For built-in Windows view modes (Details, List, LargeIcon, Tile), the system image
        /// list is applied and each item's <see cref="ListViewItem.ImageIndex"/> is refreshed.
        /// For custom thumbnail modes, the ListView is switched to LargeIcon view and
        /// <see cref="LoadThumbnailsForItems"/> is called to populate thumbnail images.
        /// </summary>
        /// <param name="value">The <see cref="ListViewDisplayMode"/> to configure for.</param>
        private void SetImageListForMode(ListViewDisplayMode value)
        {
            Debug.WriteLine("ExpList: SetAndLoadImageList Begin");
            try
            {
                if (value <= ListViewDisplayMode.Tile) //built-in Windows 95 Shell view modes
                {
                    bool large = (value == ListViewDisplayMode.LargeIcon);

                    if (large)
                        SystemImageListManager.SetListViewImageList(_listView, true, false);
                    else
                        SystemImageListManager.SetListViewImageList(_listView, false, false);
                }
                else //custom thumbnail view modes
                {
                    EnterListViewEnumeration();
                    try
                    {
                        EnterImageListMutation();
                        try
                        {
                            _thumbnailManager.SetImageListForSize(GetThumbnailSizeForMode(value));
                        }
                        finally
                        {
                            ExitImageListMutation();
                        }
                    }
                    finally
                    {
                        ExitListViewEnumeration();
                    }
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: SetAndLoadImageList End");
            }
        }

        private void LoadImagesForVisibleItems(ListViewDisplayMode? mode = null)
        {
            Debug.WriteLine("ExpList: LoadImagesForItems Begin");
            try
            {
                mode = mode == null ? DisplayMode : mode;

                if (mode <= ListViewDisplayMode.Tile)
                {
                    LoadIconsForItems(true);
                }
                else
                {
                    LoadThumbnailsForItems(GetThumbnailSizeForMode(mode), true);
                }
            }
            finally
            {
                //Debug.WriteLine("ExpList: LoadImagesForItems End");
            }
        }

        /// <summary>
        /// loads icons (not thumbnails) for the items in the list.
        /// Can either load all icons or only icons near the visible section.
        /// </summary>
        /// <param name="onlyVisible">true if you only want icons near the visible items.</param>
        private void LoadIconsForItems(bool onlyVisible = false)
        {
            Debug.WriteLine("ExpList: LoadIconsForItems Begin");
            try
            {
                if (!_listView.IsHandleCreated) return;

                bool isLarge = (_listView.View == View.LargeIcon);

                EnterListViewEnumeration();
                try
                {
                    if (VirtualMode)
                    {
                        int startIndex = 0;
                        int endIndex = _listViewWrapper.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = _listViewWrapper.GetTopIndex();
                            int countPerPage = _listViewWrapper.GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex - countPerPage);
                            endIndex = Math.Min(_listViewWrapper.Count - 1, topIndex + countPerPage * 2);
                        }

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = GetItem(i);
                            if (csi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItem returned null for index {i}");
                                continue;
                            }
                            csi.ImageIndex = SystemImageListManager.GetIconIndex(csi, isLarge);

                            var lvi = _listViewWrapper.GetLviFromVirtual(i);

                            if (lvi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItemInternal returned null for index {i}");
                                continue;
                            }

                            if (lvi.ImageIndex != csi.ImageIndex)
                            {
                                lvi.ImageIndex = csi.ImageIndex;
                                _listView.RedrawItems(i, i, false);
                            }
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;

                        foreach (ListViewItem item in _listView.Items)
                        {
                            if (item is null) continue;
                            if (!clientRect.IntersectsWith(item.Bounds)) continue;

                            if (item.Tag is CShellItem csi && item.ImageIndex == -1)
                            {
                                item.ImageIndex = SystemImageListManager.GetIconIndex(csi, isLarge);
                            }
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: LoadIconsForItems End");
            }
        }

        /// <summary>
        /// Gets the pixel size for a given thumbnail display mode
        /// </summary>
        private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        {
            //Debug.WriteLine("ExpList: GetThumbnailSizeForMode Begin");
            try
            {
                mode ??= DisplayMode;
                return mode switch
                {
                    ListViewDisplayMode.Thumbnail => 48,
                    ListViewDisplayMode.LargeThumbnail => 96,
                    ListViewDisplayMode.ExtraLargeThumbnail => 256,
                    _ => 48 // Default to 48 for non-thumbnail modes, though this should never be used
                };
            }
            finally
            {
                //Debug.WriteLine("ExpList: GetThumbnailSizeForMode End");
            }
        }

        /// <summary>
        /// loads thumbnails (not icons) for the items in the list.
        /// Can either load all thumbnails or only some thumbnails near the visible section.
        /// </summary>
        /// <param name="thumbnailSize">The size of the thumbnails to load.</param>
        /// <param name="onlyVisible">If true, only loads thumbnails for items currently visible in the viewport that don't already have one.</param>
        private void LoadThumbnailsForItems(int thumbnailSize, bool onlyVisible = false)
        {
            Debug.WriteLine("ExpList: LoadThumbnailsForItems Begin");

            try
            {
                if (!_listView.IsHandleCreated) return;

                EnterListViewEnumeration();
                try
                {
                    if (VirtualMode)
                    {
                        int startIndex = 0, backFill = 0;
                        int endIndex = _listViewWrapper.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = _listViewWrapper.GetTopIndex();
                            int countPerPage = _listViewWrapper.GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex);
                            endIndex = Math.Min(_listViewWrapper.Count - 1, topIndex + countPerPage * 2);
                            backFill = startIndex - countPerPage/2; // if user scrolls up, we want to have thumbnails ready for the previous page
                        }

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = _listViewWrapper.GetItem(i);
                            if (csi.ImageIndex != -1)
                            {
                                // Skip if already in image list (GetThumbnailIndex will return != -1)
                                if (_thumbnailManager.GetThumbnailIndex(csi, thumbnailSize) != -1) continue;
                            }

                            _thumbnailManager.RequestThumbnail(csi, thumbnailSize, i);
                            Debug.WriteLine("ExpList: thumbnailManager.RequestThumbnail: " + i.ToString());
                        }

                        backFill = backFill < 0 ? 0 : backFill;
                        for (int i = backFill; i < startIndex; i++)
                        {
                            var csi = _listViewWrapper.GetItem(i);
                            if (csi is null)
                            {
                                Debug.WriteLine($"LoadThumbnailsForItems: GetItem returned null for index {i}");
                                continue;
                            }

                            if (csi.ImageIndex == -1)
                            {
                                _thumbnailManager.RequestThumbnail(csi, thumbnailSize, i);
                                Debug.WriteLine("ExpList: thumbnailManager.RequestThumbnail: " + i.ToString());
                            }
                            else
                            {
                                // Skip if already in image list (GetThumbnailIndex will return != -1)
                                if (_thumbnailManager.GetThumbnailIndex(csi, thumbnailSize) != -1) continue;
                            }
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;
                        clientRect.Inflate(0, clientRect.Height); // buffer zone

                        foreach (ListViewItem item in _listView.Items)
                        {
                            if (item is null) continue;
                            if (onlyVisible && item.ImageIndex != -1) continue;
                            if (!clientRect.IntersectsWith(item.Bounds)) continue;

                            if (item.Tag is CShellItem csi && !string.IsNullOrWhiteSpace(csi.FullPath))
                                _thumbnailManager.RequestThumbnail(csi, thumbnailSize);
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }

            }
            finally
            {
                Debug.WriteLine("ExpList: LoadThumbnailsForItems End");
            }
        }

        private ListViewScrollHook _scrollHook;
        /// <summary>
        /// The _thumbnailTimer is a debounce timer used to implement Lazy Loading. Even though the actual thumbnail generation
        ///happens on background threads(via ThumbnailProvider), the timer is essential for maintaining UI performance and
        ///efficiency.
        ///Here is why it's necessary:
        ///1. Preventing "Scroll Stutter"
        ///The ListView fires dozens of scroll events per second during rapid scrolling.If the app tried to calculate which
        ///items are visible on every single event, the UI thread would "stutter" because it's spending too much time doing
        ///geometry calculations instead of rendering the list. The timer waits for a 200ms pause in scrolling before doing this
        ///calculation.
        /// </summary>
        private System.Windows.Forms.Timer _scrollDebounceTimer;

        /// <summary>
        /// Hook for capturing scroll and other events from the ListView to trigger lazy loading.
        /// </summary>
        private class ListViewScrollHook : NativeWindow
        {
            private readonly Action _onScroll;
            private readonly ListView _listView;
            private readonly VirtualListViewWrapper _listViewWrapper;

            public ListViewScrollHook(VirtualListViewWrapper listView, Action onScroll)
            {
                Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook Begin");
                try
                {
                    _onScroll = onScroll;
                    _listViewWrapper = listView;
                    _listView = _listViewWrapper._ListView;
                    AssignHandle(_listView.Handle);
                }
                finally
                {
                    Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook End");
                }
            }

            protected override void WndProc(ref Message m)
            {
                //Debug.WriteLine("ExpList.WndProc Begin");
                try
                {
                    try
                    {
                        base.WndProc(ref m); //must call before exit or you will get form creation errors.
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        _listView.SelectedIndices.Clear();
                    }

                    if (m.Msg == WindowsMessages.WM_QUERYENDSESSION || m.Msg == WindowsMessages.WM_ENDSESSION || m.Msg == WindowsMessages.WM_CLOSE) // || m.Msg == WindowsMessages.WM_NCDESTORY WM_NCDESTORY get's called during startup
                        _listViewWrapper.IsShuttingDown = true;

                    if (_listViewWrapper.IsShuttingDown) return;

                    switch (m.Msg)
                    {
                        case WindowsMessages.WM_VSCROLL:
                        case WindowsMessages.WM_HSCROLL:
                        case WindowsMessages.WM_MOUSEWHEEL:
                            _listViewWrapper.LastTopIndex = -1; //invalid due to a scroll moving items
                            QueueOnScroll();
                            break;
                        case WindowsMessages.WM_KEYDOWN:
                            Keys key = (Keys)m.WParam.ToInt32();
                            if (key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || key == Keys.Up || key == Keys.Down)
                            {
                                //the problem with the arrow keys is we don't have a test yet to see if the navigation movement stayed with the list of visible items or moved to a non-visible item
                                _listViewWrapper.LastTopIndex = -1; //invalid due to a scroll moving items
                                QueueOnScroll();
                            }
                            break;
                    }
                }
                finally
                {
                    //Debug.WriteLine("ExpList.WndProc End");
                }
            }

            private int _scrollQueued;
            private void QueueOnScroll()
            {
                Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll Begin");
                try
                {
                    if (_listView.IsDisposed || !_listView.IsHandleCreated) return;
                    if (System.Threading.Interlocked.Exchange(ref _scrollQueued, 1) == 1) return;

                    _listView.BeginInvoke((MethodInvoker)(() =>
                    {
                        System.Threading.Interlocked.Exchange(ref _scrollQueued, 0);
                        if (!_listView.IsDisposed) _onScroll?.Invoke();
                    }));
                }
                finally
                {
                    Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll End");
                }
            }
        }

        private void OnScroll()
        {
            Debug.WriteLine("ExpList: OnListViewScroll Begin");
            if (IsShuttingDown) return;
            try
            {
                //issues a new request to get thumbnails after a brief debounce delay
                _scrollDebounceTimer?.Stop();
                _scrollDebounceTimer?.Start();
            }
            finally
            {
                //Debug.WriteLine("ExpList: OnListViewScroll End");
            }
        }

        public void EnsureVisible(int index)
        {
            _listViewWrapper._ListView.EnsureVisible(index);
        }

        #endregion




    }


}
