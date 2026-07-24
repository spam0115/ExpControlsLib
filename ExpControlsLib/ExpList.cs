using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;
using MethodInvoker = System.Windows.Forms.MethodInvoker;
using ListView = System.Windows.Forms.ListView;

namespace ExpControlsLib
{
    /// <summary>
    ///     This Form is a fully working start point for any form which requires an ExplorerTree and
    ///     ListView with enough room left for application specific controls.
    ///     
    ///     Explanation about how file icons are handled:
    ///     It is handled by a weird mix of Windows and custom code.  We use the OS's Shell's 
    ///     SystemImageListManager - it caches icons and provides them on demand to the ListView.  
    ///     The Listview is linked to the image-list orchestrator, which selects the appropriate
    ///     native or managed image list. However, just setting the image list
    ///     doesn't link the listview items to the image list - you still have to set the ImageIndex of each 
    ///     ListViewItem to the appropriate index in the SystemImageList.  This is done by setting  
    ///     ListViewItem.ImageIndex is populated by the orchestrator (Tag contains a reference to
    ///     the CShellItem for each Shell item entity.).
    ///     
    ///     However, for thumbnail display modes, Windows Shell doesn't have native support for that. 
    ///     We implemented the ImageListOrchestrator to coordinate that shortfall.
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
    ///     
    ///     BTW, non-virtual mode is currently broken because I don't really care about non-virtual mode right now.
    /// </remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public partial class ExpList
    {

        #region Private fields

        private const int _batchThreshold = 5;
        private int _approxCountPerPage = 0;
        // InitialLoadLimit is the number of ExpFileList.Items whose IconIndex will be fetched on initial load
        // the balance will be fetched AFTER ExpFileList.EndUpdate
        private const int InitialLoadLimit = 128;

        // For ExpFileList label text selection
        private const int EM_SETSEL = 0xB1;

        private ShellController? _shellController = null;
        private ShellDirectoryLoader? _directoryLoader;
        private HashSet<string> _excludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Func<CShellItem, bool>? _filter;
        private ImageListOrchestrator _imageListOrchestrator = null!;
        private VirtualListViewWrapper _listViewWrapper;
        private bool _initialized = false;

        // Avoid Globalization problem-- an empty timevalue
        private static readonly DateTime EmptyTimeValue = new DateTime(1, 1, 1, 0, 0, 0);


        private readonly ExpNavigationHistory _navigation = new(
            (left, right) => CPidl.ResolvesToSamePathOrName(left.PIDL, right.PIDL));

        private CDragWrapper DW;         // Wrapper for Drag ops originating in ExpFileList
        private ClvDropWrapper DropWrap; // Wrapper for Drop ops targeting ExpFileList
        private bool m_CreateNew = false; // Flag for NewMenu processing of "New" item

        // Reentrancy guard: prevents DoItemUpdate from modifying _listView.Items
        // while an enumeration is in progress (Invoke() pumps messages and can trigger
        // reentrant shell notifications on the same UI thread).
        private int _enumerationDepth = 0;
        private readonly Queue<(object? sender, ShellItemUpdateEventArgs e)> _deferredUpdates = new();

        // Reentrancy guard for ShowAndHandleContextMenu. TrackPopupMenuEx runs a modal
        // message loop, and the method is async void with awaits that resume on the UI
        // thread message pump. A second right-click arriving during the first call's
        // await continuation would re-enter and corrupt the in-flight menu handles /
        // m_WindowsContextMenu state.
        private bool m_IsShowingContextMenu = false;

        // Reentrancy guard for image list modifications. Prevents modifying the 
        // image list while the OS is in the middle of a draw cycle (e.g. RetrieveVirtualItem).
        private int _imageListMutationDepth = 0;
        private readonly Queue<(object? sender, ThumbnailReadyEventArgs e)> _deferredThumbnailUpdates = new();
        // Tracks whether a deferred-drain message has been posted to the UI message pump.
        // Ensures the drain runs on a clean pump cycle (not reentrantly on the
        // RetrieveVirtualItem / paint call stack) so that RedrawItems calls issued
        // by the drain are not coalesced away by the control's in-flight draw.
        private bool _drainScheduled = false;

        private bool IsInDesignMode => (DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime);

        // These methods are the narrow internal surface used by thumbnail management.
        // Keep the ListView itself private so callers cannot depend on its implementation.
        internal void BeginListViewUpdate() => _listView.BeginUpdate();

        internal void EndListViewUpdate() => _listView.EndUpdate();

        internal ImageList? LargeImageList
        {
            get => _listView.LargeImageList;
            set => _listView.LargeImageList = value;
        }

        internal void ResetListViewItemImageIndices()
        {
            if (VirtualMode) return;

            foreach (ListViewItem item in _listView.Items)
            {
                if (item is not null) item.ImageIndex = -1;
            }
        }

        internal void ClearListViewImageLists()
        {
            _listView.LargeImageList = null;
            _listView.SmallImageList = null;
        }

        public bool IsShuttingDown {
            get; 
            set {
                _listViewWrapper.IsShuttingDown = value;
                field = value;
            }
        }


        private CancellationTokenSource? _loadDirectoryCancelTs;
        private StaThreadRunner? _staRunner;
        private ShellCommandService? _shellCommandService;

        private void Cleanup()
        {
            _scrollDebounceTimer?.Stop();
            _scrollDebounceTimer?.Dispose();
            _scrollDebounceTimer = null;

            // Stop producers before releasing the resources they may be using.
            // In particular, directory loads can still be queued on the dedicated
            // STA runner when the control is torn down.
            _loadDirectoryCancelTs?.Cancel();
            _loadDirectoryCancelTs?.Dispose();
            _loadDirectoryCancelTs = null;

            _staRunner?.Dispose();
            _staRunner = null;
            _shellCommandService = null;

            _imageListOrchestrator?.Dispose();
            if (_shellController?.ShellUpdater != null)
                _shellController.ShellUpdater.UpdateEvent -= ShellUpdater_UpdateEventInvoker;

            // ContextMenu owns shell COM interfaces and the New-menu state. Release
            // it with the rest of the control-owned resources, including when the
            // menu is still open while the control is being disposed.
            m_WindowsContextMenu.Dispose();
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

        public delegate void ExpListDirectoryLoadedEventHandler(int itemCount);
        [Category("Action")]
        [Description("Fires after a directory load completes with the total item count")]
        public event ExpListDirectoryLoadedEventHandler ExpListDirectoryLoaded;

        public event EventHandler ExpListDirectoryLoading;

        public event EventHandler ExpListEmptyClick;

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
        public delegate void ExpListMoveEventHandler(object? sender, ExpListMoveEventArgs e);
        /// <summary>
        /// Occurs when Move is selected from the context menu.
        /// </summary>
        [Category("Action")]
        [Description("Fires when Move is selected from the context menu")]
        public event ExpListMoveEventHandler ExpListMove;

        /// <summary>
        /// Delegate for the <see cref="ExpListCopy"/> event.
        /// </summary>
        public delegate void ExpListCopyEventHandler(object? sender, ExpListCopyEventArgs e);
        /// <summary>
        /// Occurs when Copy to Folder is selected from the context menu.
        /// </summary>
        [Category("Action")]
        [Description("Fires when Copy to Folder is selected from the context menu")]
        public event ExpListCopyEventHandler ExpListCopy;

        /// <summary>
        /// Occurs when a drag-and-drop operation completes (move or copy).
        /// Unlike <see cref="ExpListMove"/> and <see cref="ExpListCopy"/>, this fires for
        /// shell drag-and-drop operations where the move/copy was already performed by the shell.
        /// </summary>
        [Category("Action")]
        [Description("Fires when a drag-and-drop operation completes (move or copy)")]
        public event EventHandler<DragCompletedEventArgs>? ExpListDragCompleted;

        /// <summary>
        /// Delegate for the <see cref="Deleted"/> event.
        /// </summary>
        public delegate void ExpListDeletedEventHandler(object? sender, ExpListDeletedEventArgs e);
        /// <summary>
        /// Occurs when items are deleted from the list view.
        /// </summary>
        [Category("Action")]
        [Description("Fires when items are deleted from the list view")]
        public event ExpListDeletedEventHandler ItemDeleted;

        public delegate void ExpListSelectedIndexChangedEventHandler(object? sender, CShellItem item);
        /// <summary>
        /// Occurs when the selection in the list view changes.
        /// </summary>
        [Category("Action")]
        [Description("Fires when the selection changes")]
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
        public delegate void ExpListGetColumnDataEventHandler(object? sender, ExpListGetColumnDataEventArgs e);
        /// <summary>
        /// Occurs when data for a custom column is requested.
        /// </summary>
        [Category("Action"), Description("Occurs when data for a custom column is requested.")]
        public event ExpListGetColumnDataEventHandler ExpListGetColumnData;

        /// <summary>
        /// Delegate for the <see cref="ExpListBulkColumnDataRequested"/> event.
        /// </summary>
        public delegate void ExpListBulkColumnDataRequestedEventHandler(object? sender, ExpListBulkColumnDataEventArgs e);
        /// <summary>
        /// Occurs during directory loading with all items, allowing bulk column data fetching.
        /// This is more efficient than <see cref="ExpListGetColumnData"/> for large directories
        /// because it allows a single database query for all items instead of one per item.
        /// </summary>
        [Category("Action"), Description("Occurs during directory loading with all items for bulk column data fetching.")]
        public event ExpListBulkColumnDataRequestedEventHandler ExpListBulkColumnDataRequested;

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

        /// <summary>Delegate for the <see cref="ItemChecked"/> event.</summary>
        public delegate void ExpListItemCheckedEventHandler(object? sender, ExpListItemCheckedEventArgs e);

        /// <summary>
        /// Occurs after a <see cref="CShellItem"/>'s checked state changes as a result of a
        /// user interaction or a call to <see cref="SetChecked"/>.
        /// Not raised during bulk <see cref="CheckAll"/> / <see cref="UncheckAll"/>.
        /// </summary>
        [Category("Action")]
        [Description("Fires when an item's checked state changes")]
        public event ExpListItemCheckedEventHandler? ItemChecked;


        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the display mode used to present items in the list view.
        /// The native ListView dates from Windows 95 and doesn't support thumbnails.  Support for thumbnails 
        /// was a kludge introduced in XP.
        /// If you have checkboxes turned on and then you switch to a displaymode that doesn't support checkboxes 
        /// (all icon modes), the handle for this control will be recreated.  This also happens if you switch from a 
        /// displaymode that doesn't support checkboxes to one that does.  This causes the scroll position to be lost
        /// so we must save and restore the scroll position.
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
                if (!_listView.IsHandleCreated)
                {
                    _listViewWrapper.DisplayMode = value;
                    return;
                }

                if (_listViewWrapper.DisplayMode == value) return;

                //save scroll position
                int topIndex = 0;
                if (_listViewWrapper.Items.Count > 0)
                {
                    topIndex = _listViewWrapper.GetTopIndex();
                }

                _listViewWrapper._listView.BeginUpdate();

                try
                {
                    _listViewWrapper.DisplayMode = value;
                    //_listViewWrapper._listView.EnsureVisible(topIndex); //no effect inside beginupdate
                    //_listViewWrapper._listView.TopItem = _listViewWrapper._listView.Items[t]; //no effect inside beginupdate
                    SetImageListForMode(value);
                }
                finally
                {
                    _listViewWrapper._listView.EndUpdate();
                }


                if (_listViewWrapper.Items.Count > 0)
                {
                    //_listViewWrapper._listView.TopItem = _listViewWrapper._listView.Items[topIndex]; //works for transitioning from icons to details but not from details to icons - just resets to position 0
                    _listViewWrapper.MoveItemToTop(topIndex);
                    if (_imageListOrchestrator != null && _listViewWrapper.VirtualMode) LoadImagesForVisibleItems();
                }

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
        /// Gets or sets a predicate used to filter items in the list view.
        /// When set, this predicate is applied automatically during directory loading (pre-load filtering)
        /// and during shell notification insertions. Use <see cref="ApplyFilter"/> to re-apply the filter
        /// to already-loaded items (post-load filtering).
        /// Set to null to disable filtering.
        /// </summary>
        /// <remarks>
        /// The predicate is evaluated for each item after custom column data has been fetched,
        /// so column-based criteria (e.g., classification) are available for filtering.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<CShellItem, bool>? Filter
        {
            get => _filter;
            set => _filter = value;
        }

        /// <summary>
        /// Applies the current <see cref="Filter"/> predicate to the already-loaded items.
        /// This is post-load filtering: it re-evaluates the filter against all items in the master list
        /// and rebuilds the filtered view. Call this after filter criteria change or after new data
        /// (e.g., classification scores) has been populated into the items.
        /// If <see cref="Filter"/> is null, clears any active filter.
        /// </summary>
        public void ApplyFilter()
        {
            if (_filter == null)
            {
                _listViewWrapper.ClearFilter();
                return;
            }

            _listViewWrapper.SetFilter(_filter);
        }

        /// <summary>
        /// Applies the specified filter predicate to the already-loaded items and stores it
        /// as the current <see cref="Filter"/>. This is a convenience method equivalent to
        /// setting <see cref="Filter"/> and then calling <see cref="ApplyFilter()"/>.
        /// </summary>
        /// <param name="predicate">The filter predicate to apply, or null to clear the filter.</param>
        public void ApplyFilter(Func<CShellItem, bool>? predicate)
        {
            _filter = predicate;
            ApplyFilter();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the list view is in virtual mode.
        /// This must be set before the control is displayed.
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

        public CShellItem? FocusedItem
        {
            get
            {
                if (_listView.FocusedItem != null)
                    return GetItem(_listView.FocusedItem.Index);
                return null;
            }
        }

        public string? LastMoveFolder { get; set; }

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

                var oldCsi = _currentFolderCsi;
                if (value != null)
                    _currentFolderCsi = _shellController.HierachyManager.Add(value);
                else
                    _currentFolderCsi = value;
                if (_currentFolderCsi is not null && isDifferent)
                {
                    _navigation.RecordSelection(_currentFolderCsi);
                }
                if (_initialized)
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
                //int current = GetScrollPos(_listView.Handle, SB_VERT);
                SendMessage(_listView.Handle, (uint)LVM_SCROLL, 0, value);
            }
        }

        private bool TryGetVerticalScrollPercentage(out double percentage)
        {
            percentage = 0;
            if (!_listView.IsHandleCreated) return false;

            var scrollInfo = new SCROLLINFO
            {
                cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(),
                fMask = SIF_ALL
            };

            if (!GetScrollInfo(_listView.Handle, SB_VERT, ref scrollInfo)) return false;

            int maximumPosition = scrollInfo.nMax - Math.Max((int)scrollInfo.nPage - 1, 0);
            int scrollableRange = maximumPosition - scrollInfo.nMin;
            if (scrollableRange <= 0)
            {
                percentage = 0;
                return true;
            }

            percentage = Math.Clamp(
                (scrollInfo.nPos - scrollInfo.nMin) / (double)scrollableRange,
                0,
                1);
            return true;
        }

        private void RestoreVerticalScrollPercentage(double percentage)
        {
            if (!_listView.IsHandleCreated) return;

            var scrollInfo = new SCROLLINFO
            {
                cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(),
                fMask = SIF_ALL
            };

            if (!GetScrollInfo(_listView.Handle, SB_VERT, ref scrollInfo)) return;

            int maximumPosition = scrollInfo.nMax - Math.Max((int)scrollInfo.nPage - 1, 0);
            int scrollableRange = maximumPosition - scrollInfo.nMin;
            int targetPosition = scrollInfo.nMin + (int)Math.Round(
                Math.Clamp(percentage, 0, 1) * Math.Max(scrollableRange, 0));

            var rowHeight = _listViewWrapper.GetRowHeight();

            ////

            targetPosition = (int)Math.Round(scrollInfo.nMin + percentage * scrollableRange);


            VerticalScrollPosition = targetPosition * rowHeight;
        }

        private void QueueVerticalScrollPercentageRestore(double percentage)
        {
            if (!_listView.IsHandleCreated || _listView.IsDisposed) return;

            _listView.BeginInvoke((MethodInvoker)(() =>
            {
                if (!_listView.IsDisposed)
                    RestoreVerticalScrollPercentage(percentage);
            }));
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
        /// Note: the items are in sorted order, not chronological order.
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
        /// Gets or sets a value indicating whether a checkbox is displayed next to each item.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WinForms renders the checkbox glyph only in <c>Details</c>, <c>List</c>, and
        /// <c>SmallIcon</c> view modes. In <c>LargeIcon</c> / thumbnail modes the glyph may
        /// not appear, but <see cref="CShellItem.Checked"/> state is still maintained in the model.
        /// </para>
        /// <para>
        /// Setting <c>CheckBoxes</c> on the underlying Win32 <c>SysListView32</c> forces a
        /// handle recreation, which silently resets <see cref="ListView.VirtualListSize"/>
        /// to 0 and corrupts any cached virtual-mode <see cref="ListViewItem"/> objects.
        /// This setter compensates by dropping virtual mode across the change and then
        /// restoring <c>VirtualListSize</c> and clearing the wrapper's item cache. See the
        /// remarks on <see cref="VirtualListViewWrapper.VirtualMode"/> for the full
        /// explanation of the handle-recreation issue.
        /// </para>
        /// </remarks>
        [Category("Behavior")]
        [Description("Show a checkbox next to each list item.")]
        [DefaultValue(false)]
        public bool CheckBoxes
        {
            get => _listViewWrapper.CheckBoxes;
            set => _listViewWrapper.CheckBoxes = value;
        }

        /// <summary>
        /// Enumerates every <see cref="CShellItem"/> in the list whose
        /// <see cref="CShellItem.Checked"/> property is <c>true</c>.
        /// Works in both virtual and non-virtual mode; reflects the full item set
        /// regardless of any active filter.
        /// </summary>
        [Browsable(false)]
        public IEnumerable<CShellItem> CheckedShellItems =>
            _listViewWrapper.AllShellItems.Where(i => i.Checked);

        /// <summary>Gets the count of checked items.</summary>
        [Browsable(false)]
        public int CheckedCount => _listViewWrapper.AllShellItems.Count(i => i.Checked);

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

                VisibleChanged += ExpFileList_VisibleChanged;

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
                _listView.ItemChecked          += ExpFileList_ItemChecked;

                _listViewWrapper = new VirtualListViewWrapper(this, _listView);
                _listViewWrapper.CreateListviewItemCallback = CreateListviewItemCallback;
                _listViewWrapper.UpdateListviewItemCallback = UpdateListviewItemCallback;

            }
            finally
            {
                Debug.WriteLine("ExpList: ExpList End");
            }
        }

