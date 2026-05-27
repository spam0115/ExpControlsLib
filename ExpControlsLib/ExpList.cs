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

        // Avoid Globalization problem-- an empty timevalue
        private static readonly DateTime EmptyTimeValue = new DateTime(1, 1, 1, 0, 0, 0);

        private CShellItem? _currentFolderCsi;
        private CShellItem? _selectedItem; // The currently selected item within the list
        private Dictionary<string, ListViewItem> _itemIndex = new(StringComparer.OrdinalIgnoreCase); //if we ever have put real multithreading code into this control, change this to a concurrentdictionary

        private Stack<CShellItem> _backHistory = new();
        private Stack<CShellItem> _forwardHistory = new();
        private bool _isNavigatingHistory = false;

        private CDragWrapper DW;         // Wrapper for Drag ops originating in ExpFileList
        private ClvDropWrapper DropWrap; // Wrapper for Drop ops targeting ExpFileList

        private bool m_CreateNew = false; // Flag for NewMenu processing of "New" item
        private ThumbnailImageListManager _thumbnailManager; // Manager for thumbnail display modes

        private ShellController? _shellController = null;

        private bool _useVirtualMode;
        private List<CShellItem> _virtualItems = new();
        private Dictionary<int, ListViewItem> _itemCache = new();
        private Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        private LVColSorter _sorter;

        // Reentrancy guard: prevents DoItemUpdate from modifying _listView.Items
        // while an enumeration is in progress (Invoke() pumps messages and can trigger
        // reentrant shell notifications on the same UI thread).
        private int _enumerationDepth = 0;
        private readonly Queue<(object sender, ShellItemUpdateEventArgs e)> _deferredUpdates = new();

        #endregion

        #region Public fields
        /// <summary>
        /// Delegate for the <see cref="ExpListItemClick"/> event.
        /// </summary>
        /// <param name="SelPath">The path of the clicked item.</param>
        /// <param name="Item">The <see cref="CShellItem"/> that was clicked.</param>
        public delegate void ExpListItemClickEventHandler(CShellItem Item);
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
            get; set
            {
                if (field == value) return;
                if (value <= ListViewDisplayMode.Tile) // View values native to the ListView control 
                {
                    _listView.View = (View)value;
                }
                else
                {
                    _listView.View = View.LargeIcon; //XP kludge for thumbnail mode
                }
                field = value;

                SetImageListForMode(value);
                if (_useVirtualMode) LoadImagesForItems();

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
            get => (_listView.ListViewItemSorter as LVColSorter)?.SortColumn ?? 0;
            set
            {
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                    sorter.SortColumn = value;
            }
        }

        /// <summary>
        /// Gets the current sort order.
        /// </summary>
        [Browsable(false)]
        public SortOrder SortOrder => (_listView.ListViewItemSorter as LVColSorter)?.OrderOfSort ?? SortOrder.None;


        /// <summary>
        /// Gets or sets a value indicating whether the list view is in virtual mode.
        /// </summary>
        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _useVirtualMode;
            set
            {
                if (_useVirtualMode == value) return;
                _useVirtualMode = value;
                _listView.VirtualMode = value;

                if (value)
                {
                    _listView.RetrieveVirtualItem -= RetrieveVirtualItem; //just in case
                    _listView.RetrieveVirtualItem += RetrieveVirtualItem;
                    _listView.Items.Clear();
                    _itemIndex.Clear();
                }
                else
                {
                    _listView.RetrieveVirtualItem -= RetrieveVirtualItem;
                    _virtualItems.Clear();
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }

                if (_currentFolderCsi != null)
                    DisplayFiles(_currentPath, _currentFolderCsi, true, reload: true);
            }
        }

        private string? _currentPath = null;
        /// <summary>
        /// Gets or sets the current file system path displayed in the list view.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current path of ExpFileList"),
         DefaultValue(null)]
        public string? CurrentPath
        {
            get => _currentPath;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    bool needsUpdate = !string.IsNullOrEmpty(_currentPath);
                    if (needsUpdate)
                    {
                        _listView.BeginUpdate();
                        ClearListInternal();
                    }
                    _currentPath = value;

                    var oldCsi = _currentFolderCsi;
                    _currentFolderCsi = null;
                    ExpListCurrentFolderChanged?.Invoke(null, oldCsi);

                    if (needsUpdate)
                        _listView.EndUpdate();
                }
                else
                {
                    if (value == _currentPath && _currentFolderCsi != null) return;
                     
                    var oldCsi = _currentFolderCsi;

                    var newCsi = _shellController.HierachyManager.FindOrAdd(value);

                    if (newCsi != null && newCsi.IsFolder)
                        DisplayFiles(value, newCsi, true);
                    else
                    {
                        _listView.BeginUpdate();
                        ClearListInternal();
                        _currentPath = value;
                        _currentFolderCsi = null;
                        ExpListCurrentFolderChanged?.Invoke(newCsi, oldCsi);
                        _listView.EndUpdate();
                    }
                }
            }
        }

        private void ClearListInternal() //todo: move this to the privates area
        {
            _listView.SelectedIndices.Clear();
            if (_useVirtualMode)
            {
                _listView.VirtualListSize = 0;
            }
            else
            {
                _listView.Items.Clear();
            }
            _virtualItems.Clear();
            _itemCache.Clear();
            _pathToIndex.Clear();
            _itemIndex.Clear();
        }

        /// <summary>
        /// Gets the <see cref="CShellItem"/> representing the currently selected folder in the tree or the folder being viewed.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current CSI of ExpFileList"),
         DefaultValue("")]
        public CShellItem SelectedItem => _selectedItem;

        /// <summary>
        /// Gets the <see cref="CShellItem"/> representing the currently loaded/displayed folder.
        /// </summary>
        public CShellItem CurrentFolderCsi => _currentFolderCsi;

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
        public int Count => _useVirtualMode ? _virtualItems.Count : _listView.Items.Count;

        /// <summary>
        /// Gets the number of selected items in the list view.
        /// </summary>
        [Browsable(false)]
        public int SelectedCount => _listView.SelectedIndices.Count;

        /// <summary>
        /// Gets the indices of the selected items.
        /// </summary>
        [Browsable(false)]
        public ListView.SelectedIndexCollection SelectedIndices => _listView.SelectedIndices;

        /// <summary>
        /// Gets an enumerable collection of selected CShellItems.
        /// This is more efficient than using SelectedItems in virtual mode.
        /// </summary>
        [Browsable(false)]
        public IEnumerable<CShellItem> SelectedCShellItems
        {
            get
            {
                foreach (int index in _listView.SelectedIndices)
                {
                    var item = GetItem(index);
                    if (item != null) yield return item;
                }
            }
        }

        /// <summary>
        /// Gets the CShellItem at the specified index.
        /// </summary>
        public CShellItem? GetItem(int index)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GetItem Begin");
            try
            {
                if (_useVirtualMode)
                {
                    if (index >= 0 && index < _virtualItems.Count)
                        return _virtualItems[index];
                }
                else
                {
                    if (index >= 0 && index < _listView.Items.Count)
                        return _listView.Items[index].Tag as CShellItem;
                }
                return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: GetItem End");
            }
        }

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public void RemoveAt(int index)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RemoveAt Begin");
            try
            {
                if (_useVirtualMode)
                {
                    if (index >= 0 && index < _virtualItems.Count)
                    {
                        _virtualItems.RemoveAt(index);
                        RecreateIndexMapping();
                        _itemCache.Clear();
                        _listView.VirtualListSize = _virtualItems.Count;
                        _listView.Invalidate();
                    }
                }
                else
                {
                    if (index >= 0 && index < _listView.Items.Count)
                    {
                        var lvi = _listView.Items[index];
                        if (lvi.Tag is CShellItem csi)
                            _itemIndex.Remove(csi.FullPath);
                        _listView.Items.RemoveAt(index);
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: RemoveAt End");
            }
        }

        /// <summary>
        /// Sets the sort column and order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSort(int column, SortOrder order)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: SetSort Begin");
            try
            {
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                    sorter.SetSort(column, order);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: SetSort End");
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
            System.Diagnostics.Debug.WriteLine("ExpList: ExpList Begin");
            try
            {
                InitializeComponent();

                // Initialize thumbnail timer for lazy loading
                _scrollDebounceTimer = new System.Windows.Forms.Timer();
                _scrollDebounceTimer.Interval = 200;
                _scrollDebounceTimer.Tick += (s, e) =>
                {
                    _scrollDebounceTimer.Stop();
                    LoadImagesForItems();
                };

                // Converted from Handles clauses in VB
                Load += ExpList_Load;
                VisibleChanged += ExpList_VisibleChanged;

                _listView.HandleCreated += ExpFileList_HandleCreated;
                _listView.Resize += (s, e) => OnListViewScroll();
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
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpList End");
            }
        }

        public void Initialize(ShellController shellController)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: Initialize Begin");
            try
            {
                _shellController = shellController;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: Initialize End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.Load"/> event of the <see cref="ExpList"/> control.
        /// Initializes drag and drop wrappers, thumbnail manager, and shell item update notifications.
        /// </summary>
        private void ExpList_Load(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpList_Load Begin");
            try
            {
                // Setup Drag and Drop Wrappers
                DW = new CDragWrapper(_listView);
                DropWrap = new ClvDropWrapper(_listView);

                // Initialize Thumbnail Manager
                _thumbnailManager = new ThumbnailImageListManager(this);

                //create sorter
                var sorter = new LVColSorter(_listView);
                sorter.SortOrderChanged += (s, e) =>
                {
                    if (_useVirtualMode)
                    {
                        SortVirtualItems(sorter.SortColumn, sorter.OrderOfSort);
                    }
                    SortOrderChanged?.Invoke(this, EventArgs.Empty);
                    OnListViewScroll();
                };
                _listView.ListViewItemSorter = sorter;

                // Setup Change Notification
                CShellItemUpdater.UpdateEvent += UpdateInvoke;

                DisplayMode = (ListViewDisplayMode)_listView.View;

		          SetImageListForMode(DisplayMode);
		          //LoadImagesForItems();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpList_Load End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.HandleCreated"/> event of the <see cref="_listView"/> ListView.
        /// </summary>
        private void ExpFileList_HandleCreated(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_HandleCreated Begin");
            try
            {
                _scrollHook = new ListViewScrollHook(this, OnListViewScroll);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_HandleCreated End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.VisibleChanged"/> event of the <see cref="ExpList"/> control.
        /// Re-configures image lists for the current display mode when the control becomes visible.
        /// </summary>
        private void ExpList_VisibleChanged(object sender, EventArgs e) //occurs when the control become visible
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpList_VisibleChanged Begin");
            try
            {

            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpList_VisibleChanged End");
            }
        }


        /// <summary>
        /// Overrides <see cref="Control.WndProc(ref Message)"/> to handle shell context menu messages.
        /// </summary>
        /// <param name="m">The Windows <see cref="Message"/> to process.</param>
        protected override void WndProc(ref Message m)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: WndProc Begin");
            const int WM_QUERYENDSESSION = 0x0011;
            const int WM_ENDSESSION = 0x0016;
            const int WM_CLOSE = 0x0010;
            bool isShuttingDown = false;

            try
            {
                if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION || m.Msg == WM_CLOSE)
                {
                    isShuttingDown = true;
                }
                if (isShuttingDown) return;

                int hr;
                if (m.Msg == (int)WM.INITMENUPOPUP || m.Msg == (int)WM.MEASUREITEM || m.Msg == (int)WM.DRAWITEM)
                {
                    if (m_WindowsContextMenu.winMenu2 != null)
                    {
                        hr = m_WindowsContextMenu.winMenu2.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                        if (hr == 0) return;
                    }
                    else if ((m.Msg == (int)WM.INITMENUPOPUP && m.WParam == m_WindowsContextMenu.newMenuPtr)
                             || m.Msg == (int)WM.MEASUREITEM || m.Msg == (int)WM.DRAWITEM)
                    {
                        if (m_WindowsContextMenu.newMenu2 != null)
                        {
                            hr = m_WindowsContextMenu.newMenu2.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                            if (hr == 0) return;
                        }
                    }
                }
                else if (m.Msg == (int)WM.MENUCHAR)
                {
                    if (m_WindowsContextMenu.winMenu3 != null)
                    {
                        hr = m_WindowsContextMenu.winMenu3.HandleMenuMsg2(m.Msg, m.WParam, m.LParam, IntPtr.Zero);
                        if (hr == 0) return;
                    }
                }

                base.WndProc(ref m);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: WndProc End");
            }
        }


        #endregion


        #region ExplorerTree Event Handling -- AfterNodeSelect

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="pathName">The display path of the folder.</param>
        /// <param name="csi">The <see cref="CShellItem"/> representing the folder to display.</param>
        /// <param name="includeFolder">True to include subdirectories in the list.</param>
        /// <param name="reload">True to force a reload even if the same item was previously selected.</param>
        public void DisplayFiles(string pathName, CShellItem csi, bool includeFolder, bool reload = false)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: DisplayFiles Begin");
            try
            {
                if (csi == null && !string.IsNullOrEmpty(pathName))
                    csi = CShellItemFactory.CreateCShItem(pathName);

                if (csi is null)
                {
                    _listView.BeginUpdate();
                    ClearListInternal();
                    var oldCsi_null = _currentFolderCsi;
                    _currentFolderCsi = null;
                    _currentPath = pathName;
                    ExpListCurrentFolderChanged?.Invoke(null, oldCsi_null);
                    _listView.EndUpdate();
                    return;
                }

                bool samePath;
                if (_currentFolderCsi is null)
                    samePath = false;
                else
                    samePath = CPidl.IsEqual(_currentFolderCsi.PIDL, csi.PIDL);

                if (_currentFolderCsi != null && samePath && reload == false) return;

                var oldCsi = _currentFolderCsi;
                var hierarchyCsi = _shellController.LoadFolderContents(csi);
                if (hierarchyCsi != null)
                {
                    _currentFolderCsi = hierarchyCsi;
                }
                else
                {
                    // If loading fails, clear the list instead of throwing
                    _listView.BeginUpdate();
                    ClearListInternal();
                    _currentFolderCsi = null;
                    _currentPath = pathName;
                    ExpListCurrentFolderChanged?.Invoke(null, oldCsi);
                    _listView.EndUpdate();
                    return;
                }

                // record history
                if (!_isNavigatingHistory && _currentFolderCsi != null && !samePath)
                {
                    _backHistory.Push(_currentFolderCsi);
                    _forwardHistory.Clear();
                }

                _selectedItem = null; //new folder loaded, no item selected yet
                _currentPath = _currentFolderCsi.FullPath;

                //display directories separately
                var dirList = new List<CShellItem>();
                var fileList = new List<CShellItem>();
                if (includeFolder) dirList.AddRange(_currentFolderCsi.Directories);

                if (!csi.DisplayName.Equals(CShellItemFactory.StrMyComputer)) fileList.AddRange(_currentFolderCsi.Files);

                if ((dirList.Count + fileList.Count) == 0) //no items
                {
                    _listView.BeginUpdate();
                    ClearListInternal();
                    _listView.EndUpdate();

                    if (!samePath) ExpListCurrentFolderChanged?.Invoke(_currentFolderCsi, oldCsi);

                    return;
                }
                else
                {
                    int totalItems;

                    Console.WriteLine("\tSorting...");
                    fileList.Sort();
                    totalItems = fileList.Count;
                    if (includeFolder)
                    {
                        dirList.Sort();
                        totalItems += dirList.Count;
                    }
                    Console.WriteLine("\tSorting done");

                    var combinedList = new List<CShellItem>(totalItems);
                    if (includeFolder) combinedList.AddRange(dirList);
                    combinedList.AddRange(fileList);

                    if (_useVirtualMode)
                    {
                        _virtualItems = combinedList;
                        RecreateIndexMapping();
                        _itemCache.Clear();
                        _listView.VirtualListSize = _virtualItems.Count;
                        _listView.Tag = _currentFolderCsi;
                        // Removed LoadVisibleIcons call: the 200ms debounce timer started by 
                        // OnListViewScroll will handle the initial load correctly after layout.
                    }
                    else
                    {
                        int initialFillLim = Math.Min(combinedList.Count, InitialLoadLimit);
                        var combinedLvi = new List<ListViewItem>(combinedList.Count);
                        int topIndex = GetTopIndex();

                        Console.WriteLine("\tMaking ListViewItems...");
                        _itemIndex.Clear();
                        foreach (CShellItem item in combinedList)
                        {
                            ListViewItem lvi = MakeLVItem(item);
                            if (!_itemIndex.TryAdd(item.FullPath, lvi))
                            {
                                _itemIndex[item.FullPath] = lvi;
                            }

                            combinedLvi.Add(lvi);
                        }
                        Console.WriteLine("\tDone making ListViewItems.");

                        _listView.Tag = _currentFolderCsi;
                        if (!RequestListViewRepopulate(combinedLvi.ToArray(), !samePath)) return;
                    }
                }

                OnListViewScroll(); //this lazy loads the visible icons/thumbnails and is called here to ensure they are loaded on initial display

                if (!samePath) ExpListCurrentFolderChanged?.Invoke(_currentFolderCsi, oldCsi);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: DisplayFiles End");
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
            System.Diagnostics.Debug.WriteLine("ExpList: EnterListViewEnumeration Begin");
            try
            {
                _enumerationDepth++;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: EnterListViewEnumeration End");
            }
        }

        /// <summary>
        /// Decrements the enumeration depth counter. When it reaches 0, any deferred
        /// shell item updates are drained and applied.
        /// Must be paired with <see cref="EnterListViewEnumeration"/>.
        /// </summary>
        private void ExitListViewEnumeration()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExitListViewEnumeration Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: ExitListViewEnumeration End");
            }
        }

        /// <summary>
        /// Processes all deferred shell item updates that were queued while an enumeration was in progress.
        /// </summary>
        private void DrainDeferredUpdates()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: DrainDeferredUpdates Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: DrainDeferredUpdates End");
            }
        }

        /// <summary>
        /// Executes the action immediately if no enumeration is in progress, otherwise
        /// defers it via BeginInvoke to run after the enumeration completes.
        /// Use this for ListView modification operations outside of DoItemUpdate.
        /// </summary>
        private void InvokeWhenListViewReady(Action action)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: InvokeWhenListViewReady Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: InvokeWhenListViewReady End");
            }
        }

        /// <summary>
        /// This refreshes the ListView with new items.
        /// This function marshals execution to the ui thread.  Also prevents double updating by gatekeeping 
        /// execution via the _refreshing boolean.  Without these precautions, we got errors with array index 
        /// out of bounds errors on the listview items that couldn't be resolved with only a lock. 
        /// </summary>
        /// <param name="newItems"></param>
        /// <returns></returns>
        private bool RequestListViewRepopulate(ListViewItem[] newItems, bool fetchImages)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RequestListViewRefresh Begin");
            try
            {
                if (_refreshing)
                {
                    _refreshPending = true;
                    return false;
                }

                // Snapshot now (avoid deferred enumeration / later mutation)
                _pendingItems = newItems;
                _refetchImages = fetchImages;
                _refreshing = true;

                BeginInvoke(new MethodInvoker(RepopulateListViewCore)); // queue, don't run inline.  Can't take arguments because of MethodInvoker unfortunately

                return true;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: RequestListViewRefresh End");
            }
        }

        private void RepopulateListViewCore()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RefreshListViewCore Begin");
            try
            {
                // If an enumeration is in progress (reentrancy via message pumping),
                // defer this refresh to after the enumeration completes.
                if (_enumerationDepth > 0)
                {
                    BeginInvoke(new MethodInvoker(RepopulateListViewCore));
                    return;
                }

                try
                {
                    // snapshot old position safely
                    int topIndex = 0;
                    if (_listView.Items.Count > 0)
                    {
                        topIndex = GetTopIndex();
                    }

                    var newItems = _pendingItems ?? Array.Empty<ListViewItem>();

                    Console.WriteLine("Begin loading items into listview...");
                    _listView.BeginUpdate();
                    try
                    {
                        _itemIndex.Clear();

                        if (_useVirtualMode)
                        {
                            int count = newItems == null ? 0 : newItems.Length;

                            _listView.VirtualListSize = count;
                            _listView.Tag = _currentFolderCsi;
                            _listView.Refresh();
                        }
                        else
                        {
                            _listView.Items.Clear();
                            _listView.Items.AddRange(newItems);

                            if (_listView.Items.Count > 0)
                            {
                                _listView.Tag = _currentFolderCsi; // For ClvDropWrapper

                                topIndex = Math.Max(0, Math.Min(topIndex, _listView.Items.Count - 1));
                                _listView.EnsureVisible(topIndex);

                                if (_refetchImages) LoadImagesForItems();
                            }
                        }
                    }
                    finally
                    {
                        _listView.EndUpdate();
                    }
                    Console.WriteLine("End loading items into listview");
                }
                finally
                {
                    _refreshing = false;
                    _refreshPending = false;
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: RefreshListViewCore End");
            }
        }

        #endregion


        #region Public Methods

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
            if (_pathToIndex.TryGetValue(fullPath, out int index))
                return index;
            return -1;
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a <see cref="ListViewItem"/> for a given <see cref="CShellItem"/>.
        /// Populates columns based on <see cref="ExpListGetColumnData"/> event or <see cref="ColumnHeader.Tag"/> mapping.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to create the list view item for.</param>
        /// <returns>A configured <see cref="ListViewItem"/>.</returns>
        private ListViewItem MakeLVItem(CShellItem item)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: MakeLVItem Begin");
            try
            {
                if (item == null) return new ListViewItem("Error: no CShellItem provided to MakeLVItem()");

                ListViewItem lvi = new ListViewItem(item.DisplayName);

                UpdateLviUsingCsi(lvi, item);

                return lvi;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: MakeLVItem End");
            }
        }
        #endregion

        #region Dynamic Update Handler

        private delegate void InvokeUpdate(object sender, ShellItemUpdateEventArgs e);

        /// <summary>
        /// Exposes the SelectedItems collection of the internal ListView to allow external handlers to access the currently selected items.
        /// </summary>
        public ListView.SelectedListViewItemCollection SelectedItems => _listView.SelectedItems;


        /// <summary>
        /// Finds the <see cref="ListViewItem"/> corresponding to a specific <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to search for.</param>
        /// <returns>The matching <see cref="ListViewItem"/>, or null if not found.</returns>
        private ListViewItem? FindLVItem(CShellItem item)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindLVItem Begin");
            try
            {
                if (_itemIndex.TryGetValue(item.FullPath, out var lvi))
                    return lvi;
                return null;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: FindLVItem End");
            }
        }

        /// <summary>
        /// Inserts a <see cref="ListViewItem"/> into the ListView, maintaining sort order.
        /// </summary>
        /// <param name="lvi">The <see cref="ListViewItem"/> to insert.</param>
        /// <param name="lv">The <see cref="ListView"/> to insert into.</param>
        private void InsertLvi(ListViewItem lvi, ListView lv)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: InsertLvi Begin");
            try
            {
                var item = (CShellItem)lvi.Tag;
                for (int i = 0; i < lv.Items.Count; i++)
                {
                    if (((CShellItem)lv.Items[i].Tag).CompareTo(item) > 0)
                    {
                        lv.Items.Insert(i, lvi);
                        _itemIndex[item.FullPath] = lvi;
                        lvi.EnsureVisible();
                        return;
                    }
                }
                lv.Items.Add(lvi);
                _itemIndex[item.FullPath] = lvi;
                lvi.EnsureVisible();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: InsertLvi End");
            }
        }

        /// <summary>
        /// Marshals shell item update events to the UI thread.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void UpdateInvoke(object sender, ShellItemUpdateEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: UpdateInvoke Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: UpdateInvoke End");
            }
        }

        private void InsertVirtualItem(CShellItem item)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: InsertVirtualItem Begin");
            try
            {
                _virtualItems.Add(item);
                _listView.VirtualListSize = _virtualItems.Count;

                if (_listView.ListViewItemSorter is LVColSorter sorter && sorter.OrderOfSort != SortOrder.None)
                {
                    SortVirtualItems(sorter.SortColumn, sorter.OrderOfSort);
                }
                else
                {
                    _virtualItems.Sort();
                    RecreateIndexMapping();
                    _itemCache.Clear();
                    _listView.Invalidate();
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: InsertVirtualItem End");
            }
        }

        /// <summary>
        /// Performs the actual update of list view items in response to shell changes.
        /// Handles creation, deletion, renaming, and other updates of files and folders.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void DoItemUpdate(object sender, ShellItemUpdateEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: DoItemUpdate Begin");

            try
            {
                if (sender is null || _currentFolderCsi == null) return;

                // If an enumeration is in progress, defer this update to prevent reentrant
                // mutation of _listView.Items (which causes null items during foreach).
                if (_enumerationDepth > 0)
                {
                    _deferredUpdates.Enqueue((sender, e));
                    return;
                }

                var senderCsi = (CShellItem)sender;

                // For Created/Deleted/UpdateDir, sender is the Folder containing the item.
                // For Updated/Renamed/IconChange, sender is the Item itself.
                bool isTargetFolder = CPidl.IsEqual(senderCsi.PIDL, _currentFolderCsi.PIDL);
                bool isTargetItem = senderCsi.Parent != null && CPidl.IsEqual(senderCsi.Parent.PIDL, _currentFolderCsi.PIDL);

                if (!isTargetFolder && !isTargetItem) return;

                try
                {
                    switch (e.UpdateType)
                    {
                        case CShItemUpdateType.Created:
                            {
                                if (!isTargetFolder) return;

                                if (_useVirtualMode)
                                {
                                    InsertVirtualItem(e.Item);
                                }
                                else
                                {
                                    var lvi = MakeLVItem(e.Item);
                                    InsertLvi(lvi, _listView);
                                    m_CreateNew = false; //finished create new handling.  I don't think this is even used?
                                }

                                break;
                            }

                        case CShItemUpdateType.Deleted:
                            {
                                if (e.Item is null)
                                {
                                    Debug.WriteLine("ExpList received DELETED event but no item was specified.");
                                    return;
                                }

                                if (_useVirtualMode)
                                {
                                    if (_pathToIndex.TryGetValue(e.Item.FullPath, out int index))
                                    {
                                        _listView.SelectedIndices.Clear();
                                        RemoveAt(index);
                                    }
                                }
                                else
                                {
                                    //var lvi = FindLVItem(e.Item);
                                    var lvi = e.Item.LVItem;
                                    if (lvi == null) //deletion messages get sent twice.  The second time the item has an index of -1
                                    {
                                        lvi = FindLVItem(e.Item);
                                    }

                                    if (lvi != null && lvi.Index >= 0) //deletion messages get sent twice.  The second time the item has an index of -1
                                    {
                                        int index = lvi.Index;
                                        bool wasSelected = lvi.Selected;
                                        _itemIndex.Remove(e.Item.FullPath);
                                        try
                                        {
                                            _listView.Items.Remove(lvi);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine("Exception while removing item from listview.  " + ex.ToString());
                                        }
                                        if (wasSelected && _listView.SelectedItems.Count == 0 && _listView.Items.Count > 0)
                                        {
                                            int nextIndex = Math.Min(index, _listView.Items.Count - 1);
                                            _listView.Items[nextIndex].Selected = true;
                                            _listView.Items[nextIndex].Focused = true;
                                        }
                                    }
                                }

                                break;
                            }

                        case CShItemUpdateType.Renamed:
                            {
                                if (_useVirtualMode)
                                {
                                    // Re-load and re-sort
                                    DisplayFiles(_currentPath, _currentFolderCsi, true, reload: true);
                                }
                                else
                                {
                                    // On Rename, we must find the item by its tag (CShellItem) because the path has changed.
                                    // However, we can use the lvi.Name which stores the OLD path used in our dictionary.
                                    var lvi = e.Item.LVItem;
                                    if (lvi == null || !ReferenceEquals(lvi.Tag, e.Item))
                                    {
                                        lvi = _itemIndex.Values.FirstOrDefault(x => ReferenceEquals(x.Tag, e.Item));
                                    }

                                    if (lvi != null)
                                    {
                                        _itemIndex.Remove(lvi.Name); // Remove old path key
                                        if (!ReferenceEquals(e.Item.Parent, _currentFolderCsi))
                                        {
                                            _listView.Items.Remove(lvi);
                                        }
                                        else
                                        {
                                            lvi.Text = e.Item.DisplayName;
                                            lvi.Name = e.Item.FullPath; // Update lvi.Name to NEW path
                                            lvi.ImageIndex = ((CShellItem)e.Item).IconIndexNormal;
                                            _listView.Items.Remove(lvi);
                                            InsertLvi(lvi, _listView); // InsertLvi will add NEW path to index
                                        }
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.UpdateDir:
                            DisplayFiles(_currentPath, _currentFolderCsi, true, reload: true);
                            break;

                        case CShItemUpdateType.Updated:
                            {
                                if (_useVirtualMode)
                                {
                                    if (_pathToIndex.TryGetValue(e.Item.FullPath, out int index))
                                    {
                                        _itemCache.Remove(index);
                                        _listView.RedrawItems(index, index, false);
                                    }
                                }
                                else
                                {
                                    var lvi = FindLVItem(e.Item);
                                    if (lvi != null)
                                    {
                                        UpdateLviUsingCsi(lvi, e.Item);
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.IconChange:
                            {
                                if (_useVirtualMode)
                                {
                                    if (_pathToIndex.TryGetValue(e.Item.FullPath, out int index))
                                    {
                                        _itemCache.Remove(index);
                                        if (IsThumbnailViewMode())
                                            _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode(), index);
                                        else
                                            _listView.RedrawItems(index, index, false);
                                    }
                                }
                                else
                                {
                                    var lvi = FindLVItem(e.Item);
                                    if (lvi != null)
                                    {
                                        if (IsThumbnailViewMode())
                                            _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode());
                                        else
                                            lvi.ImageIndex = ((CShellItem)e.Item).IconIndexNormal;
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.MediaChange:
                            {
                                if (_useVirtualMode)
                                {
                                    if (_pathToIndex.TryGetValue(e.Item.FullPath, out int index))
                                    {
                                        _itemCache.Remove(index);
                                        if (IsThumbnailViewMode())
                                            _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode(), index);
                                        else
                                            _listView.RedrawItems(index, index, false);
                                    }
                                }
                                else
                                {
                                    var lvi = FindLVItem(e.Item);
                                    if (lvi != null)
                                    {
                                        lvi.Text = e.Item.DisplayName;
                                        if (IsThumbnailViewMode())
                                            _thumbnailManager.RequestThumbnail(e.Item, GetThumbnailSizeForMode());
                                        else lvi.ImageIndex = ((CShellItem)e.Item).IconIndexNormal;
                                    }
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
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: DoItemUpdate End");
            }
        }

        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: RetrieveVirtualItem Begin: " + e.ItemIndex.ToString() + ", " + DateTime.Now.ToString("mm:ss.fff"));
            try
            {
                e.Item = GetItemInternal(e.ItemIndex);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: RetrieveVirtualItem End " + DateTime.Now.ToString("mm:ss.fff"));
            }
        }

        private ListViewItem? GetItemInternal(int index)
        {
            if (index < 0 || index >= _virtualItems.Count) return null;

            if (_itemCache.TryGetValue(index, out var lvi))
            {
                var csi = _virtualItems[index];

                // Sync ImageIndex if it was updated in the background while item was cached
                if (IsThumbnailViewMode() && lvi.ImageIndex == -1)
                {
                    int thumbIndex = _thumbnailManager.GetThumbnailIndex(csi.FullPath, GetThumbnailSizeForMode());
                    lvi.ImageIndex = thumbIndex;
                }

                if (DisplayMode == ListViewDisplayMode.Details && (csi.ColumnDic == null || csi.ColumnDic.Count == 0))
                {
                    PopulateColumnData(lvi, csi);
                }
                return lvi;
            }

            var item = _virtualItems[index];
            lvi = MakeLVItem(item);

            _itemCache[index] = lvi;
            return lvi;
        }


        /// <summary>
        /// Clears and recreates the entire mapping of item paths to their indices in the virtual list. 
        /// This is necessary after any operation that can change the order of items, such as sorting or bulk updates.
        /// This is an expensive operation (O(n)) and should be used judiciously.
        /// Do not use this function when only a small number of inserts and deletes are made
        /// , as it will be more efficient to update the index mapping incrementally in those cases..
        /// </summary>
        private void RecreateIndexMapping()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: UpdateIndexMapping Begin");
            try
            {
                _pathToIndex.Clear();
                for (int i = 0; i < _virtualItems.Count; i++)
                {
                    _pathToIndex[_virtualItems[i].FullPath] = i;
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: UpdateIndexMapping End");
            }
        }

        /// <summary>
        /// Refreshes the list view item associated with data from the given shell item.
        /// </summary>
        /// <param name="csi">The shell item whose corresponding list view item will be refreshed. Cannot be null.</param>
        public void UpdateLviUsingCsi(CShellItem csi)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: UpdateLviUsingCsi Begin");
            try
            {
                if (csi == null) return;

                if (!_pathToIndex.TryGetValue(csi.FullPath, out int index))
                {
                    Debug.WriteLine("ExpList: UpdateLviUsingCsi - item not found in index for path: " + csi.FullPath);
                    return;
                }

                if (_useVirtualMode)
                {
                    {
                        var lvi = GetItemInternal(index);
                        UpdateLviUsingCsi(lvi, csi); //ensure the columns have been populated because they are not in some other view modes
                    }

                    _listView.RedrawItems(index, index, false);
                }
                else
                {
                    var lvi = FindLVItem(csi);
                    if (lvi == null) return;

                    UpdateLviUsingCsi(lvi, csi);
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: UpdateLviUsingCsi End");
            }
        }

        /// <summary>
        /// Refreshes the display of a single item whose underlying filesystem data has changed.
        /// </summary>
        public void UpdateLviUsingCsi(ListViewItem lvi, CShellItem item)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: UpdateLviUsingCsi Begin");
            try
            {
                if (lvi == null || item == null) return;

                // Update primary text
                lvi.Text = item.DisplayName;
                lvi.Name = item.FullPath;
                lvi.Tag = item;
                item.LVItem = lvi;

                if (DisplayMode == ListViewDisplayMode.Details)
                {
                    PopulateColumnData(lvi, item);
                }
                else if (IsThumbnailViewMode())
                {
                    int index = _thumbnailManager.GetThumbnailIndex(item.FullPath, GetThumbnailSizeForMode());
                    lvi.ImageIndex = index;
                }
                else
                    lvi.ImageIndex = SystemImageListManager.GetIconIndex(item, DisplayMode == ListViewDisplayMode.LargeIcon);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: UpdateLviUsingCsi End");
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

        private void EnsureColumnDataFetched(CShellItem item)
        {
            if (ExpListGetColumnData == null) return;
            if (item.ColumnDic.ContainsKey("__BulkEventFired")) return;

            var args = new ExpListGetColumnDataEventArgs(item);
            ExpListGetColumnData(this, args);
            item.ColumnDic["__BulkEventFired"] = ListViewSubitemData.Default;

            foreach (ColumnHeader col in _listView.Columns)
            {
                if (args.ColumnData.TryGetValue(col.Text, out var value))
                {
                    item.ColumnDic[col.Name] = value;
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
        private ListViewSubitemData GetColumnData(CShellItem item, ColumnHeader col)
        {
            //Debug.WriteLine("ExpList: GetColumnData Begin");
            try
            {
                string text = string.Empty;
                object? tag = null;

                if (item.ColumnDic.TryGetValue(col.Name, out ListViewSubitemData propInfo)) //maybe it was already fetched before
                    return propInfo;

                // 1. Try Tag Mapping
                string mapping = col.Tag?.ToString().Trim();
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

                    // Fallback to reflection for other properties
                    if (mapping.StartsWith(".Tag")) //get the value from one of the fields within the custom Tag object property
                    {
                        if (item.Tag != null)
                        {
                            string fieldName = mapping.Substring(4);
                            if (string.IsNullOrEmpty(fieldName)) new ListViewSubitemData(string.Empty, null);

                            Type tagType = item.Tag.GetType();
                            FieldInfo field = tagType.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase); //todo: cache these reflection access objects
                            if (field != null)
                            {
                                object val = field.GetValue(item.Tag);
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
                
                if (col.Index == 0)
                {
                    text = item.DisplayName;
                }
                else
                {
                    // 2. Try bulk fetch if still not found
                    EnsureColumnDataFetched(item);
                    if (item.ColumnDic.TryGetValue(col.Name, out propInfo))
                        return propInfo;
                }

            END:
                var result = new ListViewSubitemData(text, tag);

                item.ColumnDic.TryAdd(col.Name, result); //save for future use

                return result;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: GetColumnData End");
            }
        }

        /// <summary>
        /// Refresh by display name string.  This is very inefficient.  Avoid this function.
        /// </summary>
        public ListViewItem? RefreshItemByDisplayName(string fileName)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RefreshItemByDisplayName Begin");
            try
            {
                if (_useVirtualMode)
                {
                    for (int i = 0; i < _virtualItems.Count; i++)
                    {
                        if (string.Equals(_virtualItems[i].DisplayName, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            _itemCache.Remove(i);
                            _listView.RedrawItems(i, i, false);
                            return GetItemInternal(i);
                        }
                    }
                    return null;
                }

                // Try to find the item by its display name using the index values
                var key = _itemIndex.Keys.FirstOrDefault(k => k.EndsWith(Path.DirectorySeparatorChar + fileName));

                if (key is not null)
                {
                    var lvi = _itemIndex[key];
                    if (lvi is null) return null;

                    if (lvi.Tag is CShellItem csi)
                        UpdateLviUsingCsi(lvi, csi);

                    return lvi;
                }
                else return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: RefreshItemByDisplayName End");
            }
        }

        public ListViewItem? RefreshItemByFullPath(string path)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RefreshItemByFullPath Begin");
            try
            {
                if (_useVirtualMode)
                {
                    if (_pathToIndex.TryGetValue(path, out int index))
                    {
                        var csi = _virtualItems[index]; //crash when index == count+1.  it's possible for _pathToIndex to become descynchronized from _virtualItems
                        csi.ColumnDic.Clear();
                        _itemCache.Remove(index);
                        _listView.RedrawItems(index, index, false);
                        return GetItemInternal(index);
                    }
                    return null;
                }

                if (_itemIndex.TryGetValue(path, out ListViewItem? lvi))
                {
                    if (lvi is null) return null;

                    if (lvi.Tag is CShellItem csi)
                    {
                        csi.ColumnDic.Clear();
                        UpdateLviUsingCsi(lvi, csi);
                    }

                    return lvi;
                }
                else return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: RefreshItemByFullPath End");
            }
        }

        public ListViewItem? RefreshItem(CShellItem? item)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: RefreshItem Begin");
            try
            {
                if (item is null) return null;

                item.ColumnDic.Clear();

                if (_useVirtualMode)
                {
                    if (_pathToIndex.TryGetValue(item.FullPath, out int index))
                    {
                        _itemCache.Remove(index);
                        _listView.RedrawItems(index, index, false);
                        return GetItemInternal(index);
                    }
                    return null;
                }

                if (_itemIndex.TryGetValue(item.FullPath, out ListViewItem? lvi))
                {
                    if (lvi is null) return null;

                    UpdateLviUsingCsi(lvi, item);

                    return lvi;
                }
                else return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: RefreshItem End");
            }
        }

        #endregion


        #region Navigation

        /// <summary>
        /// Navigates back to the previous folder in the history.
        /// </summary>
        public void GoBack()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GoBack Begin");
            try
            {
                if (_backHistory.Count > 0)
                {
                    _forwardHistory.Push(_currentFolderCsi);
                    var prev = _backHistory.Pop();
                    _isNavigatingHistory = true;
                    try
                    {
                        DisplayFiles(prev.FullPath, prev, true);
                    }
                    finally
                    {
                        _isNavigatingHistory = false;
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GoBack End");
            }
        }

        /// <summary>
        /// Navigates forward to the next folder in the history.
        /// </summary>
        public void GoForward()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GoForward Begin");
            try
            {
                if (_forwardHistory.Count > 0)
                {
                    _backHistory.Push(_currentFolderCsi);
                    var next = _forwardHistory.Pop();
                    _isNavigatingHistory = true;
                    try
                    {
                        DisplayFiles(next.FullPath, next, true);
                    }
                    finally
                    {
                        _isNavigatingHistory = false;
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GoForward End");
            }
        }

        /// <summary>
        /// Navigates to the parent folder of the currently loaded folder.
        /// </summary>
        public void GoUp()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GoUp Begin");
            try
            {
                if (_currentFolderCsi?.Parent != null)
                {
                    var parent = _currentFolderCsi.Parent;
                    DisplayFiles(parent.FullPath, parent, true);
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GoUp End");
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


        private void ExpFileList_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_Click Begin");
            try
            {
                ListView listView = (ListView)sender;

                if (listView.SelectedIndices.Count == 0) return;

                var csi = GetItem(listView.SelectedIndices[0]);
                if (csi == null) return;

                _selectedItem = csi; // ← keep in sync

                if (csi.IsFileSystem)
                {
                    ExpListItemClick?.Invoke(csi);
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_Click End");
            }
        }

        /// <summary>
        /// Handles double-click events on list view items. 
        /// Folders are navigated into, while files are launched.
        /// </summary>
        private void ExpFileList_DoubleClick(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_DoubleClick Begin");
            try
            {
                if (_listView.SelectedIndices.Count <= 0) return;

                var csi = GetItem(_listView.SelectedIndices[0]);
                if (csi == null) return;

                if (csi.IsFolder)
                {
                    if (csi.FullPath.StartsWith(":"))
                        ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                    else
                        ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);
                }
                else
                {
                    try
                    {
                        LaunchFile(csi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error in starting application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_DoubleClick End");
            }
        }

        private void ExpFileList_SelectedIndexChanged(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged Begin");
            try
            {
                try
                {
                    if (_listView.SelectedIndices.Count > 0)
                    {
                        _selectedItem = GetItem(_listView.SelectedIndices[0]);
                    }
                    else
                    {
                        _selectedItem = null;
                    }

                    if (_useVirtualMode)
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
                catch (InvalidOperationException) { }
                catch (NullReferenceException) { }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged End");
            }
        }

        private void ExpFileList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged Begin");
            try
            {
                ItemSelectionChanged?.Invoke(e);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged End");
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

        #region LabelEdit Handlers (Item Rename)

        /// <summary>
        /// Handles the <see cref="ListView.BeforeLabelEdit"/> event.
        /// Determines if an item can be renamed and sets up the edit control.
        /// </summary>
        private void ExpFileList_BeforeLabelEdit(object sender, LabelEditEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit End");
            }
        }

        /// <summary>
        /// Handles the <see cref="ListView.AfterLabelEdit"/> event.
        /// Applies the new name to the shell item.
        /// </summary>
        private void ExpFileList_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit Begin");
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
                    if (item.Parent.Folder.SetNameOf(
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
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit End");
            }
        }

        #endregion

        #region Context Menu Handlers

        private readonly ExpControlsLib.ContextMenu m_WindowsContextMenu = new ExpControlsLib.ContextMenu();
        private bool m_OutOfRange;

        /// <summary>
        /// Determines if the mouse coordinates are within the client area of the specified control.
        /// </summary>
        /// <param name="ctl">The control to check.</param>
        /// <param name="e">The <see cref="MouseEventArgs"/> containing the mouse position.</param>
        /// <returns>True if the mouse is within the control's client area.</returns>
        private bool IsWithin(Control ctl, MouseEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: IsWithin Begin");
            try
            {
                if (e.X < 0 || e.Y < 0) return false;
                Rectangle cr = ctl.ClientRectangle;
                if (e.X > cr.Width || e.Y > cr.Height) return false;
                return true;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: IsWithin End");
            }
        }

        /// <summary>
        /// Sorts the items in the list view based on their tags (CShellItem).
        /// </summary>
        private void SortLVItems()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: SortLVItems Begin");
            try
            {
                if (_useVirtualMode)
                {
                    if (_listView.ListViewItemSorter is LVColSorter sorter)
                    {
                        SortVirtualItems(sorter.SortColumn, sorter.OrderOfSort);
                    }
                    return;
                }

                if (_listView.Items.Count < 2) return;

                EnterListViewEnumeration();
                try
                {
                    _listView.BeginUpdate();
                    var tmp = new ListViewItem[_listView.Items.Count];
                    _listView.Items.CopyTo(tmp, 0);
                    Array.Sort(tmp, new TagComparer());
                    _listView.Items.Clear();
                    _listView.Items.AddRange(tmp);
                    _listView.EndUpdate();
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: SortLVItems End");
            }
        }

        /// <summary>
        /// Handles the MouseLeave event to track when the mouse is outside the list view.
        /// </summary>
        private void ExpFileList_MouseLeave(object sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseLeave Begin");
            try
            {
                m_OutOfRange = true;
                OnMouseLeave(e);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseLeave End");
            }
        }

        private void ExpFileList_MouseEnter(object sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseEnter Begin");
            try
            {
                OnMouseEnter(e);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseEnter End");
            }
        }

        /// <summary>
        /// Handles the MouseDown event to reset the out-of-range flag for right-clicks.
        /// </summary>
        private void ExpFileList_MouseDown(object sender, MouseEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseDown Begin");
            try
            {
                if (e.Button == MouseButtons.Right) m_OutOfRange = false;
                OnMouseDown(e);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseDown End");
            }
        }

        /// <summary>
        /// Handles the MouseUp event to trigger context menus or middle-click actions.
        /// </summary>
        private void ExpFileList_MouseUp(object sender, MouseEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseUp Begin");
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
                            byte[] cmdBytes = new byte[256];
                            m_WindowsContextMenu.winMenu.GetCommandString(cmi.lpVerb.ToInt32(), (int)GCS.VERBA, 0, cmdBytes, 256);
                            string cmdName = SzToString(cmdBytes).ToLowerInvariant();

                            if (cmdName.Equals("rename"))
                            {
                                _listView.LabelEdit = true;
                                tn.BeginEdit();
                            }
                            else
                            {
                                string strPath = itms[0].Parent == ShellController.DesktopCSI
                                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                                    : itms[0].Parent.FullPath;

                                m_WindowsContextMenu.InvokeCommand(m_WindowsContextMenu.winMenu, (UInt32)cmi.lpVerb.ToInt32(), strPath, pt);
                            }

                            Marshal.ReleaseComObject(m_WindowsContextMenu.winMenu);
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
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi != null) ExpListItemMouseMBUp?.Invoke(csi.FullPath, csi);
                }
                OnMouseUp(e);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseUp End");
            }
        }

        private void ExpFileList_MouseMove(object sender, MouseEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseMove Begin");
            try
            {
                OnMouseMove(e);
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_MouseMove End");
            }
        }


        /// <summary>
        /// Creates a native Windows context menu for the current folder.
        /// </summary>
        /// <param name="comContextMenu">Output parameter for the main context menu handle.</param>
        /// <param name="viewSubMenu">Output parameter for the View submenu handle.</param>
        private void CreateContextMenu(out IntPtr comContextMenu, out IntPtr viewSubMenu, out IntPtr sortSubMenu)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: CreateContextMenu Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: CreateContextMenu End");
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
        private void ShowAndHandleContextMenu(Point pt)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: objects Begin");
            try
            {
                int HR;
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
                        nShow = (int)SW.SHOWNORMAL,
                        fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                        ptInvoke = new Point(pt.X, pt.Y)
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
                            _shellController.ShellUpdater.SelectiveFolderUpdate(_currentFolderCsi);
                            SortLVItems();
                            goto CLEANUP;
                        case CMD.SELECT_ALL:
                            // Select all items in the ListView.
                            if (_useVirtualMode)
                            {
                                _listView.BeginUpdate();
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
                            goto CLEANUP;
                        case CMD.PASTE:
                            if (_currentFolderCsi != null)
                            {
                                cmi.lpVerb = Marshal.StringToHGlobalAnsi("paste");
                                cmi.lpVerbW = Marshal.StringToHGlobalUni("paste");
                            }
                            else
                            {
                                goto CLEANUP;
                            }
                            break;
                        case CMD.PASTELINK:
                            cmi.lpVerb = Marshal.StringToHGlobalAnsi("pastelink");
                            cmi.lpVerbW = Marshal.StringToHGlobalUni("pastelink");
                            break;
                        case CMD.PROPERTIES:
                            cmi.lpVerb = Marshal.StringToHGlobalAnsi("properties");
                            cmi.lpVerbW = Marshal.StringToHGlobalUni("properties");
                            break;
                        default:
                            // Handle commands from the "New" submenu.
                            cmdID -= 1;
                            cmi.lpVerb = (IntPtr)cmdID;
                            cmi.lpVerbW = (IntPtr)cmdID;
                            m_CreateNew = true;
                            HR = m_WindowsContextMenu.newMenu.InvokeCommand(cmi);
#if DEBUG
                            if (HR != S_OK)
                                Marshal.ThrowExceptionForHR(HR);
#endif
                            goto CLEANUP;
                    }

                    if (_currentFolderCsi != null)
                    {
                        int prgf = 0;
                        IntPtr iunk = IntPtr.Zero;

                        IShellFolder folder = _currentFolderCsi == ShellController.DesktopCSI
                            ? _currentFolderCsi.Folder
                            : _currentFolderCsi.Parent.Folder;

                        IntPtr relPidl = CPidl.ILFindLastID(_currentFolderCsi.PIDL);

                        HR = folder.GetUIObjectOf(IntPtr.Zero, 1, new[] { relPidl }, IID_IContextMenu, prgf, out iunk);
#if DEBUG
                        if (HR != S_OK)
                            Marshal.ThrowExceptionForHR(HR);
#endif
                        m_WindowsContextMenu.winMenu = (IContextMenu)Marshal.GetObjectForIUnknown(iunk);

                        HR = m_WindowsContextMenu.winMenu.InvokeCommand(cmi);

                        m_WindowsContextMenu.ReleaseMenu();
#if DEBUG
                        if (HR != S_OK)
                            Marshal.ThrowExceptionForHR(HR);
#endif
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
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: objects End");
            }
        }
        #endregion

        #region Keyboard Events


        /// <summary>
        /// Handles KeyDown events for shortcuts (Ctrl+A, Ctrl+C/V/X, Delete, F2, F5, Enter).
        /// </summary>
        private void ExpFileList_KeyDown(object sender, KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyDown Begin");
            try
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    if (_useVirtualMode)
                    {
                        _listView.BeginUpdate();
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
                    if (_useVirtualMode)
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
                    _shellController.ShellUpdater.SelectiveFolderUpdate(_currentFolderCsi);
                    SortLVItems();
                }

                if (e.KeyCode == Keys.Enter && _listView.SelectedIndices.Count > 0)
                {
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi == null) return;

                    string name = csi.DisplayName;

                    if (csi.IsFolder)
                    {
                        if (csi.FullPath.StartsWith(":"))
                            ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                        else
                            ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);
                    }
                    else
                    {
                        string path = csi.FullPath;
                        try
                        {
                            if (name == Path.GetFileName(path))
                                LaunchFile(csi);
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
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyDown End");
            }
        }


        /// <summary>
        /// Handles the KeyUp event for navigation keys.
        /// </summary>
        private void ExpFileList_KeyUp(object sender, KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyUp Begin");
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
                    if (SelectedCount > 150)
                        _shellController.ShellUpdater.SelectiveFolderUpdate(_currentFolderCsi);
                }

                OnKeyUp(e);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyUp End");
            }
        }

        private void ExpFileList_KeyPress(object sender, KeyPressEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyPress Begin");
            try
            {
                OnKeyPress(e);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: ExpFileList_KeyPress End");
            }
        }

        /// <summary>
        /// Launches a file using the default system handler.
        /// </summary>
        /// <param name="csi">The <see cref="CShellItem"/> to launch.</param>
        private void LaunchFile(CShellItem csi)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: LaunchFile Begin");
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
                System.Diagnostics.Debug.WriteLine("ExpList: LaunchFile End");
            }
        }

        /// <summary>
        /// Invokes a standard shell action (cut, copy, paste, delete) on the selected items.
        /// </summary>
        /// <param name="cmd">The shell verb to invoke (e.g., "cut", "copy", "paste", "delete").</param>
        private void WinMenu(string cmd)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: WinMenu Begin");
            try
            {
                // Validate preconditions
                if (_currentFolderCsi == null || !_currentFolderCsi.IsFolder)
                {
                    return;
                }

                IntPtr rgfReserved = IntPtr.Zero;
                IntPtr iUnknownOut = IntPtr.Zero;
                IShellFolder? folder = null;
                IntPtr lpVerbAnsi = IntPtr.Zero;
                IntPtr lpVerbUni = IntPtr.Zero;
                List<IntPtr>? pidls = null;

                try
                {
                    if (cmd == "paste")
                    {
                        // Get the target folder for paste operation
                        try
                        {
                            folder = _currentFolderCsi == ShellController.DesktopCSI
                                ? _currentFolderCsi.Folder
                                : _currentFolderCsi.Parent?.Folder;

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
                            folder = _currentFolderCsi.Folder;
                            if (folder == null)
                            {
                                Debug.WriteLine("Failed to get folder interface for selected items");
                                MessageBox.Show("Cannot perform operation: folder interface is unavailable.", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }

                            var selectedItems = SelectedCShellItems.ToArray();
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

                                IntPtr pidl = CPidl.ILFindLastID(sel.PIDL);
                                if (pidl == IntPtr.Zero)
                                {
                                    Debug.WriteLine($"Failed to get PIDL for item: {sel.DisplayName}");
                                    MessageBox.Show($"Failed to get ID for item: {sel.DisplayName}", "Error",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    continue;
                                }

                                pidls.Add(pidl);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error preparing {cmd} operation: {ex.Message}");
                            MessageBox.Show($"Error preparing operation: {ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
#if DEBUG
                        var path = CPidl.ToString(pidls[0]);
#endif
                    }

#if DEBUG
                    var path2 = CPidl.ToString(pidls[0]);
#endif
                    // Get IContextMenu interface from the shell folder
                    if (pidls == null || pidls.Count == 0)
                    {
                        Debug.WriteLine("No items to process");
                        return;
                    }

                    try
                    {
                        int HR = folder.GetUIObjectOf(IntPtr.Zero, (uint)pidls.Count, pidls.ToArray(),
                            IID_IContextMenu, rgfReserved, out iUnknownOut);

                        if (HR != S_OK || iUnknownOut == IntPtr.Zero)
                        {
                            Debug.WriteLine($"GetUIObjectOf failed: HRESULT=0x{HR:X8}");
                            MessageBox.Show("Failed to get context menu interface from shell.", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Exception in GetUIObjectOf: {ex.Message}");
                        MessageBox.Show($"Error accessing shell interface: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Marshal the COM interface
                    try
                    {
                        m_WindowsContextMenu.winMenu = (IContextMenu)Marshal.GetObjectForIUnknown(iUnknownOut);
                        if (m_WindowsContextMenu.winMenu == null)
                        {
                            Debug.WriteLine("Failed to marshal IContextMenu interface");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to marshal IContextMenu: {ex.Message}");
                        return;
                    }

                    // Prepare command structure with allocated strings
                    try
                    {
                        lpVerbAnsi = Marshal.StringToHGlobalAnsi(cmd);
                        lpVerbUni = Marshal.StringToHGlobalUni(cmd);

                        var cmi = new CMInvokeCommandInfoEx
                        {
                            cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                            nShow = (int)SW.SHOWNORMAL,
                            fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                            ptInvoke = new Point(0, 0),
                            lpVerb = lpVerbAnsi,
                            lpVerbW = lpVerbUni
                        };

                        int topItemIndex = -1;
                        bool hasItems = _useVirtualMode ? _listView.VirtualListSize > 0 : _listView.Items.Count > 0;
                        if (cmd == "delete" && hasItems)
                        { //prevent null references from invalid selections that were deleted
                            topItemIndex = GetTopIndex();
                            _listView.SelectedIndices.Clear();
                        }
                        // Execute the shell command
                        int invokeHR = m_WindowsContextMenu.winMenu.InvokeCommand(cmi);

                        if (topItemIndex >= 0)
                        {
                            _listView.BeginInvoke(new Action(() =>
                            {
                                int count = _useVirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                                if (topItemIndex < count)
                                {
                                    if (_useVirtualMode)
                                        _listView.EnsureVisible(topItemIndex);
                                    else
                                        _listView.Items[topItemIndex].EnsureVisible();
                                }
                            }));
                        }

                        if (invokeHR != S_OK)
                        {
                            Debug.WriteLine($"InvokeCommand failed: HRESULT=0x{invokeHR:X8}, cmd='{cmd}'");
                            // Don't show error to user for most cases - shell handles UI
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error invoking command '{cmd}': {ex.Message}");
                        MessageBox.Show($"Error executing command: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        m_WindowsContextMenu.ReleaseMenu();

                        // Clean up allocated strings
                        if (lpVerbAnsi != IntPtr.Zero)
                            Marshal.FreeHGlobal(lpVerbAnsi);
                        if (lpVerbUni != IntPtr.Zero)
                            Marshal.FreeHGlobal(lpVerbUni);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unexpected error in WinMenu: {ex.Message}");
                    MessageBox.Show($"Unexpected error: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: WinMenu End");
            }
        }

        /// <summary>
        /// Determines if the current display mode is a thumbnail-based view.
        /// </summary>
        /// <returns>True if in a thumbnail view mode.</returns>
        private bool IsThumbnailViewMode() => DisplayMode == ListViewDisplayMode.Thumbnail || DisplayMode == ListViewDisplayMode.LargeThumbnail || DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail;

        #endregion

        #region Public Functions 


        /// <summary>
        /// Finds a ListViewItem by its display name (case-insensitive).
        /// </summary>
        public ListViewItem FindItemByName(string name)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindItemByName Begin");
            try
            {
                if (_useVirtualMode)
                {
                    for (int i = 0; i < _virtualItems.Count; i++)
                    {
                        if (string.Equals(_virtualItems[i].DisplayName, name, StringComparison.OrdinalIgnoreCase))
                            return GetItemInternal(i);
                    }
                    return null;
                }

                foreach (var lvi in _itemIndex.Values)
                {
                    if (string.Equals(lvi.Text, name, StringComparison.OrdinalIgnoreCase))
                        return lvi;
                }
                return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: FindItemByName End");
            }
        }

        /// <summary>
        /// Finds a ListViewItem by its Shell ID (PIDL).
        /// </summary>
        public ListViewItem FindItemByID(IntPtr pidl)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindItemByID Begin");
            try
            {
                if (_useVirtualMode)
                {
                    for (int i = 0; i < _virtualItems.Count; i++)
                    {
                        if (CPidl.IsEqual(_virtualItems[i].PIDL, pidl))
                            return GetItemInternal(i);
                    }
                    return null;
                }

                foreach (var lvi in _itemIndex.Values)
                {
                    if (lvi.Tag is CShellItem csi && CPidl.IsEqual(csi.PIDL, pidl))
                        return lvi;
                }
                return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: FindItemByID End");
            }
        }

        /// <summary>
        /// Finds a ListViewItem by its full filesystem path.
        /// </summary>
        public ListViewItem FindItemByPath(string path)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindItemByPath Begin");
            try
            {
                if (_useVirtualMode)
                {
                    if (_pathToIndex.TryGetValue(path, out int index))
                        return GetItemInternal(index);
                    return null;
                }

                if (_itemIndex.TryGetValue(path, out var lvi))
                    return lvi;
                return null;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: FindItemByPath End");
            }
        }


        private const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        private const int LVM_GETITEMRECT = LVM_FIRST + 14;
        private const int LVM_HITTEST = LVM_FIRST + 18;
        private const int LVM_GETITEMSPACING = LVM_FIRST + 51; // returns packed x/y in LPARAM
        private const int LVM_GETTOPINDEX = LVM_FIRST + 39;

        private const int LVNI_VISIBLE = 0x0008;
        private const int LVIR_BOUNDS = 0; // for LVM_GETITEMRECT
        private const int LVM_GETCOUNTPERPAGE = 0x1000 + 40;



        private int _lastTopIndex = -1;

        /// <summary>
        /// Returns a "top-like" index for any ListView mode.
        /// - Details/List: effectively top row index
        /// - LargeIcon/SmallIcon/Tile: top-left visible item index
        /// Works in virtual and non-virtual mode.
        /// </summary>
        public int GetTopIndex()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GetTopIndex Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated) return -1;

                int total = _listView.VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                if (total <= 0) return -1;

                if (_lastTopIndex > -1) return _lastTopIndex; // cache for repeated calls.  The OS will sometimes make tons of redundant calls

                int top = 0;
                if (!_listView.VirtualMode && _listView.TopItem != null)
                {
                    _lastTopIndex = _listView.TopItem.Index;
                    return _listView.TopItem.Index;
                }

                // 2) Try visible enumeration (works in many non-virtual cases)
                int byVisibleEnum = FindTopLeftByVisibleEnumeration(total);
                if (byVisibleEnum >= 0) return byVisibleEnum;

                // 3) Virtual-safe fallback: scan viewport by hit-test
                int byHitTestScan = FindTopLeftByHitTestScan(total);
                if (byHitTestScan >= 0) return byHitTestScan;

                // 4) Last fallback
                _lastTopIndex = (top >= 0 && top < total) ? top : -1;
                return _lastTopIndex;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GetTopIndex End");
            }
        }

        private int FindTopLeftByVisibleEnumeration(int total)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindTopLeftByVisibleEnumeration Begin");
            try
            {
                int bestIndex = -1;
                int bestTop = int.MaxValue;
                int bestLeft = int.MaxValue;

                int i = -1;
                while (true)
                {
                    i = (int)SendMessage(_listView.Handle, LVM_GETNEXTITEM, (IntPtr)i, (IntPtr)LVNI_VISIBLE);
                    if (i < 0) break;
                    if (i >= total) continue;

                    RECT rc = new RECT { left = LVIR_BOUNDS };
                    if (SendMessage(_listView.Handle, LVM_GETITEMRECT, (IntPtr)i, ref rc) == IntPtr.Zero)
                        continue;

                    if (rc.top < bestTop || (rc.top == bestTop && rc.left < bestLeft))
                    {
                        bestTop = rc.top;
                        bestLeft = rc.left;
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: FindTopLeftByVisibleEnumeration End");
            }
        }

        private int FindTopLeftByHitTestScan(int total)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: FindTopLeftByHitTestScan Begin");
            try
            {
                var client = _listView.ClientRectangle;
                if (client.Width <= 0 || client.Height <= 0) return -1;

                int step = Math.Max(6, _listView.Font.Height / 2);

                int bestIndex = -1;
                int bestTop = int.MaxValue;
                int bestLeft = int.MaxValue;

                for (int y = 0; y < client.Height; y += step)
                {
                    for (int x = 0; x < client.Width; x += step)
                    {
                        int idx = HitTestIndex(x, y);
                        if (idx < 0 || idx >= total) continue;

                        RECT rc = new RECT { left = LVIR_BOUNDS };
                        if (SendMessage(_listView.Handle, LVM_GETITEMRECT, (IntPtr)idx, ref rc) != IntPtr.Zero)
                        {
                            if (rc.top < bestTop || (rc.top == bestTop && rc.left < bestLeft))
                            {
                                bestTop = rc.top;
                                bestLeft = rc.left;
                                bestIndex = idx;
                            }
                        }
                        else
                        {
                            // fallback ordering if rect unavailable
                            if (y < bestTop || (y == bestTop && x < bestLeft))
                            {
                                bestTop = y;
                                bestLeft = x;
                                bestIndex = idx;
                            }
                        }
                    }
                }

                return bestIndex;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: FindTopLeftByHitTestScan End");
            }
        }

        private int HitTestIndex(int x, int y)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: HitTestIndex Begin");
            try
            {
                LVHITTESTINFO ht = new LVHITTESTINFO
                {
                    pt = new POINT { x = x, y = y }
                };

                int result = (int)SendMessage(_listView.Handle, LVM_HITTEST, IntPtr.Zero, ref ht);
                return result; // -1 if none
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("ExpList: HitTestIndex End");
            }
        }

        private int GetApproxVisibleCount()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GetApproxVisibleCount Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated)
                    return 0;

                return _listView.View == View.LargeIcon
                    ? GetApproxVisibleCountLargeIcon()
                    : GetAnyVisibleCount();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GetApproxVisibleCount End");
            }
        }

        private int GetAnyVisibleCount()
        {
            if (_listView == null || !_listView.IsHandleCreated || _listView.View == View.LargeIcon)
                return 0;

            int total = _listView.VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
            if (total <= 0) return 0;

            switch (_listView.View)
            {
                case View.Details:
                case View.List:
                    // LVM_GETCOUNTPERPAGE is geometry-based and works in virtual mode
                    int perPage = (int)SendMessage(_listView.Handle, LVM_GETCOUNTPERPAGE, IntPtr.Zero, IntPtr.Zero);
                    return Math.Min(total, Math.Max(0, perPage));

                case View.SmallIcon:
                case View.Tile:
                    // LVM_GETCOUNTPERPAGE returns total item count for these views, so use spacing math instead
                    return EstimateVisibleBySpacing(_listView, total, largeIcon: false);

                default:
                    return 0;
            }
        }

        private static int EstimateVisibleBySpacing(ListView lv, int total, bool largeIcon)
        {
            int packed = (int)SendMessage(lv.Handle, LVM_GETITEMSPACING,
                largeIcon ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);

            int cellW = packed & 0xFFFF;
            int cellH = (packed >> 16) & 0xFFFF;

            if (cellW <= 0 || cellH <= 0)
            {
                var img = (largeIcon ? lv.LargeImageList?.ImageSize : lv.SmallImageList?.ImageSize)
                          ?? new System.Drawing.Size(16, 16);
                cellW = Math.Max(1, img.Width + 16);
                cellH = Math.Max(1, img.Height + lv.Font.Height + 8);
            }

            int cols = Math.Max(1, (int)Math.Ceiling(lv.ClientSize.Width / (double)cellW));
            int rows = Math.Max(1, (int)Math.Ceiling(lv.ClientSize.Height / (double)cellH));

            return Math.Min(total, cols * rows);
        }

        //private int GetAnyVisibleCount()
        //{
        //    System.Diagnostics.Debug.WriteLine("ExpList: GetAnyVisibleCount Begin");
        //    try
        //    {
        //        if (_listView == null || !_listView.IsHandleCreated || _listView.View == View.LargeIcon)
        //            return 0;

        //        int total = _listView.VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
        //        if (total <= 0) return 0;

        //        Rectangle client = _listView.ClientRectangle;
        //        int count = 0;
        //        int i = -1;

        //        while (true)
        //        {
        //            i = (int)SendMessage(_listView.Handle, LVM_GETNEXTITEM, (IntPtr)i, (IntPtr)LVNI_VISIBLE); //always returns -1 in listview virtual mode
        //            if (i < 0) break;
        //            if (i >= total) continue;

        //            RECT rc = new RECT { left = LVIR_BOUNDS };
        //            if (SendMessage(_listView.Handle, LVM_GETITEMRECT, (IntPtr)i, ref rc) == IntPtr.Zero)
        //                continue;

        //            Rectangle itemRect = Rectangle.FromLTRB(rc.left, rc.top, rc.right, rc.bottom);
        //            if (itemRect.IntersectsWith(client))
        //                count++;
        //        }

        //        return count;
        //    }
        //    finally
        //    {
        //        System.Diagnostics.Debug.WriteLine("ExpList: GetAnyVisibleCount End");
        //    }
        //}

        private int GetApproxVisibleCountLargeIcon()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: GetApproxVisibleCountLargeIcon Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated || _listView.View != View.LargeIcon)
                    return 0;

                int total = _listView.VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                if (total <= 0) return 0;

                // FALSE => large icon spacing
                int packed = (int)SendMessage(_listView.Handle, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
                int cellW = packed & 0xFFFF;
                int cellH = (packed >> 16) & 0xFFFF;

                // Fallback if spacing couldn't be read
                if (cellW <= 0 || cellH <= 0)
                {
                    var img = _listView.LargeImageList?.ImageSize ?? new System.Drawing.Size(32, 32);
                    cellW = Math.Max(1, img.Width + 32);                   // rough label/padding allowance
                    cellH = Math.Max(1, img.Height + _listView.Font.Height * 2 + 16);
                }

                int vw = Math.Max(1, _listView.ClientSize.Width);
                int vh = Math.Max(1, _listView.ClientSize.Height);

                int cols = Math.Max(1, (int)Math.Ceiling(vw / (double)cellW));
                int rows = Math.Max(1, (int)Math.Ceiling(vh / (double)cellH));

                int approx = cols * rows;
                return Math.Min(total, approx);
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: GetApproxVisibleCountLargeIcon End");
            }
        }

        #endregion

        #region Lazy Thumbnail Loading Support

        //private bool _smallImageListInitialized = false;
        //private bool _largeImageListInitialized = false;
        //private bool _thumbnailImageListInitialized = false;
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
            System.Diagnostics.Debug.WriteLine("ExpList: SetAndLoadImageList Begin");
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
                        _thumbnailManager.SetImageListSize(GetThumbnailSizeForMode(value));
                    }
                    finally
                    {
                        ExitListViewEnumeration();
                    }
                }
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: SetAndLoadImageList End");
            }
        }

        private void LoadImagesForItems(ListViewDisplayMode? mode = null)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: LoadImagesForItems Begin");
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
                //System.Diagnostics.Debug.WriteLine("ExpList: LoadImagesForItems End");
            }
        }

        /// <summary>
        /// loads icons (not thumbnails) for the items in the list.
        /// Can either load all icons or only icons near the visible section.
        /// </summary>
        /// <param name="onlyVisible">true if you only want icons near the visible items.</param>
        private void LoadIconsForItems(bool onlyVisible = false)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: LoadIconsForItems Begin");
            try
            {
                if (!_listView.IsHandleCreated) return;

                bool isLarge = (_listView.View == View.LargeIcon);

                EnterListViewEnumeration();
                try
                {
                    if (_useVirtualMode)
                    {
                        int startIndex = 0;
                        int endIndex = _virtualItems.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = GetTopIndex();
                            int countPerPage = GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex - countPerPage);
                            endIndex = Math.Min(_virtualItems.Count - 1, topIndex + countPerPage * 2);
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

                            var lvi = GetItemInternal(i);

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
                System.Diagnostics.Debug.WriteLine("ExpList: LoadIconsForItems End");
            }
        }


        /// <summary>
        /// Gets the pixel size for a given thumbnail display mode
        /// </summary>
        private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        {
            //System.Diagnostics.Debug.WriteLine("ExpList: GetThumbnailSizeForMode Begin");
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
                //System.Diagnostics.Debug.WriteLine("ExpList: GetThumbnailSizeForMode End");
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

                Debug.WriteLine("Starting to request thumbnails...");

                EnterListViewEnumeration();
                try
                {
                    if (_useVirtualMode)
                    {
                        int startIndex = 0;
                        int endIndex = _virtualItems.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = GetTopIndex();
                            int countPerPage = GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex - countPerPage);
                            endIndex = Math.Min(_virtualItems.Count - 1, topIndex + countPerPage * 2);
                        }

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var item = _virtualItems[i];
                            // Skip if already in image list (GetThumbnailIndex will return != -1)
                            if (_thumbnailManager.GetThumbnailIndex(item.FullPath, thumbnailSize) != -1)
                                continue;

                            _thumbnailManager.RequestThumbnail(item, thumbnailSize, i);
                            Debug.WriteLine("ExpList: thumbnailManager.RequestThumbnail: " + i.ToString());
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

        private void SortVirtualItems(int column, SortOrder order)
        {
            System.Diagnostics.Debug.WriteLine("ExpList: SortVirtualItems Begin");
            try
            {
                if (order == SortOrder.None || _virtualItems.Count == 0) return;

                var col = _listView.Columns[column];

                _virtualItems.Sort((x, y) =>
                {
                    var xInfo = GetColumnData(x, col);
                    var yInfo = GetColumnData(y, col);

                    int result = 0;
                    if (xInfo.Tag is IComparable compX && yInfo.Tag is IComparable compY && xInfo.Tag.GetType() == yInfo.Tag.GetType())
                    {
                        result = compX.CompareTo(compY);
                    }
                    else
                    {
                        result = string.Compare(xInfo.Text, yInfo.Text, StringComparison.OrdinalIgnoreCase);
                    }

                    return order == SortOrder.Descending ? -result : result;
                });

                RecreateIndexMapping();
                _itemCache.Clear();
                _listView.Invalidate();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: SortVirtualItems End");
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
            private const int WM_VSCROLL = 0x0115;
            private const int WM_HSCROLL = 0x0114;
            private const int WM_MOUSEWHEEL = 0x020A;
            private const int WM_KEYDOWN = 0x0100;

            private readonly Action _onScroll;
            private readonly ListView _listView;
            private readonly ExpList _expList;

            public ListViewScrollHook(ExpList expList, Action onScroll)
            {
                System.Diagnostics.Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook Begin");
                try
                {
                    _onScroll = onScroll;
                    _expList = expList;
                    _listView = _expList._listView;
                    AssignHandle(_listView.Handle);
                }
                finally
                {
                    System.Diagnostics.Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook End");
                }
            }

            protected override void WndProc(ref Message m)
            {
                //System.Diagnostics.Debug.WriteLine("ExpList.WndProc Begin");
                try
                {
                    try
                    {
                        base.WndProc(ref m);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        _listView.SelectedIndices.Clear();
                    }


                    switch (m.Msg)
                    {
                        case WM_VSCROLL:
                        case WM_HSCROLL:
                        case WM_MOUSEWHEEL:
                            _expList._lastTopIndex = -1; //invalid due to a scroll moving items
                            QueueOnScroll();
                            break;
                        case WM_KEYDOWN:
                            Keys key = (Keys)m.WParam.ToInt32();
                            if (key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || key == Keys.Up || key == Keys.Down)
                            {
                                //the problem with the arrow keys is we don't have a test yet to see if the navigation movement stayed with the list of visible items or moved to a non-visible item
                                _expList._lastTopIndex = -1; //invalid due to a scroll moving items
                                QueueOnScroll();
                            }
                            break;
                    }
                }
                finally
                {
                    //System.Diagnostics.Debug.WriteLine("ExpList.WndProc End");
                }
            }

            private int _scrollQueued;
            private void QueueOnScroll()
            {
                System.Diagnostics.Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll Begin");
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
                    System.Diagnostics.Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll End");
                }
            }
        }

        private void OnListViewScroll()
        {
            System.Diagnostics.Debug.WriteLine("ExpList: OnListViewScroll Begin");
            try
            {
                //issues a new request to get thumbnails after a brief debounce delay
                _scrollDebounceTimer?.Stop();
                _scrollDebounceTimer?.Start();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("ExpList: OnListViewScroll End");
            }
        }

        #endregion

    }


}
