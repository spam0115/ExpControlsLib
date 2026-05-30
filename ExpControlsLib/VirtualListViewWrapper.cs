using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using TreeLib;

namespace ExpControlsLib
{
    /// <summary>
    /// Encapsulates the messy behavior of the ListView control when switching between
    /// Virtual and Regular modes. Provides a unified interface for data manipulation.
    /// </summary>
    internal class VirtualListViewWrapper
    {
        private readonly ListView _listView;
        private readonly HugeList<CShellItem> _virtualItems = new();
        private readonly Dictionary<int, ListViewItem> _itemCache = new();
        private readonly Dictionary<string, int> _pathToIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _itemIndex = new(StringComparer.OrdinalIgnoreCase);

        private bool _useVirtualMode;
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
            _useVirtualMode = _listView.VirtualMode;

            _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        }

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
                    _listView.Items.Clear();
                    _itemIndex.Clear();
                }
                else
                {
                    _virtualItems.Clear();
                    _itemCache.Clear();
                    _pathToIndex.Clear();
                }
            }
        }

        public int Count => _useVirtualMode ? _virtualItems.Count : _listView.Items.Count;

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
                if (_useVirtualMode)
                    return _sortOrder;
                else
                    return _listView.Sorting;
            }
            set
            {
                if (_useVirtualMode)
                    _sortOrder = value;
                else
                    _listView.Sorting = value;
            }
        }


        public void Clear()
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

        public void AddRange(IEnumerable<CShellItem> items)
        {
            if (_useVirtualMode)
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
            if (_useVirtualMode)
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

            if (_useVirtualMode)
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

            if (_useVirtualMode)
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

        public ListViewItem GetListViewItem(int index)
        {
            if (_useVirtualMode)
            {
                return GetItemInternal(index);
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
            if (_useVirtualMode)
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

        public int GetIndexFromFullPath(string fullPath)
        {
            if (_useVirtualMode)
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

            if (_useVirtualMode)
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
            if (_useVirtualMode)
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
            if (_useVirtualMode)
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

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (ExpList._isShuttingDown) return;

            e.Item = GetItemInternal(e.ItemIndex);
        }

        private ListViewItem GetItemInternal(int index)
        {
            if (index < 0 || index >= _virtualItems.Count) return null;

            if (_itemCache.TryGetValue(index, out var lvi))
            {
                var csi = _virtualItems[index];
                UpdateItemCallback?.Invoke(lvi, csi);
                return lvi;
            }

            var item = _virtualItems[index];
            lvi = CreateItemCallback?.Invoke(item) ?? new ListViewItem(item.DisplayName) { Tag = item };
            _itemCache[index] = lvi;
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

        public int FindInsertionPoint(CShellItem item)
        {
            var comparer = GetComparerCallback?.Invoke(_sortColumn, _sortOrder);

            if (comparer == null || _sortOrder == SortOrder.None)
                return Count;

            if (_useVirtualMode)
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
    }
}
