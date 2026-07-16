using System.Collections;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Windows.Forms.VisualStyles;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// Provides a Synchronized wrapper for a Strongly Typed List of CShItems. 
    /// </summary>
    /// <remarks></remarks>
    public class CShellItemCollection : IEnumerable<CShellItem>, ICollection
    {
        private CShellItem _parent; //needed to set parent values
        private List<CShellItem> _items; //todo: maybe change this to HugeList ?
        private Dictionary<string, CShellItem>? _dictionary = null;

        /// <summary>
        /// A collection of CShellItems releted to a given parent
        /// </summary>
        /// <param name="parent">The parent item for all items that will eventually be added to this collection.</param>
        public CShellItemCollection(CShellItem parent)
        {
            _parent = parent;
            _items = new List<CShellItem>();
        }

        public CShellItemCollection(CShellItem parent, List<CShellItem> items)
        {
            _parent = parent;
            _items = items;
        }

        public int Count => _items.Count;

        public List<CShellItem> Items => _items;


        public CShellItem Parent
        {
            get
            {
                return _parent;
            }
            set //need this to support move operations
            {
                _parent = value;
            }
        }

        /// <summary>
        /// Lazy loaded dictionary of the collection indexed by display name.
        /// </summary>
        public Dictionary<string, CShellItem> Dictionary
        {
            get
            {
                if (_dictionary is null)
                {
                    lock (_items)
                    {
                        if (_dictionary == null)
                        {
                            _dictionary = _items.DistinctBy(o => o.DisplayName).ToDictionary(o => o.DisplayName, o => o, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                return _dictionary;
            }
        }

        public object SyncRoot
        {
            get
            {
                return _items;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return true;
            }
        }


        public void Sort()
        {
            lock (_items)
                _items.Sort();
        }

        internal int Add(CShellItem value)
        {
            if (value.Parent is null)
            {
                value.SetParent(_parent);
            }
            lock (_items)
            {
                _items.Add(value);
                if (_dictionary is not null)
                    _dictionary[value.DisplayName] = value;
                return _items.Count - 1;
            }
        }

        internal void AddRange(IEnumerable<CShellItem> value)
        {
            lock (_items)
            {
                _items.AddRange(value);
                if (_dictionary is not null)
                {
                    foreach (var item in value)
                        _dictionary[item.DisplayName] = item;
                }
            }
        }

        public void Clear()
        {
            lock (_items)
            {
                _items.Clear();
                if (_dictionary is not null)
                    _dictionary.Clear();
            }
        }

        public void ClearCaches()
        {
            if (_dictionary is not null)
                _dictionary.Clear();
        }

        public bool Contains(CShellItem value)
        {
            return _items.Contains(value);
        }

        public bool Contains(string name)
        {
            return Dictionary.ContainsKey(name);
        }

        /// <summary>
        /// Note: this is slow O(n).
        /// </summary>
        /// <param name="pidl"></param>
        /// <returns></returns>
        public bool Contains(IntPtr pidl)
        {
            foreach (CShellItem itm in _items)
            {
                if (CPidl.IsBinaryEqual(itm.PIDL, pidl))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsEquivalentAbsolutePidl(IntPtr pidl)
        {
            var result = CShellItemHierachyManager.Find(_parent, pidl);

            return result != null;
        }

        public CShellItem Find(string name)
        {
            Dictionary.TryGetValue(name, out var item);
            return item;
        }

        /// <summary>
        /// Returns items in this collection filtered by a wildcard pattern.
        /// </summary>
        /// <param name="filter">A filter string (for example: *.Doc)</param>
        /// <returns>A List of CShellItems. May return an empty List if there are none.</returns>
        public List<CShellItem> Filter(string filter)
        {
            var filteredItems = new List<CShellItem>();
            var normalizedFilter = filter.ToLowerInvariant();

            lock (_items)
            {
                foreach (var item in _items)
                {
                    if (Utils.WildcardLike(item.DisplayName.ToLowerInvariant(), normalizedFilter))
                    {
                        filteredItems.Add(item);
                    }
                }
            }

            return filteredItems;
        }

        public int IndexOf(CShellItem value)
        {
            return _items.IndexOf(value);
        }

        /// <summary>
        /// Note: this is slow O(n).
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public int IndexOf(string name)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Compare(_items[i].GetFileName(), name, true) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Note: this is slow O(n).
        /// </summary>
        /// <param name="pidl"></param>
        /// <returns></returns>
	     public int IndexOf(IntPtr pidl)
        {
            //fast memcomp
            for (int i = 0; i < _items.Count; i++)
            {
                if (CPidl.IsBinaryEqual(_items[i].PIDL, pidl))
                {
                    return i;
                }
            }

            // Fallback to more expensive but robust comparison for absolute PIDLs
            for (int i = 0; i < _items.Count; i++)
            {
                if (CPidl.ResolvesToSamePathOrName(_items[i].PIDL, pidl))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Note: this is slow O(n).
        /// </summary>
        /// <param name="relPidl"></param>
        /// <returns></returns>
        public int IndexOfRelative(IntPtr relPidl)
        {
            if (relPidl == IntPtr.Zero) return -1;

            // First try binary equal as it is fastest
            for (int i = 0; i < _items.Count; i++)
            {
                if (CPidl.IsBinaryEqual(_items[i].LastPIDL, relPidl))
                {
                    return i;
                }
            }

            // Fallback to shell-based comparison
            IShellFolder folder = _parent.GetIShellFolder();
            if (folder != null)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (CPidl.AreEqual(folder, _items[i].LastPIDL, relPidl))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        internal void Insert(int index, CShellItem value)
        {
            lock (_items)
            {
                _items.Insert(index, value);
                if (_dictionary is not null)
                    _dictionary[value.DisplayName] = value;
            }
        }

        internal void Remove(CShellItem value)
        {
            lock (_items)
            {
                _items.Remove(value);
                if (_dictionary is not null)
                    _dictionary.Remove(value.DisplayName);
            }
        }

        internal void RemoveRange(IEnumerable<CShellItem> items)
        {
            if (items == null) return;
            var toRemove = new HashSet<CShellItem>(items);
            lock (_items)
            {
                _items.RemoveAll(i => toRemove.Contains(i));
                if (_dictionary is not null)
                {
                    foreach (var item in items)
                        _dictionary.Remove(item.DisplayName);
                }
            }
        }

        internal void Remove(string name)
        {
            lock (_items)
            {
                int index = IndexOf(name);
                if (index > -1)
                {
                    _items.RemoveAt(index);
                    if (_dictionary is not null)
                        _dictionary.Remove(name);
                }
            }
        }

        internal void RemoveAt(int index)
        {
            lock (_items)
            {
                if (_dictionary is not null)
                    _dictionary.Remove(_items[index].DisplayName);
                _items.RemoveAt(index);
            }
        }

        public CShellItem this[int index]
        {
            get
            {
                return _items[index];
            }
        }

        public CShellItem this[string name]
        {
            get
            {
                int index = IndexOf(name);
                return index > -1 ? _items[index] : null;
            }
            set
            {
                int index = IndexOf(name);
                if (index > -1)
                {
                    _items[index] = value;
                }
            }
        }

        /// <summary>
        /// Gets the item matching either an absolute PIDL or a relative PIDL (last segment).
        /// </summary>
        public CShellItem this[IntPtr pidl]
        {
            get
            {
                int index = IndexOf(pidl);
                if (index == -1) index = IndexOfRelative(pidl);
                return index > -1 ? _items[index] : null;
            }
            set
            {
                int index = IndexOf(pidl);
                if (index == -1) index = IndexOfRelative(pidl);
                if (index > -1)
                {
                    _items[index] = value;
                }
            }
        }

        public IEnumerator<CShellItem> GetEnumerator()
        {
            lock (_items)
                return new List<CShellItem>(_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void CopyTo(Array array, int index)
        {
            lock (_items)
                ((ICollection)_items).CopyTo(array, index);
        }

        public CShellItem[] ToArray()
        {
            lock (_items)
                return _items.ToArray();
        }
    }
}