        /// <summary>
        /// This initializes some fields in this user control.  This should be called before the Load event.
        /// </summary>
        /// <param name="shellController"></param>
        public void Initialize(ShellController shellController)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.Initialize: Begin");
            if (_initialized)
                throw new InvalidOperationException("ExpList has already been initialized.");

            _shellController = shellController ?? throw new ArgumentNullException(nameof(shellController));
            _directoryLoader = new ShellDirectoryLoader(_shellController);
            _initialized = true;
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.Initialize: End");
        }

        /// <summary>
        /// Handles the <see cref="Control.Load"/> event of the <see cref="ExpList"/> control.
        /// Initializes drag and drop wrappers, thumbnail manager, and shell item update notifications.
        /// </summary>
        private void ExpList_Load(object? sender, EventArgs e)
        {
            if (IsInDesignMode)
                return;

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Begin");
            try
            {
                _staRunner = new StaThreadRunner(5, "ExpListStaRunner"); //todo: i think we might be limited to one sta thread becuase com objects have thread affinity and COM tries to marshal com calls to different threads and post messages onto the other thread's message queue.
                _shellCommandService = new ShellCommandService(_staRunner);

                // Setup Drag and Drop Wrappers
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Setting up drag/drop...");
                DW = new CDragWrapper(_listView);
                DW.DragStart += DW_DragStart;
                DW.DragEnd += DW_DragEnd;
                DropWrap = new ClvDropWrapper(_listView);

                // Initialize the image-list coordinator.
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Initializing image-list orchestrator...");
                _imageListOrchestrator = new ImageListOrchestrator(this, _listView, DisplayMode, GetThumbnailSizeForMode());
                _imageListOrchestrator.ThumbnailReady += ThumbnailManager_ThumbnailReady;

                //set up sorter
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Initializing sorter...");
                _listViewWrapper.Initialize();
                SetImageListForMode(DisplayMode);
                _listViewWrapper.Sorter.SortOrderChanged += (s, e) =>
                {
                    if (VirtualMode)
                    {
                        _listViewWrapper.Sort(_listViewWrapper.Sorter.SortColumn, _listViewWrapper.Sorter.OrderOfSort);
                    }
                    SortOrderChanged?.Invoke(this, EventArgs.Empty); //what does this do?
                    OnScroll();
                };

                // Initialize thumbnail timer for lazy loading
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Setting up scroll debounce timer...");
                _scrollDebounceTimer = new System.Windows.Forms.Timer();
                _scrollDebounceTimer.Interval = 100;
                _scrollDebounceTimer.Tick += (s, e) =>
                {
                    _scrollDebounceTimer?.Stop();
                    if (IsDisposed || Disposing) return;
                    _imageListOrchestrator?.CancelPendingRequests();
                    LoadImagesForVisibleItems();
                };


                // Setup Change Notification
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: Wiring shell update events...");
                _shellController.ShellUpdater.UpdateEvent += ShellUpdater_UpdateEventInvoker;

                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpList_Load: End");
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpList_Load End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.HandleCreated"/> event of the <see cref="_listView"/> ListView.
        /// </summary>
        private void ExpFileList_HandleCreated(object? sender, EventArgs e)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpFileList_HandleCreated: Begin");
            try
            {
                _scrollHook = new ListViewScrollHook(_listViewWrapper, OnScroll);
            }
            finally
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.ExpFileList_HandleCreated: End");
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.VisibleChanged"/> event of the <see cref="ExpList"/> control.
        /// Re-configures image lists for the current display mode when the control becomes visible.
        /// </summary>
        private void ExpFileList_VisibleChanged(object? sender, EventArgs e)
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


        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    Cleanup();

                    if (components != null)
                    {
                        components.Dispose();
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public void Redraw(CShellItem csi)
        {
            int index = _listViewWrapper.GetIndex(csi);
            _listViewWrapper.RedrawItem(index);
        }

        public void Remove(CShellItem item)
        {
            var index = _listViewWrapper.GetIndex(item);
            if (index >= 0)
            {
                _listViewWrapper.RemoveAndRedrawAt(index);
            }
        }
    }


}
