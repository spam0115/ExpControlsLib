using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        private const int BatchThreshold = 10;
        private readonly ExpList _expList;
        /// <summary>
        /// Cache of ListViewItems for virtual mode, keyed by index.  
        /// Note: it is important to update a given ListViewItems if the associated CShellItem changes, 
        /// otherwise the ListView will display stale data.
        /// </summary>
        private readonly LruDictionary<int, ListViewItem> _indexedLviCache = new(1000); 
        private readonly Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Provides a mapping from file name to ListViewItems in the listview.  
        /// Note: This can only be used in non-virtual mode beucase in virtual mode ListViewItems do not persist.
        /// </summary>
        private readonly Dictionary<string, ListViewItem> _pathToLvi = new(StringComparer.OrdinalIgnoreCase);
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
        /// Tracks the checkbox state the caller <em>wants</em>, independent of whether
        /// the current <see cref="DisplayMode"/> supports checkboxes. When the mode
        /// switches away from a checkbox-compatible view the checkbox is suppressed on
        /// the underlying <see cref="ListView"/>, but this flag remembers that it should
        /// be restored once a compatible mode is entered again.
        /// </summary>
        private bool _desiredCheckBoxes;

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
        /// Returns <c>true</c> if the given display mode is compatible with
        /// <see cref="ListView.CheckBoxes"/>. WinForms throws a
        /// <see cref="NotSupportedException"/> if <c>CheckBoxes</c> is <c>true</c>
        /// while the view is <see cref="View.Tile"/>, <see cref="View.LargeIcon"/>,
        /// or any of the custom thumbnail modes (which map to <c>LargeIcon</c>
        /// internally).
        /// </summary>
        private static bool SupportsCheckBoxes(ListViewDisplayMode mode) =>
            mode == ListViewDisplayMode.Details ||
            mode == ListViewDisplayMode.SmallIcon ||
            mode == ListViewDisplayMode.List;

        /// <summary>
        /// Gets or sets whether checkboxes should be shown. The desired state is always
        /// stored in <c>_desiredCheckBoxes</c>; the underlying
        /// <see cref="ListView.CheckBoxes"/> is only set to <c>true</c> when the
        /// current <see cref="DisplayMode"/> is compatible (Details, SmallIcon, List).
        /// Switching to an incompatible mode (Tile, LargeIcon, Thumbnail, …)
        /// automatically suppresses the glyph and switching back restores it.
        /// </summary>
        internal bool CheckBoxes
        {
            get => _desiredCheckBoxes;
            set
            {
                _desiredCheckBoxes = value;
                bool compatible = SupportsCheckBoxes(DisplayMode);
                ApplyCheckBoxesToListView(compatible && value);
            }
        }

        /// <summary>
        /// Applies <paramref name="active"/> to <see cref="ListView.CheckBoxes"/> using
        /// the VirtualMode round-trip guard so the handle recreation side-effects are
        /// handled correctly. Does nothing if the value is already correct.
        /// </summary>
        private void ApplyCheckBoxesToListView(bool active)
        {
            if (_listView.CheckBoxes == active) return;

            // CheckBoxes toggle forces a handle recreation — same guard pattern as the
            // DisplayMode setter. See VirtualMode <remarks> for the full explanation.
            //bool wasVirtual = _listView.VirtualMode;
            //if (wasVirtual)
            //{
            //    _listView.VirtualMode = false;
            //    _listView.VirtualListSize = 0;
            //}

            // Cached virtual ListViewItems can be associated with the old native handle.
            // Drop them before and after the checkbox toggle so the first repaint after a
            // view switch materializes fresh items with the correct state image.
            if (VirtualMode)
                _indexedLviCache.Clear();

            _listView.CheckBoxes = active;

            if (VirtualMode)
            {
                _indexedLviCache.Clear();
                _listView.Invalidate();
            }

            //if (wasVirtual)
            //{
            //    _listView.VirtualMode = true;
            //    _listView.VirtualListSize = ActiveViewCount;
            //    _itemCache.Clear();
            //}
        }

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

            ApplyFilteredViewToListView();
        }

        /// <summary>
        /// Clears the active filter, showing all items from the master list.
        /// </summary>
        public void ClearFilter()
        {
            if (_filteredView == null) return;
            _filteredView = null;
            ApplyFilteredViewToListView();
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
        private void ApplyFilteredViewToListView()
        {
            LastTopIndex = -1;
            _indexedLviCache.Clear(); //the indxes are about to change so we clear this instead of doing a complicated remapping of the cache
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


        /// <summary>
        /// Gets or sets whether the underlying <see cref="ListView"/> is in virtual mode
        /// (owner-data, populated via <see cref="ListView.RetrieveVirtualItem"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>VirtualMode</c> must be set before the ListView handle is created. WinForms
        /// does not support changing the owner-data mode after the control has been
        /// displayed, because doing so invalidates the ListView's item storage and
        /// cached state.
        /// </para>
        /// </remarks>
        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _listView.VirtualMode;
            set
            {
                if (_listView.VirtualMode == value) return;

                if (_listView.IsHandleCreated)
                {
                    throw new InvalidOperationException(
                        "VirtualMode can only be set before the ListView is displayed.");
                }

                _listView.VirtualMode = value;

                if (value)
                {
                    _listView.RetrieveVirtualItem -= OnRetrieveVirtualItem; //just in case
                    _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
                    _listView.Items.Clear();
                    _pathToLvi.Clear();
                }
                else
                {
                    _listView.RetrieveVirtualItem -= OnRetrieveVirtualItem;
                    Items.Clear();
                    _filteredView = null;
                    _indexedLviCache.Clear();
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

                // Checkbox suppression around the view change.
                //
                // WinForms throws NotSupportedException if CheckBoxes is true while View
                // is Tile or LargeIcon (or any of our custom thumbnail modes which map
                // to LargeIcon). We therefore must:
                //   * If entering an incompatible mode: drop CheckBoxes BEFORE changing View.
                //   * If entering a compatible mode:    change View BEFORE restoring CheckBoxes.
                // ApplyCheckBoxesToListView handles the VirtualMode round-trip guard
                // internally — see VirtualMode <remarks> for the full handle-recreation
                // explanation.
                bool newModeSupportsCheckBoxes = SupportsCheckBoxes(value);

                if (_desiredCheckBoxes && !newModeSupportsCheckBoxes)
                {
                    // Must suppress checkboxes first so the view change doesn't throw.
                    ApplyCheckBoxesToListView(false);
                }

                if (value <= ListViewDisplayMode.Tile) // View values native to the ListView control 
                {
                    _listView.View = (View)value;
                }
                else
                {
                    _listView.View = View.LargeIcon; //XP era kludge for thumbnail mode
                }

                field = value;

                if (_desiredCheckBoxes && newModeSupportsCheckBoxes)
                {
                    // Safe to (re-)apply checkboxes now that the view supports them.
                    ApplyCheckBoxesToListView(true);
                }

                if (VirtualMode) InvalidateVirtualItemImagesIndexes();

            }
        } = ListViewDisplayMode.Unset;

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
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.Clear");
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
            _indexedLviCache.Clear();
            _pathToIndex.Clear();
            _pathToLvi.Clear();
        }

        /// <summary>
        /// Currently, we're only using this to initialize the whole collection.  If we ever want to use this 
        /// only add a batch of items to an existing collection, we'll need to add some logic to handle 
        /// merging the new items with the existing ones in sorted order and etc.
        /// </summary>
        /// <param name="items"></param>
        public void AddRange(IEnumerable<CShellItem> items)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.AddRange #" + items.Count());
            LastTopIndex = -1;
            if (VirtualMode)
            {
                Items.AddRange(items);
                ApplyFilteredViewToListView();
            }
            else
            {
                _listView.BeginUpdate();
                foreach (var item in items)
                {
                    var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                    _listView.Items.Add(lvi);
                    _pathToLvi[item.FullPath] = lvi;
                }
                _listView.EndUpdate();
            }
        }

        public void AddToEnd(CShellItem item)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.Add - " + item.Text);
            LastTopIndex = -1;
            if (VirtualMode)
            {
                Items.Add(item);
                ApplyFilteredViewToListView();
            }
            else
            {
                var lvi = CreateListviewItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
                _listView.Items.Add(lvi);
                _pathToLvi[item.FullPath] = lvi;
            }
        }

        /// <summary>
        /// Shifts cached ListViewItem objects after an item has been inserted into the list.
        /// This allows us to reuse existing ListViewItem objects for items that have merely shifted index.
        /// </summary>
        /// <param name="index">The index where the item was inserted.</param>
        private void ShiftCacheAfterInsertion(int index)
        {
            if (_indexedLviCache.Count == 0) return;

            // Shift all items from the insertion point onwards up by one index
            var keysToShift = _indexedLviCache.Keys.Where(k => k >= index).OrderByDescending(k => k).ToList();
            foreach (var k in keysToShift)
            {
                _indexedLviCache[k + 1] = _indexedLviCache[k];
                _indexedLviCache.Remove(k);
            }
        }

        public void InsertSorted(CShellItem item)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.InsertSorted - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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

                ApplyFilteredViewToListView();

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
                _pathToLvi[item.FullPath] = lvi;
                lvi.EnsureVisible();
            }
        }

        /// <summary>
        /// Shifts cached ListViewItem objects after an item has been removed from the list.
        /// This allows us to reuse existing ListViewItem objects for items that have merely shifted index.
        /// </summary>
        /// <param name="index">The index where the item was removed.</param>
        /// <param name="path">The path of the item being removed.</param>
        private void RemoveItemFromCachesAndShiftIndexes(int index, string path)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.ShiftLviCacheAfterRemoval: " + index.ToString());

            if (_indexedLviCache.Count == 0) return;

            // Remove the deleted item from cache
            _indexedLviCache.Remove(index);

            // Shift all subsequent items down by one index
            var keysToShift = _indexedLviCache.Keys.Where(k => k > index).OrderBy(k => k).ToList();
            foreach (var k in keysToShift)
            {
                _indexedLviCache[k - 1] = _indexedLviCache[k];
                _indexedLviCache.Remove(k);
            }

            _pathToIndex.Remove(path);
            foreach (var kvp in _pathToIndex)
            {
                if (kvp.Value > index)
                {
                    _pathToIndex[kvp.Key] = kvp.Value - 1;
                }
            }

        }

        /// <summary>
        /// Removes an item at the given location.
        /// </summary>
        /// <param name="index"></param>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count) return;

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.RemoveAt: " + index.ToString());

            if (VirtualMode)
            {
                lock (Items)
                {
                    CShellItem item = GetItemFromActiveView(index);

                    Items.RemoveAt(index);
                    RemoveItemFromCachesAndShiftIndexes(index, item.FullPath);
                }
            }
            else
            {
                var lvi = _listView.Items[index];
                if (lvi.Tag is CShellItem csi)
                    _pathToLvi.Remove(csi.FullPath);
                _listView.Items.RemoveAt(index);
            }
        }

        /// <summary>
        /// Removes an item at the given location and redraws the affected areas.
        /// This shouldn't be used inside big loops because it is too inefficient.
        /// </summary>
        /// <param name="index"></param>
        public void RemoveAndRedrawAt(int index)
        {
            if (index < 0 || index >= Count) return;

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.RemoveAndRedrawAt: " + index.ToString());

            RemoveAt(index);

            if (VirtualMode)
            {
                //ApplyFilteredViewToListView(); isn't this only needed for additions, not removals?

                RedrawStartingAt(index);
            }
        }

        private void RedrawStartingAt(int index)
        {
            // redraw new sections if they are in the viewport
            int top = GetTopIndex();
            int visibleCount = GetApproxVisibleCount();
            int lastVisible = top + visibleCount;

            // Only redraw if the removal affects currently visible items or new items shift into view
            int startRedraw = Math.Max(index, top);
            int endRedraw = Math.Min(lastVisible, ActiveViewCount - 1);

            if (startRedraw <= endRedraw)
            {
                _listView.RedrawItems(index, endRedraw, false);
            }
        }

        public void RemoveItems(IEnumerable<CShellItem> items)
        {
            if (items == null) return;
            var toRemove = new HashSet<CShellItem>(items);
            if (toRemove.Count == 0) return;

            // Process small number of removals individually to avoid full redraw
            if (toRemove.Count <= BatchThreshold)
            {
                try { 
                    //_listView.SuspendLayout();
                    var indices = new List<int>();
                    foreach (var item in items)
                    {
                        int index = GetIndex(item);
                        if (index >= 0) indices.Add(index);
                    }

                    // Remove in reverse order to avoid index shifting problems
                    indices.Sort((a, b) => b.CompareTo(a));
                    var first = indices.Last();
                    foreach (int index in indices)
                    {
                        RemoveAt(index);
                    }

                    RedrawStartingAt(first);
                }
                finally
                {
                    //_listView.ResumeLayout();
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

                    // For large batches, it's safer and often faster to just clear the cache rather than trying to remap it
                    _indexedLviCache.Clear();
                }

                ApplyFilteredViewToListView();
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
                            _pathToLvi.Remove(csi.FullPath);
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

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.GetItem failed to get item at index " + index);
            return null;
        }

        public ListViewItem GetListViewItem(int index)
        {
            if (VirtualMode)
            {
                return GetLviForVirtualItem(index);
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
            //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindLVItem Begin");
            if (VirtualMode)
            {
                return null;
            }

            try
            {
                if (_pathToLvi.TryGetValue(item.FullPath, out var lvi))
                    return lvi;
                return null;
            }
            finally
            {
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindLVItem End");
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
                if (_pathToLvi.TryGetValue(fullPath, out var lvi))
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
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper: Sort begin");
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
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper: Sort end");
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

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: RefreshItem Begin");

            try
            {
                ListViewItem lvi = null;
                int index = GetIndexFromFullPath(csi.FullPath);
                if (VirtualMode)
                {
                    if (_indexedLviCache.ContainsKey(index)) {
                        lvi = _indexedLviCache[index];
                        UpdateListviewItemCallback?.Invoke(lvi, csi);
                    }
                    else
                    {
                        lvi = CreateLviFromCsi(csi);
                        _indexedLviCache[index] = lvi;
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
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: RefreshItem End");
            }
        }

        public void RefreshItemByFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: RefreshItemByFullPath Begin");
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
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: RefreshItemByFullPath End");
            }
        }

        public void RedrawAll()
        {
            if (VirtualMode)
            {
                _indexedLviCache.Clear();
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
            _indexedLviCache.Clear();
        }

        public int GetRowHeight()
        {
            int itemCount = _listView.VirtualMode
                ? _listView.VirtualListSize
                : _listView.Items.Count;

            if (itemCount == 0)
                return 0;

            int topIndex = _listView.TopItem?.Index ?? 0;
            topIndex = Math.Clamp(topIndex, 0, itemCount - 1);

            return _listView
                .GetItemRect(topIndex, ItemBoundsPortion.Entire)
                .Height;
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
            //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: InvalidateVirtualItemIndexes Begin");
            try
            {
                if (!VirtualMode) return;

                foreach (var item in ActiveView)
                {
                    if (item != null) item.ImageIndex = -1;
                }

                foreach (var lvi in _indexedLviCache.Values)
                {
                    if (lvi != null) lvi.ImageIndex = -1;
                }
            }
            finally
            {
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: InvalidateVirtualItemIndexes End");
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
                var lvi = GetLviForVirtualItem(e.ItemIndex);
                if (lvi is null)
                {
                    e.Item = new ListViewItem(); //send back a dummy
                }
                else
                    e.Item = lvi;
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error in OnRetrieveVirtualItem: " + ex.Message);
                e.Item = new ListViewItem(); //send back a dummy to avoid crashing the ListView
            }
            finally
            {
                if (isThumbnailMode) _expList.ExitImageListMutation();
            }
        }

        public ListViewItem GetLviForVirtualItem(int index)
        {
            if (index < 0 || index >= ActiveViewCount) return null;

            var item = GetItemFromActiveView(index);

            if (item.NeedsRefresh) //item has been updated in the background and needs to be recreated as a new ListViewItem to reflect changes
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.GetLviFromVirtual needs refresh - " + item.Text);
                var lvi = CreateLviFromCsi(item);
                _indexedLviCache[index] = lvi;
                return lvi;
            }
            else
            {
                if (_indexedLviCache.TryGetValue(index, out var lvi))
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

                    // NOTE: do NOT reset lvi.Selected or lvi.Focused here.
                    // In virtual mode the Win32 ListView owns selection/focus state by
                    // index; writing those properties on a ListViewItem sends
                    // LVM_SETITEMSTATE back to the control, which triggers a repaint,
                    // which fires RetrieveVirtualItem again — an infinite loop leading
                    // to a StackOverflowException. The ListView ignores the LVI flags
                    // for selection/focus during virtual-mode rendering anyway.

                    return lvi;
                }
                else
                {
                    //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VirtualListViewWrapper.GetLviFromVirtual failed to get item #" + index.ToString() + " from cache - " + item.Text);
                    lvi = CreateLviFromCsi(item);
                    _indexedLviCache[index] = lvi;
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
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Failed to create listview item");

                    var si = new ListViewItem.ListViewSubItem();
                    si.Text = string.Empty;
                    si.Tag = null;
                    lvi.SubItems.Add(si); // Placeholder for subitems, UpdateItemCallback should fill these in
                }
            }
            item.NeedsRefresh = false;

            SyncCheckboxState(lvi, item);

            return lvi;
        }

        /// <summary>
        /// Synchronizes the model's checked state to a freshly materialized virtual
        /// ListViewItem. ListViewItem.StateImageIndex is zero-based: 0 is unchecked and
        /// 1 is checked.
        /// </summary>
        private void SyncCheckboxState(ListViewItem lvi, CShellItem item)
        {
            SuppressCheckEvents = true;
            try
            {
                lvi.Checked = item.Checked;
                if (_listView.CheckBoxes)
                    lvi.StateImageIndex = item.Checked ? 1 : 0;
            }
            finally { SuppressCheckEvents = false; }
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
                if (_indexedLviCache.TryGetValue(viewIndex, out var lvi))
                {
                    SuppressCheckEvents = true;
                    try
                    {
                        lvi.Checked = value;
                        if (_listView.CheckBoxes)
                            lvi.StateImageIndex = value ? 1 : 0;
                    }
                    finally { SuppressCheckEvents = false; }
                    _listView.RedrawItems(viewIndex, viewIndex, false);
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
            _indexedLviCache.Clear();
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
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetTopIndex Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated) return -1;

                int total = VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                if (total <= 0) return -1;

                if (LastTopIndex > -1) return LastTopIndex; // cache for repeated calls.  The OS will sometimes make tons of redundant calls in a brief amount of time.

                var view = _listView.View;

                // 1) Fast O(1) path for Details/List views.
                if (view == View.Details || view == View.List)
                {
                    if (!VirtualMode && _listView.TopItem != null)
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
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tFound TopLeft by FindTopLeftByOrigin.");
                        return byOrigin; 
                    }

                    int bySingleHit = FindTopLeftBySingleHitTest(total);
                    if (bySingleHit >= 0) { LastTopIndex = bySingleHit;
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tFound TopLeft by FindTopLeftBySingleHitTest.");
                        return bySingleHit; }
                }

                // 3) Visible enumeration (works in many non-virtual cases)
                int byVisibleEnum = FindTopLeftByVisibleEnumeration(total);
                if (byVisibleEnum >= 0) { LastTopIndex = byVisibleEnum;
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tFound TopLeft by FindTopLeftByVisibleEnumeration.");
                    return byVisibleEnum; }

                // 4) Last-resort fallback: scan viewport by hit-test
                int byHitTestScan = FindTopLeftByHitTestScan(total);
                if (byHitTestScan >= 0) { LastTopIndex = byHitTestScan;
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tFound TopLeft by FindTopLeftByHitTestScan.");
                    return byHitTestScan; }

                // 5) Absolute fallback
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tFailed to find topleft.");

                LastTopIndex = 0;
                return LastTopIndex;
            }
            finally
            {
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetTopIndex End");
            }
        }

        /// <summary>
        /// Uses LVM_GETTOPINDEX to return the topmost visible item in Details/List views.
        /// This is a single message call, O(1), and virtual-mode safe.
        /// </summary>
        private int FindTopLeftByTopIndex(int total)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByTopIndex Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            try
            {
                int idx = (int)SendMessage(_listView.Handle, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
                if (idx >= 0 && idx < total) return idx;
                return -1;
            }
            finally
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByTopIndex End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Computes the top-left visible item for icon-grid views (SmallIcon, LargeIcon, Tile)
        /// from the viewport scroll origin (LVM_GETORIGIN) and the per-item cell spacing
        /// (LVM_GETITEMSPACING). Two message calls, O(1), virtual-mode safe.
        /// </summary>
        private int FindTopLeftByOrigin(int total)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByOrigin Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByOrigin End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Single LVM_HITTEST probe near the top-left of the client area. One message call,
        /// O(1). Used as a fast fallback when the grid-math path is unavailable for icon views.
        /// </summary>
        private int FindTopLeftBySingleHitTest(int total)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftBySingleHitTest Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftBySingleHitTest End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        private int FindTopLeftByVisibleEnumeration(int total)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByVisibleEnumeration Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByVisibleEnumeration End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        /// <summary>
        /// Tries to find the first item visible in the ListView's current viewport.  Works in both list and icon view modes.
        /// </summary>
        /// <param name="listCount">the number of items in the listview</param>
        /// <returns>index number of what is believed to be the first top-left most item.</returns>
        private int FindTopLeftByHitTestScan(int listCount)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByHitTestScan Begin - " + DateTime.Now.ToString("HH:mm:ss.fff"));
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
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: FindTopLeftByHitTestScan End - " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        private int HitTestIndex(int x, int y)
        {
            //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: HitTestIndex Begin");
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
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: HitTestIndex End");
            }
        }

        public int GetApproxVisibleCount()
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetApproxVisibleCount Begin");
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
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetApproxVisibleCount End");
            }
        }

        private int GetAnyVisibleCount()
        {
            if (_listView == null || !_listView.IsHandleCreated || _listView.View == View.LargeIcon)
                return 0;

            int total = VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
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
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetApproxVisibleCountLargeIcon Begin");
            try
            {
                if (_listView == null || !_listView.IsHandleCreated || _listView.View != View.LargeIcon)
                    return 0;

                int total = VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
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
                //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: GetApproxVisibleCountLargeIcon End");
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

        internal int GetItemsViewportTop()
        {
            // Icon, tile, and list views generally begin at client Y = 0.
            if (_listView.View != View.Details ||
                _listView.HeaderStyle == ColumnHeaderStyle.None)
            {
                return _listView.ClientRectangle.Top;
            }

            IntPtr headerHandle = SendMessage(
                _listView.Handle,
                LVM_GETHEADER,
                IntPtr.Zero,
                IntPtr.Zero);

            if (headerHandle == IntPtr.Zero)
                return _listView.ClientRectangle.Top;

            if (!GetWindowRect(headerHandle, out RECT headerRect))
                return _listView.ClientRectangle.Top;

            // GetWindowRect returns screen coordinates. Convert the bottom of
            // the header to coordinates relative to the ListView.
            Point headerBottom = _listView.PointToClient(
                new Point(headerRect.left, headerRect.bottom));

            return headerBottom.Y;
        }

        public void MoveItemToTop(int index)
        {
            int count = _listView.VirtualMode
                ? _listView.VirtualListSize
                : _listView.Items.Count;

            if ((uint)index >= (uint)count)
                return;

            // First make the item available in the viewport so GetItemRect
            // returns useful coordinates.
            _listView.EnsureVisible(index);

            Rectangle rect = _listView.GetItemRect(
                index,
                ItemBoundsPortion.Entire);

            int targetY = GetItemsViewportTop();
            int dy = rect.Top - targetY;

            SendMessage(
                _listView.Handle,
                LVM_SCROLL,
                IntPtr.Zero,
                new IntPtr(dy));
        }

        internal void ClearSelected()
        {
            _listView.SelectedIndices.Clear();
            if (!VirtualMode)
                _listView.SelectedItems.Clear();
        }

    }
}
