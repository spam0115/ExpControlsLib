using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TreeLib;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static System.Runtime.InteropServices.JavaScript.JSType;
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
        private readonly ListView _listView;
        private readonly HugeList<CShellItem> _virtualItems = new();
        private readonly Dictionary<int, ListViewItem> _itemCache = new();
        private readonly Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _itemIndex = new(StringComparer.OrdinalIgnoreCase);

        private SortOrder _sortOrder = SortOrder.None;
        private int _sortColumn = 0;

        /// <summary>
        /// Callback to create a new ListViewItem for a given CShellItem.
        /// </summary>
        public Func<CShellItem, ListViewItem> CreateItemCallback { get; set; }

        /// <summary>
        /// Callback to update an existing ListViewItem with data from a CShellItem.
        /// </summary>
        public Action<ListViewItem, CShellItem> UpdateItemCallback { get; set; }

        /// <summary>
        /// Callback to get a comparer for sorting.
        /// </summary>
        public Func<int, SortOrder, IComparer<CShellItem>> GetComparerCallback { get; set; }

        public VirtualListViewWrapper(ListView listView)
        {
            _listView = listView ?? throw new ArgumentNullException(nameof(listView));

            _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        }

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
                    _itemIndex.Clear();
                }
                else
                {
                    _listView.RetrieveVirtualItem -= OnRetrieveVirtualItem;
                    _virtualItems.Clear();
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }
            }
        }

        public int Count => VirtualMode ? _virtualItems.Count : _listView.Items.Count;

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

        public SortOrder Sorting
        {
            get
            {
                if (VirtualMode)
                    return _sortOrder;
                else
                    return _listView.Sorting;
            }
            set
            {
                if (VirtualMode)
                    _sortOrder = value;
                else
                    _listView.Sorting = value;
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
                    _listView.View = (View)value;
                }
                else
                {
                    _listView.View = View.LargeIcon; //XP era kludge for thumbnail mode
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
            _listView.SelectedIndices.Clear();
            if (VirtualMode)
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

        public void AddRange(IEnumerable<CShellItem> items)
        {
            if (VirtualMode)
            {
                _virtualItems.AddRange(items);
                UpdateVirtualListSize();
                RecreateIndexMapping();
            }
            else
            {
                _listView.BeginUpdate();
                foreach (var item in items)
                {
                    var lvi = CreateItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                    _listView.Items.Add(lvi);
                    _itemIndex[item.FullPath] = lvi;
                }
                _listView.EndUpdate();
            }
        }

        public void Add(CShellItem item)
        {
            if (VirtualMode)
            {
                _virtualItems.Add(item);
                UpdateVirtualListSize();
                _pathToIndex[item.FullPath] = _virtualItems.Count - 1;
            }
            else
            {
                var lvi = CreateItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _listView.Items.Add(lvi);
                _itemIndex[item.FullPath] = lvi;
            }
        }

        public void InsertSorted(CShellItem item)
        {
            int index = FindInsertionPoint(item);

            if (VirtualMode)
            {
                _virtualItems.Insert(index, item);
                UpdateVirtualListSize();
                RecreateIndexMapping();
            }
            else
            {
                var lvi = CreateItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _listView.Items.Insert(index, lvi);
                _itemIndex[item.FullPath] = lvi;
                lvi.EnsureVisible();
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count) return;

            if (VirtualMode)
            {
                _virtualItems.RemoveAt(index);
                UpdateVirtualListSize();
                RecreateIndexMapping();
                _itemCache.Clear();
                _listView.Invalidate();
            }
            else
            {
                var lvi = _listView.Items[index];
                if (lvi.Tag is CShellItem csi)
                    _itemIndex.Remove(csi.FullPath);
                _listView.Items.RemoveAt(index);
            }
        }

        public CShellItem GetItem(int index)
        {
            if (VirtualMode)
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

        public void Sort(int column, SortOrder order)
        {
            _sortColumn = column;
            _sortOrder = order;

            if (VirtualMode)
            {
                SortVirtualItems(column, order);
            }
            else
            {
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                {
                    sorter.SetSort(column, order);
                }
            }
        }

        public void RedrawItem(int index)
        {
            if (VirtualMode)
            {
                _itemCache.Remove(index);
                _listView.RedrawItems(index, index, false);
            }
            else
            {
                if (index >= 0 && index < _listView.Items.Count)
                {
                    var lvi = _listView.Items[index];
                    UpdateItemCallback?.Invoke(lvi, lvi.Tag as CShellItem);
                }
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
                    UpdateItemCallback?.Invoke(lvi, lvi.Tag as CShellItem);
                }
                _listView.EndUpdate();
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
            //System.Diagnostics.Debug.WriteLine("ExpList: InvalidateVirtualItemIndexes Begin");
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
                //System.Diagnostics.Debug.WriteLine("ExpList: InvalidateVirtualItemIndexes End");
            }
        }

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (ExpList._isShuttingDown) return; //windows tries to retrieve every item during shutdown for some reason

            e.Item = GetLviFromVirtual(e.ItemIndex);
        }

        public ListViewItem GetLviFromVirtual(int index)
        {
            if (index < 0 || index >= _virtualItems.Count) return null;

            var item = _virtualItems[index];

            if (item.Updated)
            {
                var lvi = CreateLviFromCsi(item);
                _itemCache[index] = lvi;
                return lvi;
            }
            else
            {
                if (_itemCache.TryGetValue(index, out var lvi))
                {
                    // Sync ImageIndex if it was updated in the background while item was cached
                    if (lvi.ImageIndex == -1) //could be a sync problem to not run this each time because this data will not be populate for when the listview transitions from details to thumbnail modes
                    {
                        lvi.ImageIndex = item.ImageIndex;
                    }

                    return lvi;
                }
                else
                {
                    lvi = CreateLviFromCsi(item);
                    _itemCache[index] = lvi;
                    return lvi;
                }
            }
        }

        private ListViewItem CreateLviFromCsi(CShellItem item)
        {
            var lvi = CreateItemCallback?.Invoke(item);
            if (lvi != null)
            {   //this shouldn't ever happen, but just in case the callback fails, create a basic ListViewItem to avoid crashing the ListView
                lvi = new ListViewItem(item.DisplayName) { Tag = item };
                foreach (var col in _listView.Columns)
                {
                    Debug.WriteLine("Failed to create listview item");

                    var si = new ListViewItem.ListViewSubItem();
                    si.Text = "error";
                    si.Tag = null;
                    lvi.SubItems.Add(si); // Placeholder for subitems, UpdateItemCallback should fill these in
                }
            }

            return lvi;
        }

        private void UpdateVirtualListSize()
        {
            _listView.VirtualListSize = _virtualItems.Count;
        }

        private void RecreateIndexMapping()
        {
            _pathToIndex.Clear();
            for (int i = 0; i < _virtualItems.Count; i++)
            {
                _pathToIndex[_virtualItems[i].FullPath] = i;
            }
        }

        private void SortVirtualItems(int column, SortOrder order)
        {
            if (order == SortOrder.None || _virtualItems.Count == 0) return;

            var comparer = GetComparerCallback?.Invoke(column, order);
            if (comparer == null) return;

            // Copy to a List for sorting because HugeList (B-Tree) sort is impractical in-place
            var list = new List<CShellItem>((int)_virtualItems.Count);
            foreach (var item in _virtualItems)
            {
                list.Add(item);
            }

            list.Sort(comparer);

            _virtualItems.Clear();
            _virtualItems.AddRange(list);

            RecreateIndexMapping();
            _itemCache.Clear();
            _listView.Refresh();
        }

        /// <summary>
        /// Finds the insertion point for a new item in a sorted HugeList using the built-in BinarySearch method.
        /// Returns the index where the item should be inserted to maintain sorted order.
        /// </summary>
        /// <param name="item">The item to find an insertion point for</param>
        /// <returns>The index where the item should be inserted</returns>
        public int FindInsertionPoint(CShellItem item)
        {
            var comparer = GetComparerCallback?.Invoke(_sortColumn, _sortOrder);

            if (comparer == null || _sortOrder == SortOrder.None)
                return Count;

            if (VirtualMode)
            {
                long result = _virtualItems.BinarySearch(0, _virtualItems.Count, item, comparer);
                return (int)(result < 0 ? ~result : result);
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
        //    System.Diagnostics.Debug.WriteLine("ExpList: SetAndLoadImageList Begin");
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
        //        System.Diagnostics.Debug.WriteLine("ExpList: SetAndLoadImageList End");
        //    }
        //}

        ///// <summary>
        ///// Gets the pixel size for a given thumbnail display mode
        ///// </summary>
        //private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        //{
        //    //System.Diagnostics.Debug.WriteLine("ExpList: GetThumbnailSizeForMode Begin");
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
        //        //System.Diagnostics.Debug.WriteLine("ExpList: GetThumbnailSizeForMode End");
        //    }
        //}

    }
}
