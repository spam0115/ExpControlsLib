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

        private CShellItem _currentFolderCsi;
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
        /// Delegate for the <see cref="ExpListFolderChanged"/> event.
        /// </summary>
        /// <param name="Item">The <see cref="CShellItem"/> of the folder that was just loaded.</param>
        public delegate void ExpListFolderChangedEventHandler(CShellItem Item);
        /// <summary>
        /// Occurs after the currently loaded folder has changed.
        /// </summary>
        [Category("Action")]
        [Description("Fires after the currently loaded folder has changed")]
        public event ExpListFolderChangedEventHandler ExpListFolderChanged;

        /// <summary>
        /// Delegate for the <see cref="ExpListPathChanged"/> event.
        /// </summary>
        /// <param name="Path">The new path of the ExpList.</param>
        public delegate void ExpListPathChangedEventHandler(string Path);
        /// <summary>
        /// Occurs when the <see cref="CurrentPath"/> has changed.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the CurrentPath property has changed")]
        public event ExpListPathChangedEventHandler ExpListPathChanged;

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
        /// Occurs when data for a custom column is requested.
        /// </summary>
        [Category("Action"), Description("Occurs when data for a custom column is requested.")]
        public event ExpListGetColumnDataEventHandler ExpListGetColumnData;

        #endregion


        #region Constructor & Initialization

        /// <summary>
        /// Initializes a new instance of <see cref="ExpList"/>, wires up all event handlers
        /// for the control and its child <see cref="_listView"/> ListView.
        /// </summary>
        public ExpList()
        {
            InitializeComponent();

            // Initialize thumbnail timer for lazy loading
            _thumbnailTimer = new System.Windows.Forms.Timer();
            _thumbnailTimer.Interval = 200;
            _thumbnailTimer.Tick += (s, e) =>
            {
                _thumbnailTimer.Stop();
                if (IsThumbnailViewMode())
                    LoadThumbnails(GetThumbnailSizeForMode(), true);
                else
                    LoadIconsForVisibleItems();
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


        public void Initialize(ShellController shellController)
        {
            _shellController = shellController;
        }

        /// <summary>
        /// Handles the <see cref="Control.Load"/> event of the <see cref="ExpList"/> control.
        /// Initializes drag and drop wrappers, thumbnail manager, and shell item update notifications.
        /// </summary>
        private void ExpList_Load(object sender, EventArgs e)
        {
            // Setup Drag and Drop Wrappers
            DW = new CDragWrapper(_listView);
            DropWrap = new ClvDropWrapper(_listView);

            // Initialize Thumbnail Manager
            _thumbnailManager = new ThumbnailImageListManager(_listView);

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

            SetAndLoadImageList(DisplayMode);
            LoadVisibleIcons();

        }

        /// <summary>
        /// Handles the <see cref="Control.HandleCreated"/> event of the <see cref="_listView"/> ListView.
        /// </summary>
        private void ExpFileList_HandleCreated(object sender, EventArgs e)
        {
            //SystemImageListManager.SetListViewImageList(ExpFileList, false, false);
            //SystemImageListManager.SetListViewImageList(ExpFileList, true, false);
            _scrollHook = new ListViewScrollHook(_listView, OnListViewScroll);
        }

        /// <summary>
        /// Handles the <see cref="Control.VisibleChanged"/> event of the <see cref="ExpList"/> control.
        /// Re-configures image lists for the current display mode when the control becomes visible.
        /// </summary>
        private void ExpList_VisibleChanged(object sender, EventArgs e) //occurs when the control become visible
        {
        }

        #endregion

        /// <summary>
        /// Delegate for the <see cref="ExpListGetColumnData"/> event.
        /// </summary>
        public delegate void ExpListGetColumnDataEventHandler(object sender, ExpListGetColumnDataEventArgs e);

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

                SetAndLoadImageList(value);
                LoadVisibleIcons();

                DisplayModeChanged?.Invoke(value);
            }
        }

        //private bool _smallImageListInitialized = false;
        //private bool _largeImageListInitialized = false;
        //private bool _thumbnailImageListInitialized = false;
        /// <summary>
        /// Configures the image lists bound to the ListView for the given display mode.
        /// For built-in Windows view modes (Details, List, LargeIcon, Tile), the system image
        /// list is applied and each item's <see cref="ListViewItem.ImageIndex"/> is refreshed.
        /// For custom thumbnail modes, the ListView is switched to LargeIcon view and
        /// <see cref="LoadThumbnails"/> is called to populate thumbnail images.
        /// </summary>
        /// <param name="value">The <see cref="ListViewDisplayMode"/> to configure for.</param>
        private void SetAndLoadImageList(ListViewDisplayMode value)
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

        private void LoadVisibleIcons(ListViewDisplayMode? mode = null)
        {
            mode = mode == null ? DisplayMode : mode;

            if (mode <= ListViewDisplayMode.Tile)
            {
                bool large = (mode == ListViewDisplayMode.LargeIcon);

                try
                {
                    _listView.BeginUpdate();

                    if (_useVirtualMode)
                    {
                        int topIndex = GetTopIndex();
                        int countPerPage = GetCountPerPage();
                        int lastIndex = Math.Min(_virtualItems.Count - 1, topIndex + countPerPage + 10);
                        for (int i = topIndex; i <= lastIndex; i++)
                        {
                            var item = GetItem(i);
                            if (item is null) continue;

                            item.ImageIndex = SystemImageListManager.GetIconIndex(item, large);
                            if (_itemCache.TryGetValue(i, out var cachedLvi))
                                cachedLvi.ImageIndex = item.ImageIndex;
                        }
                    }
                    else
                    {
                        EnterListViewEnumeration();
                        try
                        {
                            foreach (ListViewItem lvi in _listView.Items)
                            {
                                if (lvi is null) continue;
                                if (lvi.Tag is CShellItem csi)
                                    lvi.ImageIndex = SystemImageListManager.GetIconIndex(csi, large);
                                else
                                    lvi.ImageIndex = -1;
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
                    _listView.EndUpdate();
                }
            }
            else
            {
                LoadThumbnails(GetThumbnailSizeForMode(mode));
            }
        }

        /// <summary>
        /// Gets or sets the current file system path displayed in the list view.
        /// </summary>
        [Browsable(true), Category("Misc"),
         Description("The current path of ExpFileList"),
         DefaultValue(null)]
        public string CurrentPath
        {
            get => _CurrentPath;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    bool needsUpdate = !string.IsNullOrEmpty(_CurrentPath);
                    if (needsUpdate)
                    {
                        _listView.BeginUpdate();
                        _listView.SelectedItems.Clear();
                        _listView.Items.Clear();
                    }
                    _CurrentPath = value;
                    _itemIndex.Clear();
                    if (_currentFolderCsi != null)
                    {
                        _currentFolderCsi.ClearItems(true);
                        _currentFolderCsi = null;
                        ExpListFolderChanged?.Invoke(null);
                    }
                    
                    ExpListPathChanged?.Invoke(_CurrentPath);

                    if (needsUpdate)
                        _listView.EndUpdate();
                }
                else
                {
                    if (value == _CurrentPath && _currentFolderCsi != null) return;
                    try
                    {
                        var csi = CShellItemFactory.CreateCShItem(value);

                        if (csi != null && csi.IsFolder)
                            DisplayFiles(value, csi, true);
                        else
                        {
                            _CurrentPath = value;
                            ExpListPathChanged?.Invoke(_CurrentPath);
                        }
                    }
                    catch
                    {
                        _CurrentPath = value;
                        ExpListPathChanged?.Invoke(_CurrentPath);
                    }
                }
            }
        }
        private string _CurrentPath = null;

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

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (_useVirtualMode)
            {
                if (index >= 0 && index < _virtualItems.Count)
                {
                    _virtualItems.RemoveAt(index);
                    UpdateIndexMapping();
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
        /// Sets the sort column and order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSort(int column, SortOrder order)
        {
            if (_listView.ListViewItemSorter is LVColSorter sorter)
                sorter.SetSort(column, order);
        }

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
                    _listView.RetrieveVirtualItem += ExpFileList_RetrieveVirtualItem;
                    _listView.Items.Clear();
                    _itemIndex.Clear();
                }
                else
                {
                    _listView.RetrieveVirtualItem -= ExpFileList_RetrieveVirtualItem;
                    _virtualItems.Clear();
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }

                if (_currentFolderCsi != null)
                    DisplayFiles(_CurrentPath, _currentFolderCsi, true, reload: true);
            }
        }

        #endregion


        #region ExplorerTree Event Handling -- AfterNodeSelect

        /// <summary>
        /// Overrides <see cref="Control.WndProc(ref Message)"/> to handle shell context menu messages.
        /// </summary>
        /// <param name="m">The Windows <see cref="Message"/> to process.</param>
        protected override void WndProc(ref Message m)
        {
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

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="pathName">The display path of the folder.</param>
        /// <param name="csi">The <see cref="CShellItem"/> representing the folder to display.</param>
        /// <param name="includeFolder">True to include subdirectories in the list.</param>
        /// <param name="reload">True to force a reload even if the same item was previously selected.</param>
        public void DisplayFiles(string pathName, CShellItem csi, bool includeFolder, bool reload = false)
        {
            if (csi == null)
                csi = CShellItemFactory.CreateCShItem(pathName);

            if (csi is null) throw new Exception("Failed to create new CShellItem in DisplayFiles");

            bool samePath;
            if (_currentFolderCsi is null) 
                samePath = false;
            else 
                samePath = CPidl.IsEqual(_currentFolderCsi.PIDL, csi.PIDL);

            if (_currentFolderCsi != null && samePath && reload == false) return;

            var hierarchyCsi = _shellController.LoadFolderContents(csi);
            if (hierarchyCsi != null)
            {
                _currentFolderCsi = hierarchyCsi;
            }

            // record history
            if (!_isNavigatingHistory && _currentFolderCsi != null && !samePath)
            {
                _backHistory.Push(_currentFolderCsi);
                _forwardHistory.Clear();
            }

            _selectedItem = null; //new folder loaded, no item selected yet
            _CurrentPath = _currentFolderCsi.FullPath;

            //display directories separately
            var dirList = new List<CShellItem>();
            var fileList = new List<CShellItem>();
            if (includeFolder) dirList.AddRange(_currentFolderCsi.Directories);

            if (!csi.DisplayName.Equals(CShellItemFactory.StrMyComputer)) fileList.AddRange(_currentFolderCsi.Files);

            if ((dirList.Count + fileList.Count) == 0) //no items
            {
                if (RequestListViewRefresh(null, !samePath))
                {
                    _itemIndex.Clear();
                    if (_currentFolderCsi != null && !ReferenceEquals(_currentFolderCsi, csi))
                        _currentFolderCsi.ClearItems(true);
                }
                else return;
            }
            else
            {
                int totalItems;

                fileList.Sort();
                totalItems = fileList.Count;
                if (includeFolder)
                {
                    dirList.Sort();
                    totalItems += dirList.Count;
                }

                var combList = new List<CShellItem>(totalItems);
                if (includeFolder) combList.AddRange(dirList);
                combList.AddRange(fileList);

                if (_useVirtualMode)
                {
                    _virtualItems = combList;
                    UpdateIndexMapping();
                    _itemCache.Clear();
                    _listView.VirtualListSize = _virtualItems.Count;
                    // Removed LoadVisibleIcons call: the 200ms debounce timer started by 
                    // OnListViewScroll will handle the initial load correctly after layout.
                }
                else
                {
                    int initialFillLim = Math.Min(combList.Count, InitialLoadLimit);
                    var combinedLvi = new List<ListViewItem>(combList.Count);
                    int topIndex = this.GetIndexOfFirstVisible();

                    _itemIndex.Clear();
                    foreach (CShellItem item in combList)
                    {
                        ListViewItem lvi = MakeLVItem(item);
                        if (!_itemIndex.TryAdd(item.FullPath, lvi))
                        {
                            _itemIndex[item.FullPath] = lvi;
                        }

                        combinedLvi.Add(lvi);
                    }

                    if (!RequestListViewRefresh(combinedLvi.ToArray(), !samePath)) return;
                }
            }

            if (IsThumbnailViewMode())
            {
                OnListViewScroll();
            }

            ExpListFolderChanged?.Invoke(_currentFolderCsi);
            if (!samePath) ExpListPathChanged?.Invoke(_CurrentPath);
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
            _enumerationDepth++;
        }

        /// <summary>
        /// Decrements the enumeration depth counter. When it reaches 0, any deferred
        /// shell item updates are drained and applied.
        /// Must be paired with <see cref="EnterListViewEnumeration"/>.
        /// </summary>
        private void ExitListViewEnumeration()
        {
            _enumerationDepth--;
            if (_enumerationDepth <= 0)
            {
                _enumerationDepth = 0;
                DrainDeferredUpdates();
            }
        }

        /// <summary>
        /// Processes all deferred shell item updates that were queued while an enumeration was in progress.
        /// </summary>
        private void DrainDeferredUpdates()
        {
            while (_deferredUpdates.Count > 0)
            {
                var (sender, e) = _deferredUpdates.Dequeue();
                DoItemUpdate(sender, e);
            }
        }

        /// <summary>
        /// Executes the action immediately if no enumeration is in progress, otherwise
        /// defers it via BeginInvoke to run after the enumeration completes.
        /// Use this for ListView modification operations outside of DoItemUpdate.
        /// </summary>
        private void InvokeWhenListViewReady(Action action)
        {
            if (_enumerationDepth > 0)
            {
                BeginInvoke(() => InvokeWhenListViewReady(action));
                return;
            }
            action();
        }

        /// <summary>
        /// This refreshes the ListView with new items.
        /// This function marshals execution to the ui thread.  Also prevents double updating by gatekeeping 
        /// execution via the _refreshing boolean.  Without these precautions, we got errors with array index 
        /// out of bounds errors on the listview items that couldn't be resolved with only a lock. 
        /// </summary>
        /// <param name="newItems"></param>
        /// <returns></returns>
        private bool RequestListViewRefresh(ListViewItem[] newItems, bool fetchImages)
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

            BeginInvoke(new MethodInvoker(RefreshListViewCore)); // queue, don't run inline.  Can't take arguments because of MethodInvoker unfortunately
            
            return true;
        }

        private void RefreshListViewCore()
        {
            // If an enumeration is in progress (reentrancy via message pumping),
            // defer this refresh to after the enumeration completes.
            if (_enumerationDepth > 0)
            {
                BeginInvoke(new MethodInvoker(RefreshListViewCore));
                return;
            }

            try
            {
                // snapshot old position safely
                int topIndex = 0;
                if (_listView.Items.Count > 0)
                {
                    topIndex = GetIndexOfFirstVisible();
                }

                var newItems = _pendingItems ?? Array.Empty<ListViewItem>();

                _listView.BeginUpdate();
                try
                {
                    _listView.Items.Clear();
                    _itemIndex.Clear();

                    _listView.Items.AddRange(newItems);

                    if (_listView.Items.Count > 0)
                    {
                        _listView.Tag = _currentFolderCsi; // For ClvDropWrapper

                        topIndex = Math.Max(0, Math.Min(topIndex, _listView.Items.Count - 1));
                        _listView.EnsureVisible(topIndex);

                        if (_refetchImages) LoadVisibleIcons();
                    }
                }
                finally
                {
                    _listView.EndUpdate();
                }
            }
            finally
            {
                _refreshing = false;
                _refreshPending = false;
            }
        }

        #endregion

        #region MakeLVItem

        /// <summary>
        /// Creates a <see cref="ListViewItem"/> for a given <see cref="CShellItem"/>.
        /// Populates columns based on <see cref="ExpListGetColumnData"/> event or <see cref="ColumnHeader.Tag"/> mapping.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to create the list view item for.</param>
        /// <returns>A configured <see cref="ListViewItem"/>.</returns>
        private ListViewItem MakeLVItem(CShellItem item)
        {
            if (item == null) return new ListViewItem("Error: no CShellItem provided to MakeLVItem()");

            ListViewItem lvi = new ListViewItem(item.DisplayName);

            UpdateLviUsingCsi(lvi, item);

            return lvi;
        }


        /// <summary>
        /// Gets the pixel size for a given thumbnail display mode
        /// </summary>
        private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
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

        /// <summary>
        /// Loads thumbnails into image lists.  Then assigns the image list to the ListView.
        /// </summary>
        /// <param name="thumbnailSize">The size of the thumbnails to load.</param>
        /// <param name="onlyVisible">If true, only loads thumbnails for items currently visible in the viewport that don't already have one.</param>
        private void LoadThumbnails(int thumbnailSize, bool onlyVisible = false)
        {
            if (!_listView.IsHandleCreated) return;

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
                        int countPerPage = GetCountPerPage();
                        // Use a reasonable buffer (1 page above/below) for smoother scrolling
                        startIndex = Math.Max(0, topIndex - countPerPage);
                        endIndex = Math.Min(_virtualItems.Count - 1, topIndex + countPerPage * 2);
                    }

                    for (int i = startIndex; i <= endIndex; i++)
                    {
                        var item = _virtualItems[i];
                        // Skip if already in image list (GetThumbnailIndex will return != -1)
                        if (onlyVisible && _thumbnailManager.GetThumbnailIndex(item.FullPath, thumbnailSize) != -1)
                            continue;

                        _thumbnailManager.RequestThumbnail(null, item.FullPath, thumbnailSize, i, item);
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
                            _thumbnailManager.RequestThumbnail(item, csi.FullPath, thumbnailSize, -1, csi);
                    }
                }
            }
            finally
            {
                ExitListViewEnumeration();
            }
        }

        private const int LVM_GETTOPINDEX = 0x1000 + 39;
        private const int LVM_GETCOUNTPERPAGE = 0x1000 + 40;

        private int GetTopIndex()
        {
            return (int)SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, IntPtr.Zero);
        }

        private int GetCountPerPage()
        {
            return (int)SendMessage(_listView.Handle, LVM_GETCOUNTPERPAGE, 0, IntPtr.Zero);
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
            if (_itemIndex.TryGetValue(item.FullPath, out var lvi))
                return lvi;
            return null;
        }

        /// <summary>
        /// Inserts a <see cref="ListViewItem"/> into the ListView, maintaining sort order.
        /// </summary>
        /// <param name="lvi">The <see cref="ListViewItem"/> to insert.</param>
        /// <param name="lv">The <see cref="ListView"/> to insert into.</param>
        private void InsertLvi(ListViewItem lvi, ListView lv)
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

        /// <summary>
        /// Marshals shell item update events to the UI thread.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void UpdateInvoke(object sender, ShellItemUpdateEventArgs e)
        {
            if (sender is null)
            {
                Console.WriteLine("Sender cannot be null in UpdateInvoke.");
                return;
            }
            if (e is null)
            {
                Console.WriteLine("Event arguments cannot be null in UpdateInvoke.");
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((InvokeUpdate)DoItemUpdate, sender, e);
            }
            else
            {
                DoItemUpdate(sender, e);
            }
        }

        private void InsertVirtualItem(CShellItem item)
        {
            int insertIndex = -1;
            for (int i = 0; i < _virtualItems.Count; i++)
            {
                if (_virtualItems[i].CompareTo(item) > 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex == -1)
            {
                _virtualItems.Add(item);
                insertIndex = _virtualItems.Count - 1;
            }
            else
            {
                _virtualItems.Insert(insertIndex, item);
            }

            UpdateIndexMapping();
            _itemCache.Clear();
            _listView.VirtualListSize = _virtualItems.Count;
            _listView.Invalidate();
        }

        /// <summary>
        /// Performs the actual update of list view items in response to shell changes.
        /// Handles creation, deletion, renaming, and other updates of files and folders.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void DoItemUpdate(object sender, ShellItemUpdateEventArgs e)
        {
            if (sender is null) return;

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

                                if (IsThumbnailViewMode())
                                {
                                    lvi.ImageIndex = -1; // Placeholder until thumbnail is loaded
                                    if (m_CreateNew)
                                    {
                                        m_CreateNew = false;
                                        lvi.BeginEdit();
                                        if (IsThumbnailViewMode())
                                            _thumbnailManager.RequestThumbnail(e.Item.LVItem, e.Item.FullPath, GetThumbnailSizeForMode());
                                    }
                                }
                                else
                                    lvi.ImageIndex = ((CShellItem)e.Item).IconIndexNormal;

                                InsertLvi(lvi, _listView);
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
                                    _virtualItems.RemoveAt(index);
                                    UpdateIndexMapping();
                                    _itemCache.Clear();
                                    _listView.VirtualListSize = _virtualItems.Count;
                                    _listView.Invalidate();
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
                                DisplayFiles(_CurrentPath, _currentFolderCsi, true, reload: true);
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
                        DisplayFiles(_CurrentPath, _currentFolderCsi, true, reload: true);
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
                                        _thumbnailManager.RequestThumbnail(null, e.Item.FullPath, GetThumbnailSizeForMode(), index, e.Item);
                                    else
                                        _listView.RedrawItems(index, index, false);
                                }
                            }
                            else
                            {
                                var lvi = FindLVItem(e.Item);
                                if (lvi != null) {
                                    if (IsThumbnailViewMode())
                                        _thumbnailManager.RequestThumbnail(e.Item.LVItem, e.Item.FullPath, GetThumbnailSizeForMode(), -1, e.Item);
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
                                        _thumbnailManager.RequestThumbnail(null, e.Item.FullPath, GetThumbnailSizeForMode(), index, e.Item);
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
                                        _thumbnailManager.RequestThumbnail(e.Item.LVItem, e.Item.FullPath, GetThumbnailSizeForMode(), -1, e.Item);
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

        /// <summary>
        /// Refreshes the list view item associated with data from the given shell item.
        /// </summary>
        /// <param name="csi">The shell item whose corresponding list view item will be refreshed. Cannot be null.</param>
        public void UpdateLviUsingCsi(CShellItem csi)
        {
            if (csi == null) return;

            if (_useVirtualMode)
            {
                if (_pathToIndex.TryGetValue(csi.FullPath, out int index))
                {
                    _itemCache.Remove(index);
                    _listView.RedrawItems(index, index, false);
                }
            }
            else
            {
                var lvi = FindLVItem(csi);
                if (lvi == null) return;

                UpdateLviUsingCsi(lvi, csi);
            }
        }

        private void ExpFileList_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _virtualItems.Count) return;

            if (_itemCache.TryGetValue(e.ItemIndex, out var lvi))
            {
                // Sync ImageIndex if it was updated in the background while item was cached
                if (IsThumbnailViewMode() && lvi.ImageIndex == -1)
                {
                    var csi = _virtualItems[e.ItemIndex];
                    int thumbIndex = _thumbnailManager.GetThumbnailIndex(csi.FullPath, GetThumbnailSizeForMode());
                    if (thumbIndex != -1) lvi.ImageIndex = thumbIndex;
                }
                e.Item = lvi;
                return;
            }

            var item = _virtualItems[e.ItemIndex];
            lvi = MakeLVItem(item);

            // Limit cache size to prevent memory bloat during rapid scrolling
            if (_itemCache.Count > 1000) _itemCache.Clear();
            _itemCache[e.ItemIndex] = lvi;
            e.Item = lvi;
        }

        private void UpdateIndexMapping()
        {
            _pathToIndex.Clear();
            for (int i = 0; i < _virtualItems.Count; i++)
            {
                _pathToIndex[_virtualItems[i].FullPath] = i;
            }
        }

        /// <summary>
        /// Refreshes the display of a single item whose underlying filesystem data has changed.
        /// </summary>
        public void UpdateLviUsingCsi(ListViewItem lvi, CShellItem item)
        {
            if (lvi == null || item == null) return;

            // Update primary text
            lvi.Text = item.DisplayName;
            lvi.Name = item.FullPath;
            lvi.Tag = item;
            item.LVItem = lvi;

            for (int i = 1; i < _listView.Columns.Count; i++)
            {
                ColumnHeader col = _listView.Columns[i];
                GetColumnData(item, col, out string text, out object tag);

                if (lvi.SubItems.Count <= i)
                {
                    var si = lvi.SubItems.Add(new ListViewItem.ListViewSubItem());
                    si.Text = text;
                    si.Tag = tag;
                }
                else
                {
                    lvi.SubItems[i].Text = text;
                    lvi.SubItems[i].Tag = tag;
                }
            } //end for

            if (IsThumbnailViewMode())
            {
                int index = _thumbnailManager.GetThumbnailIndex(item.FullPath, GetThumbnailSizeForMode());
                lvi.ImageIndex = index;
            }
            else
                lvi.ImageIndex = SystemImageListManager.GetIconIndex(item, DisplayMode == ListViewDisplayMode.LargeIcon);
        }

        private void GetColumnData(CShellItem item, ColumnHeader col, out string text, out object tag)
        {
            text = string.Empty;
            tag = null;
            int colIndex = col.Index;

            // 1. Try Tag Mapping
            string mapping = col.Tag?.ToString();
            if (!string.IsNullOrEmpty(mapping) && mapping.StartsWith("."))
            {
                string propName = mapping.Substring(1);
                // Optimization: Check for common properties directly
                switch (propName)
                {
                    case "DisplayName":
                        text = item.DisplayName;
                        break;
                    case "TypeName":
                        text = item.TypeName;
                        break;
                    case "Size":
                        if (!item.IsDisk && item.IsFileSystem && !item.IsFolder)
                        {
                            text = item.Size;
                            tag = item.Length;
                        }
                        break;
                    case "LastWriteTime":
                        if (!item.IsDisk && item.LastWriteTime != EmptyTimeValue)
                        {
                            text = item.LastWriteTime.ToString("MM/dd/yyyy HH:mm:ss");
                            tag = item.LastWriteTime;
                        }
                        break;
                    case "CreationTime":
                        if (!item.IsDisk && item.CreationTime != EmptyTimeValue)
                        {
                            text = item.CreationTime.ToString("MM/dd/yyyy HH:mm:ss");
                            tag = item.CreationTime;
                        }
                        break;
                    default:  // Fallback to reflection for other properties
                        if (mapping.StartsWith(".Tag")) //get the value from one of the fields within the custom Tag object property
                        {
                            if (item.Tag != null)
                            {
                                string fieldName = mapping.Substring(4);
                                if (string.IsNullOrEmpty(fieldName)) break;

                                Type tagType = item.Tag.GetType();
                                FieldInfo field = tagType.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
                                if (field != null)
                                {
                                    object val = field.GetValue(item.Tag);
                                    text = val?.ToString() ?? string.Empty;
                                    tag = val;
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
                            }
                        }
                        break;
                }
            }
            else if (ExpListGetColumnData is not null) // 2. Try Event
            {
                var args = new ExpListGetColumnDataEventArgs(item, col);
                ExpListGetColumnData?.Invoke(this, args);

                if (args.Handled)
                {
                    text = args.Text;
                    tag = args.Tag;
                }
                else if (colIndex == 0)
                {
                    text = item.DisplayName;
                }
            }
            else if (colIndex == 0)
            {
                text = item.DisplayName;
            }
        }

        /// <summary>
        /// Refresh by display name string.  This is very inefficient.  Avoid this function.
        /// </summary>
        public ListViewItem? RefreshItemByDisplayName(string fileName)
        {
            // Try to find the item by its display name using the index values
            //var lvi = _itemIndex.Values.FirstOrDefault(i => i.Tag is CShellItem c &&
            //        string.Equals(c.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));
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

        public ListViewItem? RefreshItemByFullPath(string path)
        {
            if (_itemIndex.TryGetValue(path, out ListViewItem? lvi))
            {
                if (lvi is null) return null;

                if (lvi.Tag is CShellItem csi)
                    UpdateLviUsingCsi(lvi, csi);

                return lvi;
            }
            else return null;
        }

        public ListViewItem? RefreshItem(CShellItem? item)
        {
            if (item is null) return null;

            if (_itemIndex.TryGetValue(item.FullPath, out ListViewItem? lvi))
            {
                if (lvi is null) return null;

                UpdateLviUsingCsi(lvi, item);

                return lvi;
            }
            else return null;
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Navigates back to the previous folder in the history.
        /// </summary>
        public void GoBack()
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

        /// <summary>
        /// Navigates forward to the next folder in the history.
        /// </summary>
        public void GoForward()
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

        /// <summary>
        /// Navigates to the parent folder of the currently loaded folder.
        /// </summary>
        public void GoUp()
        {
            if (_currentFolderCsi?.Parent != null)
            {
                var parent = _currentFolderCsi.Parent;
                DisplayFiles(parent.FullPath, parent, true);
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

        #region ExpFileList_DoubleClick

        private void ExpFileList_Click(object sender, EventArgs e)
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

        /// <summary>
        /// Handles double-click events on list view items. 
        /// Folders are navigated into, while files are launched.
        /// </summary>
        private void ExpFileList_DoubleClick(object sender, EventArgs e)
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


        private void ExpFileList_SelectedIndexChanged(object sender, EventArgs e)
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

        private void ExpFileList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            ItemSelectionChanged?.Invoke(e);
        }

        #endregion

        #region ExpFileList_Leave

        /// <summary>
        /// Handles the <see cref="Control.Leave"/> event of the <see cref="_listView"/> ListView.
        /// Clears the current selection.
        /// </summary>
        /// what the hell good is this?  It makes it impossible to use any selections to do anything.
        //private void ExpFileList_Leave(object sender, EventArgs e)
        //{
        //    ExpFileList.SelectedItems.Clear();
        //}

        #endregion

        #region LabelEdit Handlers (Item Rename)

        /// <summary>
        /// Handles the <see cref="ListView.BeforeLabelEdit"/> event.
        /// Determines if an item can be renamed and sets up the edit control.
        /// </summary>
        private void ExpFileList_BeforeLabelEdit(object sender, LabelEditEventArgs e)
        {
            IntPtr editWnd = SendMessage(_listView.Handle, LVM_GETEDITCONTROL, 0, IntPtr.Zero);
            int textLen = Path.GetFileNameWithoutExtension(_listView.Items[e.Item].Text).Length;
            SendMessage(editWnd, EM_SETSEL, IntPtr.Zero, (IntPtr)textLen);

            var item = (CShellItem)_listView.Items[e.Item].Tag;
            if ((!item.IsFileSystem) || item.IsDisk ||
                item.FullPath == CShellItemFactory.CreateCShItem(CSIDL.MYDOCUMENTS).FullPath ||
                !item.CanRename)
            {
                System.Media.SystemSounds.Beep.Play();
                e.CancelEdit = true;
            }
        }

        /// <summary>
        /// Handles the <see cref="ListView.AfterLabelEdit"/> event.
        /// Applies the new name to the shell item.
        /// </summary>
        private void ExpFileList_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            var item = (CShellItem)_listView.Items[e.Item].Tag;
            if (e.Label == null || e.Label == string.Empty) return;

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
            if (e.X < 0 || e.Y < 0) return false;
            Rectangle cr = ctl.ClientRectangle;
            if (e.X > cr.Width || e.Y > cr.Height) return false;
            return true;
        }

        /// <summary>
        /// Sorts the items in the list view based on their tags (CShellItem).
        /// </summary>
        private void SortLVItems()
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

        /// <summary>
        /// Handles the MouseLeave event to track when the mouse is outside the list view.
        /// </summary>
        private void ExpFileList_MouseLeave(object sender, EventArgs e)
        {
            m_OutOfRange = true;
            OnMouseLeave(e);
        }

        private void ExpFileList_MouseEnter(object sender, EventArgs e)
        {
            OnMouseEnter(e);
        }

        /// <summary>
        /// Handles the MouseDown event to reset the out-of-range flag for right-clicks.
        /// </summary>
        private void ExpFileList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) m_OutOfRange = false;
            OnMouseDown(e);
        }

        /// <summary>
        /// Handles the MouseUp event to trigger context menus or middle-click actions.
        /// </summary>
        private void ExpFileList_MouseUp(object sender, MouseEventArgs e)
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

        private void ExpFileList_MouseMove(object sender, MouseEventArgs e)
        {
            OnMouseMove(e);
        }


        /// <summary>
        /// Creates a native Windows context menu for the current folder.
        /// </summary>
        /// <param name="comContextMenu">Output parameter for the main context menu handle.</param>
        /// <param name="viewSubMenu">Output parameter for the View submenu handle.</param>
        private void CreateContextMenu(out IntPtr comContextMenu, out IntPtr viewSubMenu, out IntPtr sortSubMenu)
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
        #endregion

        #region Keyboard Events


        /// <summary>
        /// Handles KeyDown events for shortcuts (Ctrl+A, Ctrl+C/V/X, Delete, F2, F5, Enter).
        /// </summary>
        private void ExpFileList_KeyDown(object sender, KeyEventArgs e)
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


        /// <summary>
        /// Handles the KeyUp event for navigation keys.
        /// </summary>
        private void ExpFileList_KeyUp(object sender, KeyEventArgs e)
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

        private void ExpFileList_KeyPress(object sender, KeyPressEventArgs e)
        {

            OnKeyPress(e);
        }

        /// <summary>
        /// Launches a file using the default system handler.
        /// </summary>
        /// <param name="csi">The <see cref="CShellItem"/> to launch.</param>
        private void LaunchFile(CShellItem csi)
        {
            var psi = new ProcessStartInfo {
                FileName = csi.FullPath,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        /// <summary>
        /// Invokes a standard shell action (cut, copy, paste, delete) on the selected items.
        /// </summary>
        /// <param name="cmd">The shell verb to invoke (e.g., "cut", "copy", "paste", "delete").</param>
        private void WinMenu(string cmd)
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
                        topItemIndex = GetIndexOfFirstVisible();
                        _listView.SelectedItems.Clear();
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
            foreach (var lvi in _itemIndex.Values)
            {
                if (string.Equals(lvi.Text, name, StringComparison.OrdinalIgnoreCase))
                    return lvi;
            }
            return null;
        }

        /// <summary>
        /// Finds a ListViewItem by its Shell ID (PIDL).
        /// </summary>
        public ListViewItem FindItemByID(IntPtr pidl)
        {
            foreach (var lvi in _itemIndex.Values)
            {
                if (lvi.Tag is CShellItem csi && CPidl.IsEqual(csi.PIDL, pidl))
                    return lvi;
            }
            return null;
        }

        /// <summary>
        /// Finds a ListViewItem by its full filesystem path.
        /// </summary>
        public ListViewItem FindItemByPath(string path)
        {
            if (_itemIndex.TryGetValue(path, out var lvi))
                return lvi;
            return null;
        }

        #endregion

        #region Lazy Thumbnail Loading Support

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
        private System.Windows.Forms.Timer _thumbnailTimer;

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

            public ListViewScrollHook(ListView listView, Action onScroll)
            {
                AssignHandle(listView.Handle);
                _onScroll = onScroll;
                _listView = listView;
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    base.WndProc(ref m);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    _listView.SelectedItems.Clear();
                    _listView.SelectedIndices.Clear();
                }

                switch (m.Msg)
                {
                    case WM_VSCROLL:
                    case WM_HSCROLL:
                    case WM_MOUSEWHEEL:
                        QueueOnScroll();
                        break;
                    case WM_KEYDOWN:
                        Keys key = (Keys)m.WParam.ToInt32();
                        if (key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || key == Keys.Up || key == Keys.Down)
                            QueueOnScroll();
                        break;
                }
            }

            private int _scrollQueued;
            private void QueueOnScroll()
            {
                if (_listView.IsDisposed || !_listView.IsHandleCreated) return;
                if (System.Threading.Interlocked.Exchange(ref _scrollQueued, 1) == 1) return;

                _listView.BeginInvoke((MethodInvoker)(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _scrollQueued, 0);
                    if (!_listView.IsDisposed) _onScroll?.Invoke();
                }));
            }
        }

        private void OnListViewScroll()
        {
            //issues a new request to get thumbnails after a brief debounce delay
            _thumbnailTimer?.Stop();
            _thumbnailTimer?.Start();
        }

        private void SortVirtualItems(int column, SortOrder order)
        {
            if (order == SortOrder.None || _virtualItems.Count == 0) return;

            var col = _listView.Columns[column];

            _virtualItems.Sort((x, y) =>
            {
                GetColumnData(x, col, out string textX, out object valX);
                GetColumnData(y, col, out string textY, out object valY);

                int result = 0;
                if (valX is IComparable compX && valY is IComparable compY && valX.GetType() == valY.GetType())
                {
                    result = compX.CompareTo(compY);
                }
                else
                {
                    result = string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);
                }

                return order == SortOrder.Descending ? -result : result;
            });

            UpdateIndexMapping();
            _itemCache.Clear();
            _listView.Invalidate();
        }

        private void LoadIconsForVisibleItems()
        {
            if (!_listView.IsHandleCreated) return;

            Rectangle clientRect = _listView.ClientRectangle;
            bool large = (_listView.View == View.LargeIcon);

            EnterListViewEnumeration();
            try
            {
                if (_useVirtualMode)
                {
                    int topIndex = GetTopIndex();
                    int countPerPage = GetCountPerPage();
                    int lastIndex = Math.Min(_virtualItems.Count - 1, topIndex + countPerPage);
                    for (int i = topIndex; i <= lastIndex; i++)
                    {
                        if (_itemCache.TryGetValue(i, out var lvi) && lvi.ImageIndex == -1)
                        {
                            var csi = _virtualItems[i];
                            lvi.ImageIndex = SystemImageListManager.GetIconIndex(csi, large);
                        }
                    }
                }
                else
                {
                    foreach (ListViewItem item in _listView.Items)
                    {
                        if (item is null) continue;
                        if (!clientRect.IntersectsWith(item.Bounds)) continue;

                        if (item.Tag is CShellItem csi && item.ImageIndex == -1)
                        {
                            item.ImageIndex = SystemImageListManager.GetIconIndex(csi, large);
                        }
                    }
                }
            }
            finally
            {
                ExitListViewEnumeration();
            }
        }

        #endregion

        public int GetIndexOfFirstVisible()
        {
            ListViewItem? current;
            if (_listView.View == View.Details || _listView.View == View.List)
            {
                current = _listView.TopItem;   // valid here
            }
            else
            {
                if (_listView.Items is null) return 0;

                EnterListViewEnumeration();
                try
                {
                    current = _listView.Items
                        .Cast<ListViewItem>()
                        .Where(it => it != null && _listView.ClientRectangle.IntersectsWith(it.Bounds))
                        .OrderBy(it => it.Bounds.Top)
                        .ThenBy(it => it.Bounds.Left)
                        .FirstOrDefault();
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }

            return (current?.Index == null ? 0 : current.Index);
        }

    }
}
