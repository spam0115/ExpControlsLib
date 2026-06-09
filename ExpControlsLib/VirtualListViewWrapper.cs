using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using TreeLib;
using WindowsApiLib;
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
        private readonly ExpList _expList;
        public readonly ListView _ListView;
        private readonly HugeList<CShellItem> _virtualItems = new();
        private readonly Dictionary<int, ListViewItem> _itemCache = new();
        private readonly Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _itemIndex = new(StringComparer.OrdinalIgnoreCase);

        private SortOrder _sortOrder = SortOrder.None;
        private int _sortColumn = 0;
        private SortOrder _prevSortOrder = SortOrder.None;
        private int _prevSortColumn = -1;
        private bool _inSort = false;

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
                if (!VirtualMode && _ListView != null)
                    _ListView.Sorting = value;
            }
        }

        public SortOrder Sorting => SortOrder;

        /// <summary>
        /// Callback to create a new ListViewItem for a given CShellItem.
        /// </summary>
        public Func<CShellItem, ListViewItem> CreateListviewItemCallback { get; set; }

        /// <summary>
        /// Callback to update an existing ListViewItem with data from a CShellItem.
        /// </summary>
        public Action<ListViewItem, CShellItem> UpdateListviewItemCallback { get; set; }

        public VirtualListViewWrapper(ExpList expList, ListView listView)
        {
            _expList = expList ?? throw new ArgumentNullException(nameof(expList));
            _ListView = listView ?? throw new ArgumentNullException(nameof(listView));

            _ListView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        }

        /// <summary>
        /// Stuff that can't be don't in the constructor because of unavailable dependencies.  
        /// This should be called in Control.Load().
        /// </summary>
        public void Initialize()
        {
            //create sorter.  this can't be done earlier because listview columns aren't available during the constructor
            Sorter = new LVColSorter(_ListView);
            _ListView.ListViewItemSorter = Sorter;
        }

        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _ListView.VirtualMode;
            set
            {
                if (_ListView.VirtualMode == value) return;
                _ListView.VirtualMode = value;

                if (value)
                {
                    _ListView.RetrieveVirtualItem -= OnRetrieveVirtualItem; //just in case
                    _ListView.RetrieveVirtualItem += OnRetrieveVirtualItem;
                    _ListView.Items.Clear();
                    _itemIndex.Clear();
                }
                else
                {
                    _ListView.RetrieveVirtualItem -= OnRetrieveVirtualItem;
                    _virtualItems.Clear();
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }
            }
        }

        public int Count => VirtualMode ? _virtualItems.Count : _ListView.Items.Count;

        public int SelectedCount => _ListView.SelectedIndices.Count;

        public ListView.SelectedIndexCollection SelectedIndices => _ListView.SelectedIndices;

        public IEnumerable<CShellItem> SelectedCShellItems
        {
            get
            {
                foreach (int index in _ListView.SelectedIndices)
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
                if (value <= ListViewDisplayMode.Tile) // View values native to the ListView control 
                {
                    _ListView.View = (View)value;
                }
                else
                {
                    _ListView.View = View.LargeIcon; //XP era kludge for thumbnail mode
                }
                field = value;

                if (VirtualMode) InvalidateVirtualItemImagesIndexes();

                //SetImageListForMode(value);
                //if (VirtualMode) LoadImagesForItems();

                //DisplayModeChanged?.Invoke(value);
            }
        }

        public void Clear()
        {
            Debug.WriteLine("VirtualListViewWrapper.Clear");
            _ListView.SelectedIndices.Clear();
            LastTopIndex = -1;
            if (VirtualMode)
            {
                _ListView.VirtualListSize = 0;
            }
            else
            {
                _ListView.Items.Clear();
            }
            _virtualItems.Clear();
            _itemCache.Clear();
            _pathToIndex.Clear();
            _itemIndex.Clear();
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
                _virtualItems.AddRange(items);
                UpdateVirtualListSize();
                RecreateIndexMapping();
            }
            else
            {
                _ListView.BeginUpdate();
                foreach (var item in items)
                {
                    var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                    _ListView.Items.Add(lvi);
                    _itemIndex[item.FullPath] = lvi;
                }
                _ListView.EndUpdate();
            }
        }

        public void AddToEnd(CShellItem item)
        {
            Debug.WriteLine("VirtualListViewWrapper.Add - " + item.Text);
            LastTopIndex = -1;
            if (VirtualMode)
            {
                _virtualItems.Add(item);
                UpdateVirtualListSize();
                _pathToIndex[item.FullPath] = _virtualItems.Count - 1;
            }
            else
            {
                var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _ListView.Items.Add(lvi);
                _itemIndex[item.FullPath] = lvi;
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
                lock (_virtualItems)
                {
                    _virtualItems.Insert(index, item);
                    UpdateVirtualListSize();

                    // Efficiently update index mapping for the new item and shifted items
                    for (int i = index; i < _virtualItems.Count; i++)
                    {
                        _pathToIndex[_virtualItems[i].FullPath] = i;
                    }

                    // Shift cache to reuse existing ListViewItem objects
                    ShiftCacheAfterInsertion(index);
                }

                // Determine if we need to redraw visible items
                int top = GetTopIndex();
                int visibleCount = GetApproxVisibleCount();
                int lastVisible = top + visibleCount;

                // Only redraw if the insertion affects currently visible items or items that shift into view
                int startRedraw = Math.Max(index, Math.Max(0, top));
                int endRedraw = Math.Min(lastVisible, (int)_virtualItems.Count - 1);

                if (startRedraw <= endRedraw)
                {
                    _ListView.RedrawItems(startRedraw, endRedraw, false);
                }
            }
            else
            {
                var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _ListView.Items.Insert(index, lvi);
                _itemIndex[item.FullPath] = lvi;
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
                lock (_virtualItems)
                {
                    var item = _virtualItems[index];
                    _virtualItems.RemoveAt(index);
                    UpdateVirtualListSize();

                    // Efficiently update index mapping for shifted items
                    _pathToIndex.Remove(item.FullPath);
                    for (int i = index; i < _virtualItems.Count; i++)
                    {
                        _pathToIndex[_virtualItems[i].FullPath] = i;
                    }

                    // Shift cache to reuse existing ListViewItem objects
                    ShiftCacheAfterRemoval(index);
                }

                // Reset viewport cache and determine if we need to redraw
                LastTopIndex = -1;
                int top = GetTopIndex();
                int visibleCount = GetApproxVisibleCount();
                int lastVisible = top + visibleCount;

                // Only redraw if the removal affects currently visible items or items that shift into view
                int startRedraw = Math.Max(index, top);
                int endRedraw = Math.Min(lastVisible, (int)_virtualItems.Count - 1);

                if (startRedraw <= endRedraw)
                {
                    _ListView.RedrawItems(startRedraw, endRedraw, false);
                }
            }
            else
            {
                var lvi = _ListView.Items[index];
                if (lvi.Tag is CShellItem csi)
                    _itemIndex.Remove(csi.FullPath);
                _ListView.Items.RemoveAt(index);
            }
        }

        public CShellItem? GetItem(int index)
        {
            if (VirtualMode)
            {
                if (index >= 0 && index < _virtualItems.Count)
                    return _virtualItems[index];
            }
            else
            {
                if (index >= 0 && index < _ListView.Items.Count)
                    return _ListView.Items[index].Tag as CShellItem;
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
                if (index >= 0 && index < _ListView.Items.Count)
                    return _ListView.Items[index];
            }
            return null;
        }

        public bool IsItemSelected(CShellItem item)
        {
            if (item == null) return false;
            if (VirtualMode)
            {
                if (_pathToIndex.TryGetValue(item.FullPath, out int index))
                    return _ListView.SelectedIndices.Contains(index);
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
        private ListViewItem? FindLVItem(CShellItem item)
        {
            Debug.WriteLine("ExpList: FindLVItem Begin");
            try
            {
                if (_itemIndex.TryGetValue(item.FullPath, out var lvi))
                    return lvi;
                return null;
            }
            finally
            {
                Debug.WriteLine("ExpList: FindLVItem End");
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
                if (_itemIndex.TryGetValue(fullPath, out var lvi))
                    return lvi.Index;
            }
            return -1;
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
                if (_ListView.ListViewItemSorter is LVColSorter sorter)
                {
                    sorter.SetSort(column, order);
                }
                else if (!VirtualMode)
                {
                    _ListView.Sort();
                }
            }
            finally
            {
                _inSort = false;
                Debug.WriteLine("VirtualListViewWrapper: Sort end");
            }
        }

        public void RedrawItem(int index)
        {
            if (VirtualMode)
            {
                _itemCache.Remove(index); //it is assumed that there must be new data to require a redraw
                _ListView.RedrawItems(index, index, false);
            }
            else
            {
                if (index >= 0 && index < _ListView.Items.Count)
                {
                    var lvi = _ListView.Items[index];
                    UpdateListviewItemCallback?.Invoke(lvi, lvi.Tag as CShellItem);
                }
            }
        }

        public void RefreshItem(CShellItem? item)
        {
            if (item is null) return;

            Debug.WriteLine("ExpList: RefreshItem Begin");
            try
            {
                int index = GetIndexFromFullPath(item.FullPath);
                if (index >= 0)
                {
                    item.ColumnDic.Clear();
                    
                    RedrawItem(index);
                }
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
                _ListView.Invalidate();
            }
            else
            {
                _ListView.BeginUpdate();
                foreach (ListViewItem lvi in _ListView.Items)
                {
                    UpdateListviewItemCallback?.Invoke(lvi, lvi.Tag as CShellItem);
                }
                _ListView.EndUpdate();
            }
        }

        public void InvalidateCache()
        {
            _itemCache.Clear();
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

                foreach (var item in _virtualItems)
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
        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            //Console.WriteLine("Retrieve virtual item: " + e.ItemIndex);

            if (ExpList._isShuttingDown) return; //windows tries to retrieve every item during shutdown for some reason

            e.Item = GetLviFromVirtual(e.ItemIndex);
        }

        public ListViewItem GetLviFromVirtual(int index)
        {
            if (index < 0 || index >= _virtualItems.Count) return null;

            var item = _virtualItems[index];

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
                    // Sync ImageIndex if it was updated in the background while item was cached
                    if (lvi.ImageIndex == -1)
                    {
                        if (item.ImageIndex > -1)
                            lvi.ImageIndex = item.ImageIndex;
                        else
                        {
                            //don't create the thumbnail yet because windows will ask for items that aren't even on the screen.
                        }
                    }

                    return lvi;
                }
                else
                {
                    Debug.WriteLine("VirtualListViewWrapper.GetLviFromVirtual failed to get item #" + index.ToString() + " from cache - " + item.Text);
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
                foreach (var col in _ListView.Columns)
                {
                    Debug.WriteLine("Failed to create listview item");

                    var si = new ListViewItem.ListViewSubItem();
                    si.Text = string.Empty;
                    si.Tag = null;
                    lvi.SubItems.Add(si); // Placeholder for subitems, UpdateItemCallback should fill these in
                }
            }
            item.NeedsRefresh = false;

            return lvi;
        }

        private void UpdateVirtualListSize()
        {
            _ListView.VirtualListSize = _virtualItems.Count;
        }

        private void RecreateIndexMapping()
        {
            _pathToIndex.Clear();
            for (int i = 0; i < _virtualItems.Count; i++)
            {
                _pathToIndex[_virtualItems[i].FullPath] = i;
            }
        }

        private (int index, ColumnHeader header) GetDisplayNameColumn()
        {
            for (int i = 0; i < _ListView.Columns.Count; i++)
            {
                var col = _ListView.Columns[i];
                if (col.Tag?.ToString().Trim() == ".DisplayName")
                {
                    return (i, col);
                }
            }
            if (_ListView.Columns.Count > 0)
            {
                return (0, _ListView.Columns[0]);
            }
            return (-1, null);
        }

        private CShellItemComparer GetSecondaryComparer(int primaryColumn)
        {
            if (primaryColumn >= 0 && primaryColumn < _ListView.Columns.Count)
            {
                var primCol = _ListView.Columns[primaryColumn];
                string primMapping = primCol.Tag?.ToString().Trim() ?? string.Empty;
                if (primMapping.StartsWith(".") && primMapping.Substring(1) == "DisplayName")
                {
                    return null;
                }
            }

            int secColIndex = -1;
            SortOrder secOrder = SortOrder.None;
            ColumnHeader secColHeader = null;

            if (_prevSortColumn >= 0 && _prevSortColumn < _ListView.Columns.Count && _prevSortOrder != SortOrder.None)
            {
                secColIndex = _prevSortColumn;
                secOrder = _prevSortOrder;
                secColHeader = _ListView.Columns[secColIndex];
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
            if (order == SortOrder.None || _virtualItems.Count == 0) return;

            if (column < 0 || column >= _ListView.Columns.Count) return;
            var colHeader = _ListView.Columns[column];
            var secondaryComparer = GetSecondaryComparer(column);
            var comparer = new CShellItemComparer(_expList, column, order, colHeader, secondaryComparer);

            // Copy to a List for sorting because HugeList (B-Tree) sort is impractical in-place
            var list = new List<CShellItem>((int)_virtualItems.Count);
            foreach (var item in _virtualItems)
            {
                list.Add(item);
            }

            list.Sort(comparer);

            _ListView.BeginUpdate();
            _virtualItems.Clear();
            _virtualItems.AddRange(list);

            RecreateIndexMapping();
            _itemCache.Clear();
            _ListView.EndUpdate();
            _ListView.Refresh();
        }

        /// <summary>
        /// Finds the insertion point for a new item in a sorted HugeList using the built-in BinarySearch method.
        /// Returns the index where the item should be inserted to maintain sorted order.
        /// </summary>
        /// <param name="item">The item to find an insertion point for</param>
        /// <returns>The index where the item should be inserted</returns>
        public int FindInsertionPoint(CShellItem item)
        {
            if (_sortOrder == SortOrder.None || _sortColumn < 0 || _sortColumn >= _ListView.Columns.Count)
                return Count;

            var colHeader = _ListView.Columns[_sortColumn];
            var secondaryComparer = GetSecondaryComparer(_sortColumn);
            var comparer = new CShellItemComparer(_expList, _sortColumn, _sortOrder, colHeader, secondaryComparer);

            if (VirtualMode)
            {
                long result = _virtualItems.BinarySearch(0, _virtualItems.Count, item, comparer);
                return (int)(result < 0 ? ~result : result);
            }
            else
            {
                // Binary search on ListView.Items
                int low = 0;
                int high = _ListView.Items.Count - 1;

                while (low <= high)
                {
                    int mid = low + ((high - low) / 2);
                    var midCsi = _ListView.Items[mid].Tag as CShellItem;
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
        /// Determines if the current display mode is a thumbnail-based view.
        /// </summary>
        /// <returns>True if in a thumbnail view mode.</returns>
        public bool IsThumbnailViewMode() => DisplayMode == ListViewDisplayMode.Thumbnail || DisplayMode == ListViewDisplayMode.LargeThumbnail || DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail;

        private const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        private const int LVM_GETITEMRECT = LVM_FIRST + 14;
        private const int LVM_HITTEST = LVM_FIRST + 18;
        private const int LVM_GETITEMSPACING = LVM_FIRST + 51; // returns packed x/y in LPARAM
        private const int LVM_GETTOPINDEX = LVM_FIRST + 39;
        private const int LVNI_VISIBLE = 0x0008;
        private const int LVIR_BOUNDS = 0; // for LVM_GETITEMRECT
        private const int LVM_GETCOUNTPERPAGE = 0x1000 + 40;

        public int LastTopIndex = -1;

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
                if (_ListView == null || !_ListView.IsHandleCreated) return -1;

                int total = _ListView.VirtualMode ? _ListView.VirtualListSize : _ListView.Items.Count;
                if (total <= 0) return -1;

                if (LastTopIndex > -1) return LastTopIndex; // cache for repeated calls.  The OS will sometimes make tons of redundant calls

                int top = 0;
                if (!_ListView.VirtualMode && _ListView.TopItem != null)
                {
                    LastTopIndex = _ListView.TopItem.Index;
                    return _ListView.TopItem.Index;
                }

                // 2) Try visible enumeration (works in many non-virtual cases)
                int byVisibleEnum = FindTopLeftByVisibleEnumeration(total);
                if (byVisibleEnum >= 0) return byVisibleEnum;

                // 3) Virtual-safe fallback: scan viewport by hit-test
                int byHitTestScan = FindTopLeftByHitTestScan(total);
                if (byHitTestScan >= 0) return byHitTestScan;

                // 4) Last fallback
                LastTopIndex = (top >= 0 && top < total) ? top : -1;
                return LastTopIndex;
            }
            finally
            {
                Debug.WriteLine("ExpList: GetTopIndex End");
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
                    i = (int)SendMessage(_ListView.Handle, LVM_GETNEXTITEM, (IntPtr)i, (IntPtr)LVNI_VISIBLE);
                    if (i < 0) break;
                    if (i >= total) continue;

                    RECT rc = new RECT { left = LVIR_BOUNDS };
                    if (SendMessage(_ListView.Handle, LVM_GETITEMRECT, (IntPtr)i, ref rc) == IntPtr.Zero)
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

        private int FindTopLeftByHitTestScan(int total)
        {
            Debug.WriteLine("ExpList: FindTopLeftByHitTestScan Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                var client = _ListView.ClientRectangle;
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
                        if (idx < 0 || idx >= total) continue;

                        RECT rc = new RECT { left = LVIR_BOUNDS };
                        if (SendMessage(_ListView.Handle, LVM_GETITEMRECT, (IntPtr)idx, ref rc) != IntPtr.Zero)
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

                int result = (int)SendMessage(_ListView.Handle, LVM_HITTEST, IntPtr.Zero, ref ht);
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
                if (_ListView == null || !_ListView.IsHandleCreated)
                    return 0;

                return _ListView.View == View.LargeIcon
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
            if (_ListView == null || !_ListView.IsHandleCreated || _ListView.View == View.LargeIcon)
                return 0;

            int total = _ListView.VirtualMode ? _ListView.VirtualListSize : _ListView.Items.Count;
            if (total <= 0) return 0;

            switch (_ListView.View)
            {
                case View.Details:
                case View.List:
                    // LVM_GETCOUNTPERPAGE is geometry-based and works in virtual mode
                    int perPage = (int)SendMessage(_ListView.Handle, LVM_GETCOUNTPERPAGE, IntPtr.Zero, IntPtr.Zero);
                    return Math.Min(total, Math.Max(0, perPage));

                case View.SmallIcon:
                case View.Tile:
                    // LVM_GETCOUNTPERPAGE returns total item count for these views, so use spacing math instead
                    return EstimateVisibleBySpacing(_ListView, total, largeIcon: false);

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
                if (_ListView == null || !_ListView.IsHandleCreated || _ListView.View != View.LargeIcon)
                    return 0;

                int total = _ListView.VirtualMode ? _ListView.VirtualListSize : _ListView.Items.Count;
                if (total <= 0) return 0;

                // FALSE => large icon spacing
                int packed = (int)SendMessage(_ListView.Handle, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
                int cellW = packed & 0xFFFF;
                int cellH = (packed >> 16) & 0xFFFF;

                // Fallback if spacing couldn't be read
                if (cellW <= 0 || cellH <= 0)
                {
                    var img = _ListView.LargeImageList?.ImageSize ?? new System.Drawing.Size(32, 32);
                    cellW = Math.Max(1, img.Width + 32);                   // rough label/padding allowance
                    cellH = Math.Max(1, img.Height + _ListView.Font.Height * 2 + 16);
                }

                int vw = Math.Max(1, _ListView.ClientSize.Width);
                int vh = Math.Max(1, _ListView.ClientSize.Height);

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
                ListViewDisplayMode.Details => _ListView.Font.Height,
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
