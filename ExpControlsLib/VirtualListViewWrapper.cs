using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using TreeLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    /// <summary>
    /// Encapsulates the messy behavior of the ListView control when switching between
    /// Virtual and Regular modes. Provides a unified interface for data manipulation.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal class VirtualListViewWrapper
    {
        private const int BatchThreshold = 20;
        private readonly ExpList _expList;
        /// <summary>
        /// Cache of ListViewItems for virtual mode, keyed by index.  
        /// Note: it is important to update a given ListViewItems if the associated CShellItem changes, 
        /// otherwise the ListView will display stale data.
        /// </summary>
        private readonly Dictionary<int, ListViewItem> _itemCache = new(); 
        private readonly Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Provides a mapping from file name to ListViewItems in the listview.  
        /// Note: This can only be used in non-virtual mode beucase in virtual mode ListViewItems do not persist.
        /// </summary>
        private readonly Dictionary<string, ListViewItem> _indexPathToLvi = new(StringComparer.OrdinalIgnoreCase);
        private SortOrder _sortOrder = SortOrder.None;
        private int _sortColumn = 0;
        private SortOrder _prevSortOrder = SortOrder.None;
        private int _prevSortColumn = -1;
        private bool _inSort = false;

        /// <summary>
        /// The master list of all the items.
        /// </summary>
        private List<CShellItem>? _filteredView;

        public readonly ListView _listView;
        public readonly HugeList<CShellItem> Items = new();
        public bool IsShuttingDown;

        /// <summary>
        /// When <c>true</c>, the <see cref="ExpList"/> handler must ignore <c>ItemChecked</c>
        /// events fired by the inner <see cref="ListView"/>. Set while
        /// <see cref="CreateLviFromCsi"/> applies model state to a freshly materialized item,
        /// and while <see cref="SyncCheckedInCache"/> writes back to an already-cached item,
        /// to prevent feedback loops.
        /// </summary>
        internal bool SuppressCheckEvents;

        /// <summary>
        /// The filtered view of items. When non-null, this is what the ListView displays.
        /// When null, the ListView displays all items from <see cref="Items"/>.
        /// </summary>
        public List<CShellItem>? FilteredView => _filteredView;

        /// <summary>
        /// Gets a value indicating whether a filter is currently active.
        /// </summary>
        public bool IsFilterActive => _filteredView != null;

        /// <summary>
        /// Gets the collection that the ListView should read from.
        /// Returns the filtered view if active, otherwise the master list.
        /// </summary>
        private IEnumerable<CShellItem> ActiveView => _filteredView ?? (IEnumerable<CShellItem>)Items;

        /// <summary>
        /// Gets the number of items in the active view (filtered or master).
        /// </summary>
        public int ActiveViewCount => _filteredView?.Count ?? Items.Count;

        /// <summary>
        /// Gets the item at the specified index from the active view.
        /// </summary>
        private CShellItem GetItemFromActiveView(int index)
        {
            if (_filteredView != null)
                return _filteredView[index];
            return Items[index];
        }

        /// <summary>
        /// Gets the master list index for an item at the given active view index.
        /// When no filter is active, the view index equals the master index.
        /// </summary>
        public int GetMasterIndexFromViewIndex(int viewIndex)
        {
            if (_filteredView == null) return viewIndex;
            var item = _filteredView[viewIndex];
            return Items.IndexOf(item);
        }

        /// <summary>
        /// Sets or replaces the active filter. Pass null to clear the filter.
        /// Rebuilds the filtered view from the master list and updates the ListView.
        /// </summary>
        /// <param name="predicate">The filter predicate, or null to show all items.</param>
        public void SetFilter(Func<CShellItem, bool>? predicate)
        {
            if (predicate == null)
            {
                ClearFilter();
                return;
            }

            _filteredView = new List<CShellItem>();
            foreach (var item in Items)
            {
                if (predicate(item))
                    _filteredView.Add(item);
            }

            ApplyViewToListView();
        }

        /// <summary>
        /// Clears the active filter, showing all items from the master list.
        /// </summary>
        public void ClearFilter()
        {
            if (_filteredView == null) return;
            _filteredView = null;
            ApplyViewToListView();
        }

        /// <summary>
        /// Rebuilds the filtered view from the master list using the current filter predicate.
        /// Call this after modifying the master list (e.g., after column data is populated)
        /// so the filter can re-evaluate items with the new data.
        /// </summary>
        /// <param name="predicate">The filter predicate to re-apply.</param>
        public void RebuildFilter(Func<CShellItem, bool> predicate)
        {
            if (_filteredView == null) return; // no filter active, nothing to rebuild
            SetFilter(predicate);
        }

        /// <summary>
        /// Updates the ListView to reflect the current active view (filtered or master).
        /// </summary>
        private void ApplyViewToListView()
        {
            LastTopIndex = -1;
            _itemCache.Clear();
            RecreateIndexMapping();

            if (VirtualMode)
            {
                _listView.VirtualListSize = ActiveViewCount;
            }

            _listView.Invalidate();
        }

        /// <summary>
        /// Callback to create a new ListViewItem for a given CShellItem.
        /// </summary>
        public Func<CShellItem, ListViewItem> CreateListviewItemCallback { get; set; }

        /// <summary>
        /// Callback to update an existing ListViewItem with data from a CShellItem.
        /// </summary>
        public Action<ListViewItem, CShellItem> UpdateListviewItemCallback { get; set; }

        #region Properties
        public LVColSorter Sorter { get; private set; }
        public int SortColumn
        {
            get => _sortColumn;
            set
            {
                _prevSortColumn = _sortColumn;
                _sortColumn = value;
                if (!VirtualMode && Sorter != null)
                    Sorter.SortColumn = value;
            }
        }

        public SortOrder SortOrder
        {
            get => _sortOrder;
            set
            {
                _prevSortOrder = _sortOrder;
                _sortOrder = value;
                if (!VirtualMode && _listView != null)
                    _listView.Sorting = value;
            }
        }

        public SortOrder Sorting => SortOrder;


        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _listView.VirtualMode;
            set
            {
                if (_listView.VirtualMode == value) return;
                _listView.VirtualMode = value;

                if (value)
                {
                    _listView.RetrieveVirtualItem -= OnRetrieveVirtualItem; //just in case
                    _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
                    _listView.Items.Clear();
                    _indexPathToLvi.Clear();
                }
                else
                {
                    _listView.RetrieveVirtualItem -= OnRetrieveVirtualItem;
                    Items.Clear();
                    _filteredView = null;
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }
            }
        }

        public int Count => VirtualMode ? ActiveViewCount : _listView.Items.Count;

        public int SelectedCount => _listView.SelectedIndices.Count;

        public ListView.SelectedIndexCollection SelectedIndices => _listView.SelectedIndices;

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
            get;
            set
            {
                if (field == value) return;

                // WinForms bug: changing ListView.View while both CheckBoxes=true and
                // VirtualMode=true causes a handle recreation that silently resets
                // VirtualListSize to 0, making Details view appear completely empty.
                // Work around it by temporarily disabling virtual mode, changing the
                // view, then restoring virtual mode with the correct list size.
                bool needsGuard = VirtualMode && _listView.CheckBoxes;
                if (needsGuard)
                {
                    _listView.VirtualMode = false;
                    _listView.VirtualListSize = 0;
                }

                if (value <= ListViewDisplayMode.Tile) // View values native to the ListView control 
                {
                    _listView.View = (View)value;
                }
                else
                {
                    _listView.View = View.LargeIcon; //XP era kludge for thumbnail mode
                }

                if (needsGuard)
                {
                    _listView.VirtualMode = true;
                    _listView.VirtualListSize = ActiveViewCount;
                    // Cached LVIs are corrupted after a VirtualMode round-trip — the Win32
                    // virtual list rejects them silently. Discard them so fresh items are
                    // built on the next RetrieveVirtualItem.
                    _itemCache.Clear();
                }

                field = value;

                if (VirtualMode) InvalidateVirtualItemImagesIndexes();

                //SetImageListForMode(value);
                //if (VirtualMode) LoadImagesForItems();

                //DisplayModeChanged?.Invoke(value);
            }
        }

        #endregion

        public VirtualListViewWrapper(ExpList expList, ListView listView)
        {
            _expList = expList ?? throw new ArgumentNullException(nameof(expList));
            _listView = listView ?? throw new ArgumentNullException(nameof(listView));

            _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        }

        /// <summary>
        /// Stuff that can't be don't in the constructor because of unavailable dependencies.  
        /// This should be called in Control.Load().
        /// </summary>
        public void Initialize()
        {
            //create sorter.  this can't be done earlier because listview columns aren't available during the constructor
            Sorter = new LVColSorter(_listView);
            _listView.ListViewItemSorter = Sorter;
        }

        public void Clear()
        {
            Debug.WriteLine("VirtualListViewWrapper.Clear");
            _listView.SelectedIndices.Clear();
            LastTopIndex = -1;
            if (VirtualMode)
            {
                _listView.VirtualListSize = 0;
            }
            else
            {
                _listView.Items.Clear();
            }
            Items.Clear();
            _filteredView = null;
            _itemCache.Clear();
            _pathToIndex.Clear();
            _indexPathToLvi.Clear();
        }

        /// <summary>
        /// Currently, we're only using this to initialize the whole collection.  If we ever want to use this 
        /// only add a batch of items to an existing collection, we'll need to add some logic to handle 
        /// merging the new items with the existing ones in sorted order and etc.
        /// </summary>
        /// <param name="items"></param>
        public void AddRange(IEnumerable<CShellItem> items)
        {
            Debug.WriteLine("VirtualListViewWrapper.AddRange #" + items.Count());
            LastTopIndex = -1;
            if (VirtualMode)
            {
                Items.AddRange(items);
                ApplyViewToListView();
            }
            else
            {
                _listView.BeginUpdate();
                foreach (var item in items)
                {
                    var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                    _listView.Items.Add(lvi);
                    _indexPathToLvi[item.FullPath] = lvi;
                }
                _listView.EndUpdate();
            }
        }

        public void AddToEnd(CShellItem item)
        {
            Debug.WriteLine("VirtualListViewWrapper.Add - " + item.Text);
            LastTopIndex = -1;
            if (VirtualMode)
            {
                Items.Add(item);
                ApplyViewToListView();
            }
            else
            {
                var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _listView.Items.Add(lvi);
                _indexPathToLvi[item.FullPath] = lvi;
            }
        }

        /// <summary>
        /// Shifts cached ListViewItem objects after an item has been inserted into the list.
        /// This allows us to reuse existing ListViewItem objects for items that have merely shifted index.
        /// </summary>
        /// <param name="index">The index where the item was inserted.</param>
        private void ShiftCacheAfterInsertion(int index)
        {
            if (_itemCache.Count == 0) return;

            // Shift all items from the insertion point onwards up by one index
            var keysToShift = _itemCache.Keys.Where(k => k >= index).OrderByDescending(k => k).ToList();
            foreach (var k in keysToShift)
            {
                _itemCache[k + 1] = _itemCache[k];
                _itemCache.Remove(k);
            }
        }

        public void InsertSorted(CShellItem item)
        {
            Debug.WriteLine("VirtualListViewWrapper.InsertSorted - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            LastTopIndex = -1;
            int index = FindInsertionPoint(item);

            if (VirtualMode)
            {
                int masterIndex;
                lock (Items)
                {
                    if (_filteredView != null)
                    {
                        // When filtered, find the correct position in the master list
                        // by locating where the adjacent filtered items sit in the master.
                        masterIndex = FindMasterInsertionPointForFiltered(item, index);
                        Items.Insert(masterIndex, item);
                    }
                    else
                    {
                        masterIndex = index;
                        Items.Insert(index, item);
                    }

                    ShiftCacheAfterInsertion(masterIndex);
                }

                ApplyViewToListView();

                // Determine if we need to redraw visible items
                int viewIndex = index; // For filtered view, this is the position in the rebuilt view
                int top = GetTopIndex();
                int visibleCount = GetApproxVisibleCount();
                int lastVisible = top + visibleCount;

                // Only redraw if the insertion affects currently visible items or items that shift into view
                int startRedraw = Math.Max(viewIndex, Math.Max(0, top));
                int endRedraw = Math.Min(lastVisible, ActiveViewCount - 1);

                if (startRedraw <= endRedraw)
                {
                    _listView.RedrawItems(startRedraw, endRedraw, false);
                }
            }
            else
            {
                var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _listView.Items.Insert(index, lvi);
                _indexPathToLvi[item.FullPath] = lvi;
                lvi.EnsureVisible();
            }
        }

        /// <summary>
        /// Shifts cached ListViewItem objects after an item has been removed from the list.
        /// This allows us to reuse existing ListViewItem objects for items that have merely shifted index.
        /// </summary>
        /// <param name="index">The index where the item was removed.</param>
        private void ShiftCacheAfterRemoval(int index)
        {
            if (_itemCache.Count == 0) return;

            // Remove the deleted item from cache
            _itemCache.Remove(index);

            // Shift all subsequent items down by one index
            var keysToShift = _itemCache.Keys.Where(k => k > index).OrderBy(k => k).ToList();
            foreach (var k in keysToShift)
            {
                _itemCache[k - 1] = _itemCache[k];
                _itemCache.Remove(k);
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count) return;

            Debug.WriteLine("VirtualListViewWrapper.RemoveAt - " + DateTime.Now.ToString("HH:mm:ss.fff"));

            if (VirtualMode)
            {
                int masterIndex;
                CShellItem item;
                lock (Items)
                {
                    item = GetItemFromActiveView(index);
                    masterIndex = Items.IndexOf(item);
                    if (masterIndex < 0) return;

                    Items.RemoveAt(masterIndex);

                    ShiftCacheAfterRemoval(masterIndex);
                }

                ApplyViewToListView();

                // Reset viewport cache and determine if we need to redraw
                int top = GetTopIndex();
                int visibleCount = GetApproxVisibleCount();
                int lastVisible = top + visibleCount;

                // Only redraw if the removal affects currently visible items or items that shift into view
                int startRedraw = Math.Max(index, top);
                int endRedraw = Math.Min(lastVisible, ActiveViewCount - 1);

                if (startRedraw <= endRedraw)
                {
                    _listView.RedrawItems(startRedraw, endRedraw, false);
                }
            }
            else
            {
                var lvi = _listView.Items[index];
                if (lvi.Tag is CShellItem csi)
                    _indexPathToLvi.Remove(csi.FullPath);
                _listView.Items.RemoveAt(index);
            }
        }

        public void RemoveItems(IEnumerable<CShellItem> items)
        {
            if (items == null) return;
            var toRemove = new HashSet<CShellItem>(items);
            if (toRemove.Count == 0) return;

            if (toRemove.Count <= BatchThreshold)
            {
                try { 
                    _listView.SuspendLayout();
                    // Process small number of removals individually to avoid full redraw
                    var indices = new List<int>();
                    foreach (var item in items)
                    {
                        int index = GetIndex(item);
                        if (index >= 0) indices.Add(index);
                    }

                    // Remove in reverse order to avoid index shifting problems
                    indices.Sort((a, b) => b.CompareTo(a));
                    foreach (int index in indices)
                    {
                        RemoveAt(index);
                    }

                }
                finally
                {
                    _listView.ResumeLayout();
                }

                return;
            }

            if (VirtualMode)
            {
                lock (Items)
                {
                    // Rebuild master list in one pass, removing matched items
                    var remaining = new List<CShellItem>(Items.Count);
                    foreach (var item in Items)
                    {
                        if (!toRemove.Contains(item))
                        {
                            remaining.Add(item);
                        }
                        else
                        {
                            _pathToIndex.Remove(item.FullPath);
                        }
                    }

                    Items.Clear();
                    Items.AddRange(remaining);

                    // For large batches, it's safer and often faster to just clear the cache
                    _itemCache.Clear();
                }

                ApplyViewToListView();
                _listView.Invalidate();
            }
            else
            {
                _listView.BeginUpdate();
                try
                {
                    // Remove backwards to minimize shifting
                    for (int i = _listView.Items.Count - 1; i >= 0; i--)
                    {
                        if (_listView.Items[i].Tag is CShellItem csi && toRemove.Contains(csi))
                        {
                            _indexPathToLvi.Remove(csi.FullPath);
                            _listView.Items.RemoveAt(i);
                        }
                    }
                }
                finally
                {
                    _listView.EndUpdate();
                }
            }
        }

        public CShellItem? GetItem(int index)
        {
            if (index < 0) return null;

            if (VirtualMode)
            {
                if (index >= 0 && index < ActiveViewCount)
                    return GetItemFromActiveView(index);
            }
            else
            {
                if (index >= 0 && index < _listView.Items.Count)
                    return _listView.Items[index].Tag as CShellItem;
            }

            Debug.WriteLine("VirtualListViewWrapper.GetItem failed to get item at index " + index);
            return null;
        }

        public ListViewItem GetListViewItem(int index)
        {
            if (VirtualMode)
            {
                return GetLviFromVirtual(index);
            }
            else
            {
                if (index >= 0 && index < _listView.Items.Count)
                    return _listView.Items[index];
            }
            return null;
        }

        public bool IsItemSelected(CShellItem item)
        {
            if (item == null) return false;
            if (VirtualMode)
            {
                if (_pathToIndex.TryGetValue(item.FullPath, out int index))
                    return _listView.SelectedIndices.Contains(index);
                return false;
            }
            else
            {
                var lvi = FindLVItem(item);
                return lvi?.Selected ?? false;
            }
        }

        /// <summary>
        /// Finds the <see cref="ListViewItem"/> corresponding to a specific <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to search for.</param>
        /// <returns>The matching <see cref="ListViewItem"/>, or null if not found.</returns>
        /// <remarks>Only usable in non-virtual mode.</remarks>
        private ListViewItem? FindLVItem(CShellItem item)
        {
            //Debug.WriteLine("ExpList: FindLVItem Begin");
            if (VirtualMode)
            {
                return null;
            }

            try
            {
                if (_indexPathToLvi.TryGetValue(item.FullPath, out var lvi))
                    return lvi;
                return null;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindLVItem End");
            }
        }

        public int GetIndex(CShellItem item)
        {
            if (VirtualMode)
            {
                if (_pathToIndex.TryGetValue(item.FullPath, out int index))
                {
                    return index;
                }
                return -1;
            }
            else
            {
                return item.LVItem?.Index ?? -1;
            }
        }

        public int GetIndexFromFullPath(string fullPath)
        {
            if (VirtualMode)
            {
                if (_pathToIndex.TryGetValue(fullPath, out int index))
                    return index;
            }
            else
            {
                if (_indexPathToLvi.TryGetValue(fullPath, out var lvi))
                    return lvi.Index;
            }
            return -1;
        }

        /// <summary>
        /// Sets the sort column and order and the UI glyph without triggering an actual sort.
        /// This is useful to set at startup before the first location is loaded.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSortState(int column, SortOrder order)
        {
            _sortColumn = column;
            _sortOrder = order;
            Sorter?.SetSortState(column, order);
        }

        public void Sort()
        {
            Sort(_sortColumn, _sortOrder);
        }

        public void Sort(int column, SortOrder order)
        {
            Debug.WriteLine("VirtualListViewWrapper: Sort begin");
            if (_inSort) return;
            _inSort = true;
            LastTopIndex = -1;
            try
            {
                if (column != _sortColumn && _sortOrder != SortOrder.None)
                {
                    _prevSortColumn = _sortColumn;
                    _prevSortOrder = _sortOrder;
                }

                _sortColumn = column;
                _sortOrder = order;

                if (VirtualMode)
                {
                    SortVirtualItems(column, order);
                }

                // Update the sorter state so the UI (context menu, header glyph) reflects the current sort.
                // When in VirtualMode, we must do this explicitly because the sorter isn't sorting the items.
                // If NOT in VirtualMode, LVColSorter.SetSort will also perform the sort via m_View.Sort().
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                {
                    sorter.SetSort(column, order);
                }
                else if (!VirtualMode)
                {
                    _listView.Sort();
                }
            }
            finally
            {
                _inSort = false;
                Debug.WriteLine("VirtualListViewWrapper: Sort end");
            }
        }

        /// <summary>
        /// Redraws the item at the specified index in the ListView. In virtual mode, this triggers 
        /// a call to RetrieveVirtualItem for that index.
        /// </summary>
        /// <param name="index"></param>
        public void RedrawItem(int index)
        {
            if (VirtualMode)
            {
                if (index >= 0 && index < _listView.VirtualListSize)
                {
                    _listView.RedrawItems(index, index, false);
                }
            }
            else
            {
                if (index >= 0 && index < _listView.Items.Count)
                {
                    var lvi = _listView.Items[index];
                }
            }
        }

        /// <summary>
        /// Refreshes the ListViewItem corresponding to the given CShellItem whose data has changed. 
        /// In virtual mode, this updates the cached ListViewItem and triggers a redraw. 
        /// In non-virtual mode, it updates the existing ListViewItem directly.
        /// </summary>
        /// <param name="csi"></param>
        public void RefreshItem(CShellItem? csi)
        {
            if (csi is null) return;

            Debug.WriteLine("ExpList: RefreshItem Begin");

            try
            {
                ListViewItem lvi = null;
                int index = GetIndexFromFullPath(csi.FullPath);
                if (VirtualMode)
                {
                    if (_itemCache.ContainsKey(index)) {
                        lvi = _itemCache[index];
                        UpdateListviewItemCallback?.Invoke(lvi, csi);
                    }
                    else
                    {
                        lvi = CreateLviFromCsi(csi);
                        _itemCache[index] = lvi;
                    }
                }
                else
                {
                    if (index >= 0 && index < _listView.Items.Count)
                    {
                        lvi = _listView.Items[index];
                        UpdateListviewItemCallback?.Invoke(lvi, csi);
                    }
                }

                RedrawItem(index);
                csi.NeedsRefresh = false;
            }
            finally
            {
                //Debug.WriteLine("ExpList: RefreshItem End");
            }
        }

        public void RefreshItemByFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            Debug.WriteLine("ExpList: RefreshItemByFullPath Begin");
            try
            {
                int index = GetIndexFromFullPath(path);
                if (index >= 0)
                {
                    var csi = GetItem(index);
                    csi?.ColumnDic.Clear();
                    RedrawItem(index);
                }
            }
            finally
            {
                //Debug.WriteLine("ExpList: RefreshItemByFullPath End");
            }
        }

        public void RedrawAll()
        {
            if (VirtualMode)
            {
                _itemCache.Clear();
                _listView.Invalidate();
            }
            else
            {
                _listView.BeginUpdate();
                foreach (ListViewItem lvi in _listView.Items)
                {
                    UpdateListviewItemCallback?.Invoke(lvi, lvi.Tag as CShellItem);
                }
                _listView.EndUpdate();
            }
        }

        public void InvalidateCache()
        {
            _itemCache.Clear();
        }

        /// <summary>
        /// Returns true if the given item index is within the currently visible
        /// viewport. Uses <see cref="GetTopIndex"/> + <see cref="GetApproxVisibleCount"/>,
        /// which is O(1) and works in virtual mode without realizing items.
        /// Used to prevent LRU eviction of on-screen thumbnails.
        /// </summary>
        public bool IsItemVisible(int index)
        {
            if (index < 0) return false;
            int top = GetTopIndex();
            if (top < 0) return false;
            int visible = GetApproxVisibleCount();
            return index >= top && index < top + visible;
        }

        /// <summary>
        /// Invalidates the image indices of all virtual items and cached ListViewItems.
        /// This is necessary when switching between display modes to ensure that the correct
        /// icons or thumbnails are loaded for the current view.
        /// </summary>
        private void InvalidateVirtualItemImagesIndexes()
        {
            //Debug.WriteLine("ExpList: InvalidateVirtualItemIndexes Begin");
            try
            {
                if (!VirtualMode) return;

                foreach (var item in ActiveView)
                {
                    if (item != null) item.ImageIndex = -1;
                }

                foreach (var lvi in _itemCache.Values)
                {
                    if (lvi != null) lvi.ImageIndex = -1;
                }
            }
            finally
            {
                //Debug.WriteLine("ExpList: InvalidateVirtualItemIndexes End");
            }
        }

        /// <summary>
        /// Retrieves items for Windows while in virtual list view mode.  
        /// </summary>
        /// <remarks>
        /// Windows will send multiple requests for the same item for no particular reason.
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            //Console.WriteLine("Retrieve virtual item: " + e.ItemIndex);

            if (_expList.IsShuttingDown) 
            { 
                e.Item = new ListViewItem(); //send back a dummy or else the whole program will crash
                return; //windows tries to retrieve every item during shutdown for some reason
            }

            bool isThumbnailMode = IsThumbnailViewMode();
            if (isThumbnailMode) _expList.EnterImageListMutation();
            try
            {
                var lvi = GetLviFromVirtual(e.ItemIndex);
                if (lvi is null)
                {
                    e.Item = new ListViewItem(); //send back a dummy
                }
                else
                    e.Item = lvi;
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Error in OnRetrieveVirtualItem: " + ex.Message);
                e.Item = new ListViewItem(); //send back a dummy to avoid crashing the ListView
            }
            finally
            {
                if (isThumbnailMode) _expList.ExitImageListMutation();
            }
        }

        public ListViewItem GetLviFromVirtual(int index)
        {
            if (index < 0 || index >= ActiveViewCount) return null;

            var item = GetItemFromActiveView(index);

            if (item.NeedsRefresh) //item has been updated in the background and needs to be recreated as a new ListViewItem to reflect changes
            {
                Debug.WriteLine("VirtualListViewWrapper.GetLviFromVirtual needs refresh - " + item.Text);
                var lvi = CreateLviFromCsi(item);
                _itemCache[index] = lvi;
                return lvi;
            }
            else
            {
                if (_itemCache.TryGetValue(index, out var lvi))
                {
                    // Sync ImageIndex if it was updated in the background while item was cached.
                    // Bidirectional: also pick up when item.ImageIndex has been reset to -1
                    // (e.g. by LRU eviction in ThumbnailImageListManager) so that the cached
                    // lvi doesn't keep pointing at a slot whose image has been replaced by
                    // another item's thumbnail.
                    if (lvi.ImageIndex != item.ImageIndex)
                    {
                        lvi.ImageIndex = item.ImageIndex;
                    }
                    
                    return lvi;
                }
                else
                {
                    //Debug.WriteLine("VirtualListViewWrapper.GetLviFromVirtual failed to get item #" + index.ToString() + " from cache - " + item.Text);
                    lvi = CreateLviFromCsi(item);
                    _itemCache[index] = lvi;
                    return lvi;
                }
            }
        }

        private ListViewItem CreateLviFromCsi(CShellItem item)
        {
            var lvi = CreateListviewItemCallback?.Invoke(item);
            if (lvi == null)
            {   //this shouldn't ever happen, but just in case the callback fails, create a basic ListViewItem to avoid crashing the ListView
                lvi = new ListViewItem(item.DisplayName) { Tag = item };
                foreach (var col in _listView.Columns)
                {
                    Debug.WriteLine("Failed to create listview item");

                    var si = new ListViewItem.ListViewSubItem();
                    si.Text = string.Empty;
                    si.Tag = null;
                    lvi.SubItems.Add(si); // Placeholder for subitems, UpdateItemCallback should fill these in
                }
            }
            item.NeedsRefresh = false;

            // Sync checkbox visual state from the model without triggering the ItemChecked handler.
            SuppressCheckEvents = true;
            try   { lvi.Checked = item.Checked; }
            finally { SuppressCheckEvents = false; }

            return lvi;
        }

        /// <summary>
        /// Returns the <see cref="CShellItem"/> at <paramref name="viewIndex"/> in the active
        /// view. Works in both virtual and non-virtual modes. Returns <c>null</c> if out of range.
        /// </summary>
        internal CShellItem? GetShellItemAtViewIndex(int viewIndex)
        {
            if (viewIndex < 0) return null;
            if (VirtualMode)
            {
                if (viewIndex >= ActiveViewCount) return null;
                return GetItemFromActiveView(viewIndex);
            }
            else
            {
                if (viewIndex >= _listView.Items.Count) return null;
                return _listView.Items[viewIndex].Tag as CShellItem;
            }
        }

        /// <summary>
        /// Enumerates every <see cref="CShellItem"/> regardless of virtual/non-virtual mode.
        /// In virtual mode returns <see cref="Items"/>; in non-virtual mode reads <c>Tag</c>
        /// from each <see cref="ListView.Items"/> entry.
        /// </summary>
        internal IEnumerable<CShellItem> AllShellItems
        {
            get
            {
                if (VirtualMode)
                    return Items;
                return _listView.Items
                    .Cast<ListViewItem>()
                    .Select(lvi => lvi.Tag as CShellItem)
                    .Where(csi => csi != null)!;
            }
        }

        /// <summary>
        /// Updates the cached <see cref="ListViewItem"/> at <paramref name="viewIndex"/> to
        /// reflect a programmatic checked-state change without triggering
        /// <see cref="ExpList"/>'s <c>ItemChecked</c> handler.
        /// In non-virtual mode updates the live <see cref="ListView.Items"/> entry directly.
        /// </summary>
        internal void SyncCheckedInCache(int viewIndex, bool value)
        {
            if (viewIndex < 0) return;

            if (VirtualMode)
            {
                if (_itemCache.TryGetValue(viewIndex, out var lvi))
                {
                    SuppressCheckEvents = true;
                    try   { lvi.Checked = value; }
                    finally { SuppressCheckEvents = false; }
                }
                // Not cached: next RetrieveVirtualItem will apply item.Checked automatically.
            }
            else
            {
                if (viewIndex < _listView.Items.Count)
                {
                    SuppressCheckEvents = true;
                    try   { _listView.Items[viewIndex].Checked = value; }
                    finally { SuppressCheckEvents = false; }
                }
            }
        }

        private void UpdateVirtualListSize()
        {
            _listView.VirtualListSize = ActiveViewCount;
        }

        private void RecreateIndexMapping()
        {
            _pathToIndex.Clear();
            int i = 0;
            foreach (var item in ActiveView)
            {
                _pathToIndex[item.FullPath] = i;
                i++;
            }
        }

        private (int index, ColumnHeader header) GetDisplayNameColumn()
        {
            for (int i = 0; i < _listView.Columns.Count; i++)
            {
                var col = _listView.Columns[i];
                if (col.Tag?.ToString().Trim() == ".DisplayName")
                {
                    return (i, col);
                }
            }
            if (_listView.Columns.Count > 0)
            {
                return (0, _listView.Columns[0]);
            }
            return (-1, null);
        }

        private CShellItemComparer GetSecondaryComparer(int primaryColumn)
        {
            if (primaryColumn >= 0 && primaryColumn < _listView.Columns.Count)
            {
                var primCol = _listView.Columns[primaryColumn];
                string primMapping = primCol.Tag?.ToString().Trim() ?? string.Empty;
                if (primMapping.StartsWith(".") && primMapping.Substring(1) == "DisplayName")
                {
                    return null;
                }
            }

            int secColIndex = -1;
            SortOrder secOrder = SortOrder.None;
            ColumnHeader secColHeader = null;

            if (_prevSortColumn >= 0 && _prevSortColumn < _listView.Columns.Count && _prevSortOrder != SortOrder.None)
            {
                secColIndex = _prevSortColumn;
                secOrder = _prevSortOrder;
                secColHeader = _listView.Columns[secColIndex];
            }
            else
            {
                var dn = GetDisplayNameColumn();
                if (dn.index >= 0)
                {
                    secColIndex = dn.index;
                    secOrder = SortOrder.Ascending;
                    secColHeader = dn.header;
                }
            }

            if (secColIndex >= 0 && secColHeader != null && secOrder != SortOrder.None)
            {
                return new CShellItemComparer(_expList, secColIndex, secOrder, secColHeader, null);
            }

            return null;
        }

        private void SortVirtualItems(int column, SortOrder order)
        {
            if (order == SortOrder.None || ActiveViewCount == 0) return;

            if (column < 0 || column >= _listView.Columns.Count) return;

            // Save selection and focused item before sort so we can restore after
            var selectedPaths = new List<string>();
            foreach (int idx in _listView.SelectedIndices)
            {
                var item = GetItem(idx);
                if (item != null) selectedPaths.Add(item.FullPath);
            }
            string? focusedPath = null;
            if (_listView.FocusedItem != null)
            {
                var focused = GetItem(_listView.FocusedItem.Index);
                if (focused != null) focusedPath = focused.FullPath;
            }

            var colHeader = _listView.Columns[column];
            var secondaryComparer = GetSecondaryComparer(column);
            var comparer = new CShellItemComparer(_expList, column, order, colHeader, secondaryComparer);

            if (_filteredView != null)
            {
                // Sort the filtered view directly
                _filteredView.Sort(comparer);
            }
            else
            {
                // Sort the master list: copy to List for sorting because HugeList (B-Tree) sort is impractical in-place
                var list = new List<CShellItem>((int)Items.Count);
                foreach (var item in Items)
                {
                    list.Add(item);
                }

                list.Sort(comparer);

                _listView.BeginUpdate();
                Items.Clear();
                Items.AddRange(list);
                _listView.EndUpdate();
            }

            RecreateIndexMapping();
            _itemCache.Clear();
            _listView.Refresh();

            // Restore selection by finding the new indices of the previously selected items
            if (selectedPaths.Count > 0)
            {
                _listView.SelectedIndices.Clear();
                int firstRestored = -1;
                foreach (var path in selectedPaths)
                {
                    if (_pathToIndex.TryGetValue(path, out int newIndex))
                    {
                        _listView.SelectedIndices.Add(newIndex);
                        if (firstRestored < 0) firstRestored = newIndex;
                    }
                }

                // Restore focused item and ensure it's visible
                if (focusedPath != null && _pathToIndex.TryGetValue(focusedPath, out int focusedIndex))
                {
                    _listView.FocusedItem = _listView.Items[focusedIndex];
                    _listView.EnsureVisible(focusedIndex);
                }
                else if (firstRestored >= 0)
                {
                    _listView.EnsureVisible(firstRestored);
                }
            }
        }

        /// <summary>
        /// Finds the insertion point for a new item in a sorted HugeList using the built-in BinarySearch method.
        /// Returns the index where the item should be inserted to maintain sorted order.
        /// When a filter is active, returns the index within the filtered view.
        /// </summary>
        /// <param name="item">The item to find an insertion point for</param>
        /// <returns>The index where the item should be inserted in the active view</returns>
        public int FindInsertionPoint(CShellItem item)
        {
            if (_sortOrder == SortOrder.None || _sortColumn < 0 || _sortColumn >= _listView.Columns.Count)
                return ActiveViewCount;

            var colHeader = _listView.Columns[_sortColumn];
            var secondaryComparer = GetSecondaryComparer(_sortColumn);
            var comparer = new CShellItemComparer(_expList, _sortColumn, _sortOrder, colHeader, secondaryComparer);

            if (VirtualMode)
            {
                if (_filteredView != null)
                {
                    // Binary search on the filtered list (it's a regular List<CShellItem>)
                    int low = 0;
                    int high = _filteredView.Count - 1;

                    while (low <= high)
                    {
                        int mid = low + ((high - low) / 2);
                        int compareResult = comparer.Compare(item, _filteredView[mid]);

                        if (compareResult == 0)
                            return mid;
                        else if (compareResult < 0)
                            high = mid - 1;
                        else
                            low = mid + 1;
                    }
                    return low;
                }
                else
                {
                    long result = Items.BinarySearch(0, Items.Count, item, comparer);
                    return (int)(result < 0 ? ~result : result);
                }
            }
            else
            {
                // Binary search on ListView.Items
                int low = 0;
                int high = _listView.Items.Count - 1;

                while (low <= high)
                {
                    int mid = low + ((high - low) / 2);
                    var midCsi = _listView.Items[mid].Tag as CShellItem;
                    int compareResult = comparer.Compare(item, midCsi);

                    if (compareResult == 0)
                        return mid;
                    else if (compareResult < 0)
                        high = mid - 1;
                    else
                        low = mid + 1;
                }
                return low;
            }
        }

        /// <summary>
        /// Finds the correct insertion index in the master list for a new item, given its
        /// position within the filtered view. Uses the filtered view's sorted order to determine
        /// where the item belongs relative to the master list's existing items.
        /// </summary>
        /// <param name="item">The item to insert.</param>
        /// <param name="filteredInsertIndex">The index in the filtered view where the item would be inserted.</param>
        /// <returns>The index in the master list where the item should be inserted.</returns>
        private int FindMasterInsertionPointForFiltered(CShellItem item, int filteredInsertIndex)
        {
            // If inserting at the start of the filtered view, find the master index of the first filtered item
            // and insert before it. If inserting at the end, find the master index of the last filtered item
            // and insert after it. Otherwise, insert at the master index of the adjacent filtered item.
            if (_filteredView == null || _filteredView.Count == 0)
            {
                return Items.Count;
            }

            if (filteredInsertIndex >= _filteredView.Count)
            {
                // Inserting after the last filtered item
                var lastFiltered = _filteredView[_filteredView.Count - 1];
                int masterIdx = Items.IndexOf(lastFiltered);
                return masterIdx >= 0 ? masterIdx + 1 : Items.Count;
            }
            else
            {
                // Inserting before the item at filteredInsertIndex
                var nextFiltered = _filteredView[filteredInsertIndex];
                int masterIdx = Items.IndexOf(nextFiltered);
                return masterIdx >= 0 ? masterIdx : Items.Count;
            }
        }

        /// <summary>
        /// Determines if the current display mode is a thumbnail-based view.
        /// </summary>
        /// <returns>True if in a thumbnail view mode.</returns>
        public bool IsThumbnailViewMode() => DisplayMode == ListViewDisplayMode.Thumbnail || DisplayMode == ListViewDisplayMode.LargeThumbnail || DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail;

        private const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        private const int LVM_GETITEMRECT = LVM_FIRST + 14;
        private const int LVM_HITTEST = LVM_FIRST + 18;
        private const int LVM_GETITEMSPACING = LVM_FIRST + 51; // returns packed x/y in LPARAM
        private const int LVM_GETTOPINDEX = LVM_FIRST + 39;
        private const int LVM_GETORIGIN = LVM_FIRST + 41; // icon/tile views only; lParam -> POINT
        private const int LVNI_VISIBLE = 0x0008;
        private const int LVIR_BOUNDS = 0; // for LVM_GETITEMRECT
        private const int LVM_GETCOUNTPERPAGE = 0x1000 + 40;

        /// <summary>
        /// Cache the last top index value for a short period of time because windows will sometimes 
        /// ask for it repeatedly.
        /// </summary>
        private DateTime _lastTopIndexDate = DateTime.MinValue;
        private static TimeSpan expirationAge = new TimeSpan(1000000); //100 ms
        public int LastTopIndex
        {
            get
            {
                if (DateTime.Now - _lastTopIndexDate > expirationAge)
                {
                    field = -1;
                }

                return field;
            }
            set
            {
                field = value;
                _lastTopIndexDate = DateTime.Now;
            }
        }

        /// <summary>
        /// Returns a "top-like" index for any ListView mode.
        /// - Details/List: effectively top row index
        /// - LargeIcon/SmallIcon/Tile: top-left visible item index
        /// Works in virtual and non-virtual mode.
        /// </summary>
        public int GetTopIndex()
        {
            Debug.WriteLine("ExpList: GetTopIndex Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated) return -1;

                int total = _listView.VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                if (total <= 0) return -1;

                if (LastTopIndex > -1) return LastTopIndex; // cache for repeated calls.  The OS will sometimes make tons of redundant calls in a brief amount of time.

                var view = _listView.View;

                // 1) Fast O(1) path for Details/List views.
                if (view == View.Details || view == View.List)
                {
                    if (!_listView.VirtualMode && _listView.TopItem != null)
                    {
                        LastTopIndex = _listView.TopItem.Index;
                        return LastTopIndex;
                    }

                    int byTopIndex = FindTopLeftByTopIndex(total);
                    if (byTopIndex >= 0) { LastTopIndex = byTopIndex; return byTopIndex; }
                }

                // 2) Fast O(1) path for icon-grid views (SmallIcon, LargeIcon, Tile).
                if (view == View.SmallIcon || view == View.LargeIcon || view == View.Tile)
                {
                    int byOrigin = FindTopLeftByOrigin(total);
                    if (byOrigin >= 0) { 
                        LastTopIndex = byOrigin;
                        Debug.WriteLine("\tFound TopLeft by FindTopLeftByOrigin.");
                        return byOrigin; 
                    }

                    int bySingleHit = FindTopLeftBySingleHitTest(total);
                    if (bySingleHit >= 0) { LastTopIndex = bySingleHit;
                        Debug.WriteLine("\tFound TopLeft by FindTopLeftBySingleHitTest.");
                        return bySingleHit; }
                }

                // 3) Visible enumeration (works in many non-virtual cases)
                int byVisibleEnum = FindTopLeftByVisibleEnumeration(total);
                if (byVisibleEnum >= 0) { LastTopIndex = byVisibleEnum;
                    Debug.WriteLine("\tFound TopLeft by FindTopLeftByVisibleEnumeration.");
                    return byVisibleEnum; }

                // 4) Last-resort fallback: scan viewport by hit-test
                int byHitTestScan = FindTopLeftByHitTestScan(total);
                if (byHitTestScan >= 0) { LastTopIndex = byHitTestScan;
                    Debug.WriteLine("\tFound TopLeft by FindTopLeftByHitTestScan.");
                    return byHitTestScan; }

                // 5) Absolute fallback
                Debug.WriteLine("\tFailed to find topleft.");

                LastTopIndex = 0;
                return LastTopIndex;
            }
            finally
            {
                //Debug.WriteLine("ExpList: GetTopIndex End");
            }
        }

        /// <summary>
        /// Uses LVM_GETTOPINDEX to return the topmost visible item in Details/List views.
        /// This is a single message call, O(1), and virtual-mode safe.
        /// </summary>
        private int FindTopLeftByTopIndex(int total)
        {
            Debug.WriteLine("ExpList: FindTopLeftByTopIndex Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                int idx = (int)SendMessage(_listView.Handle, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
                if (idx >= 0 && idx < total) return idx;
                return -1;
            }
            finally
            {
                Debug.WriteLine("ExpList: FindTopLeftByTopIndex End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Computes the top-left visible item for icon-grid views (SmallIcon, LargeIcon, Tile)
        /// from the viewport scroll origin (LVM_GETORIGIN) and the per-item cell spacing
        /// (LVM_GETITEMSPACING). Two message calls, O(1), virtual-mode safe.
        /// </summary>
        private int FindTopLeftByOrigin(int total)
        {
            Debug.WriteLine("ExpList: FindTopLeftByOrigin Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                POINT origin = new POINT();
                IntPtr res = SendMessage(_listView.Handle, LVM_GETORIGIN, IntPtr.Zero, ref origin);
                if (res == IntPtr.Zero) return -1; // message unsupported / failed

                bool largeIcon = (_listView.View == View.LargeIcon);
                int packed = (int)SendMessage(_listView.Handle, LVM_GETITEMSPACING,
                    largeIcon ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);
                int cellW = packed & 0xFFFF;
                int cellH = (packed >> 16) & 0xFFFF;

                if (cellW <= 0 || cellH <= 0) return -1;

                int vw = Math.Max(1, _listView.ClientSize.Width);
                int cols = Math.Max(1, (int)Math.Floor(vw / (float)cellW));

                int row = Math.Max(0, origin.y / cellH);
                int col = Math.Max(0, origin.x / cellW);

                int idx = row * cols + col;
                if (idx < 0 || idx >= total) return -1;
                return idx;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindTopLeftByOrigin End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Single LVM_HITTEST probe near the top-left of the client area. One message call,
        /// O(1). Used as a fast fallback when the grid-math path is unavailable for icon views.
        /// </summary>
        private int FindTopLeftBySingleHitTest(int total)
        {
            Debug.WriteLine("ExpList: FindTopLeftBySingleHitTest Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                var client = _listView.ClientRectangle;
                if (client.Width <= 0 || client.Height <= 0) return -1;

                int half = Math.Max(3, GetSizeForDisplayMode() / 2);

                // Probe a small fixed set of points just inside the top-left corner.
                int[] xs = { half / 2, half, half + half / 2 };
                int[] ys = { half / 2, half, half + half / 2 };

                int bestIndex = -1;
                int bestTop = int.MaxValue;
                int bestLeft = int.MaxValue;

                foreach (int y in ys)
                {
                    if (y >= client.Height) break;
                    foreach (int x in xs)
                    {
                        if (x >= client.Width) break;
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
                        else if (bestIndex < 0)
                        {
                            bestIndex = idx;
                            bestTop = y;
                            bestLeft = x;
                        }
                    }
                }

                return bestIndex;
            }
            finally
            {
                Debug.WriteLine("ExpList: FindTopLeftBySingleHitTest End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        private int FindTopLeftByVisibleEnumeration(int total)
        {
            Debug.WriteLine("ExpList: FindTopLeftByVisibleEnumeration Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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
                Debug.WriteLine("ExpList: FindTopLeftByVisibleEnumeration End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Tries to find the first item visible in the ListView's current viewport.  Works in both list and icon view modes.
        /// </summary>
        /// <param name="listCount">the number of items in the listview</param>
        /// <returns>index number of what is believed to be the first top-left most item.</returns>
        private int FindTopLeftByHitTestScan(int listCount)
        {
            Debug.WriteLine("ExpList: FindTopLeftByHitTestScan Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                var client = _listView.ClientRectangle;
                if (client.Width <= 0 || client.Height <= 0) return -1;

                //int step = Math.Max(6, _listView.Font.Height / 2);
                int step = Math.Max(6, GetSizeForDisplayMode() / 2);

                int bestIndex = -1;
                int bestTop = int.MaxValue;
                int bestLeft = int.MaxValue;

                for (int y = 0; y < client.Height; y += step)
                {
                    for (int x = 0; x < client.Width; x += step)
                    {
                        int idx = HitTestIndex(x, y);
                        if (idx < 0 || idx >= listCount) continue;

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
                Debug.WriteLine("ExpList: FindTopLeftByHitTestScan End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        private int HitTestIndex(int x, int y)
        {
            //Debug.WriteLine("ExpList: HitTestIndex Begin");
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
                //Debug.WriteLine("ExpList: HitTestIndex End");
            }
        }

        public int GetApproxVisibleCount()
        {
            Debug.WriteLine("ExpList: GetApproxVisibleCount Begin");
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
                Debug.WriteLine("ExpList: GetApproxVisibleCount End");
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

        private int GetApproxVisibleCountLargeIcon()
        {
            Debug.WriteLine("ExpList: GetApproxVisibleCountLargeIcon Begin");
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
                //Debug.WriteLine("ExpList: GetApproxVisibleCountLargeIcon End");
            }
        }

        /// <summary>
        /// Gets the approximate pixel size for an item based on the current display mode.  
        /// This is used to determine how aggressively to scan for visible items when calculating 
        /// the top index.
        /// </summary>
        /// <returns></returns>
        /// <remarks>todo: take into account display scale factor</remarks>   
        /// <exception cref="Exception"></exception>
        private int GetSizeForDisplayMode()
        {
            return DisplayMode switch
            {
                ListViewDisplayMode.Details => _listView.Font.Height,
                ListViewDisplayMode.SmallIcon => 16,
                ListViewDisplayMode.Tile => 32,
                ListViewDisplayMode.Thumbnail => 48,
                ListViewDisplayMode.LargeIcon => 96,
                ListViewDisplayMode.LargeThumbnail => 96,
                ListViewDisplayMode.ExtraLargeThumbnail => 256,
                _ => throw new Exception("GetSizeForDisplayMode: Unsupported display mode")
            };  
        }

        ///// <summary>
        ///// Configures the image lists bound to the ListView for the given display mode.
        ///// For built-in Windows view modes (Details, List, LargeIcon, Tile), the system image
        ///// list is applied and each item's <see cref="ListViewItem.ImageIndex"/> is refreshed.
        ///// For custom thumbnail modes, the ListView is switched to LargeIcon view and
        ///// <see cref="LoadThumbnailsForItems"/> is called to populate thumbnail images.
        ///// </summary>
        ///// <param name="value">The <see cref="ListViewDisplayMode"/> to configure for.</param>
        //private void SetImageListForMode(ListViewDisplayMode value)
        //{
        //    Debug.WriteLine("ExpList: SetAndLoadImageList Begin");
        //    try
        //    {
        //        if (value <= ListViewDisplayMode.Tile) //built-in Windows 95 Shell view modes
        //        {
        //            bool large = (value == ListViewDisplayMode.LargeIcon);

        //            if (large)
        //                SystemImageListManager.SetListViewImageList(_listView, true, false);
        //            else
        //                SystemImageListManager.SetListViewImageList(_listView, false, false);
        //        }
        //        else //custom thumbnail view modes
        //        {
        //            _thumbnailManager.SetImageListSize(GetThumbnailSizeForMode(value));
        //        }
        //    }
        //    finally
        //    {
        //        Debug.WriteLine("ExpList: SetAndLoadImageList End");
        //    }
        //}

        ///// <summary>
        ///// Gets the pixel size for a given thumbnail display mode
        ///// </summary>
        //private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        //{
        //    //Debug.WriteLine("ExpList: GetThumbnailSizeForMode Begin");
        //    try
        //    {
        //        mode ??= DisplayMode;
        //        return mode switch
        //        {
        //            ListViewDisplayMode.Thumbnail => 48,
        //            ListViewDisplayMode.LargeThumbnail => 96,
        //            ListViewDisplayMode.ExtraLargeThumbnail => 256,
        //            _ => 48 // Default to 48 for non-thumbnail modes, though this should never be used
        //        };
        //    }
        //    finally
        //    {
        //        //Debug.WriteLine("ExpList: GetThumbnailSizeForMode End");
        //    }
        //}

    }
}
