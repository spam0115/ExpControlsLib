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
        private readonly CShellItem m_parent; //needed to set parent values
        private readonly List<CShellItem> m_items;
        private readonly object m_syncRoot = new object();

        /// <summary>
        /// A collection of CShellItems releted to a given parent
        /// </summary>
        /// <param name="parent">The parent item for all items that will eventually be added to this collection.</param>
        public CShellItemCollection(CShellItem parent)
        {
            m_parent = parent;
            m_items = new List<CShellItem>();
        }

        public CShellItem Owner
        {
            get
            {
                return m_parent;
            }
        }

        public object SyncRoot
        {
            get
            {
                return m_syncRoot;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return true;
            }
        }

        public int Count => m_items.Count;

        public List<CShellItem> Items => m_items;

        public void Sort()
        {
            lock (m_syncRoot)
                m_items.Sort();
        }

        internal int Add(CShellItem value)
        {
            if (value.Parent is null)
            {
                value.SetParent(m_parent);
            }
            lock (m_syncRoot)
            {
                m_items.Add(value);
                return m_items.Count - 1;
            }
        }

        internal void AddRange(IEnumerable<CShellItem> value)
        {
            lock (m_syncRoot)
                m_items.AddRange(value);
        }

        internal void Clear()
        {
            lock (m_syncRoot)
                m_items.Clear();
        }

        public bool Contains(CShellItem value)
        {
            return m_items.Contains(value);
        }

        public bool Contains(string name)
        {
            foreach (CShellItem itm in m_items)
            {
                if (string.Compare(itm.GetFileName(), name, true) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public bool Contains(IntPtr pidl)
        {
            foreach (CShellItem itm in m_items)
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
            var result = CShellItemHierachyManager.Find(m_parent, pidl);

            return result != null;
        }

        public int IndexOf(CShellItem value)
        {
            return m_items.IndexOf(value);
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < m_items.Count; i++)
            {
                if (string.Compare(m_items[i].GetFileName(), name, true) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

	public int IndexOf(IntPtr pidl)
        {
            for (int i = 0; i < m_items.Count; i++)
            {
                if (CPidl.IsBinaryEqual(m_items[i].PIDL, pidl))
                {
                    return i;
                }
            }

            // Fallback to more expensive but robust comparison for absolute PIDLs
            for (int i = 0; i < m_items.Count; i++)
            {
                if (CPidl.ResolvesToSamePathOrName(m_items[i].PIDL, pidl))
                {
                    return i;
                }
            }
            return -1;
        }


        public int IndexOfRelative(IntPtr relPidl)
        {
            if (relPidl == IntPtr.Zero) return -1;

            // First try binary equal as it is fastest
            for (int i = 0; i < m_items.Count; i++)
            {
                if (CPidl.IsBinaryEqual(m_items[i].LastPIDL, relPidl))
                {
                    return i;
                }
            }

            // Fallback to shell-based comparison
            IShellFolder folder = m_parent.GetIShellFolder();
            if (folder != null)
            {
                for (int i = 0; i < m_items.Count; i++)
                {
                    if (CPidl.AreEqual(folder, m_items[i].LastPIDL, relPidl))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        internal void Insert(int index, CShellItem value)
        {
            lock (m_syncRoot)
                m_items.Insert(index, value);
        }

        internal void Remove(CShellItem value)
        {
            lock (m_syncRoot)
                m_items.Remove(value);
        }

        internal void RemoveRange(IEnumerable<CShellItem> items)
        {
            if (items == null) return;
            var toRemove = new HashSet<CShellItem>(items);
            lock (m_syncRoot)
            {
                m_items.RemoveAll(i => toRemove.Contains(i));
            }
        }

        internal void Remove(string name)
        {
            lock (m_syncRoot)
            {
                int index = IndexOf(name);
                if (index > -1)
                {
                    m_items.RemoveAt(index);
                }
            }
        }

        internal void RemoveAt(int index)
        {
            lock (m_syncRoot)
                m_items.RemoveAt(index);
        }

        public CShellItem this[int index]
        {
            get
            {
                return m_items[index];
            }
        }

        public CShellItem this[string name]
        {
            get
            {
                int index = IndexOf(name);
                return index > -1 ? m_items[index] : null;
            }
            set
            {
                int index = IndexOf(name);
                if (index > -1)
                {
                    m_items[index] = value;
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
                return index > -1 ? m_items[index] : null;
            }
            set
            {
                int index = IndexOf(pidl);
                if (index == -1) index = IndexOfRelative(pidl);
                if (index > -1)
                {
                    m_items[index] = value;
                }
            }
        }

        public IEnumerator<CShellItem> GetEnumerator()
        {
            lock (m_syncRoot)
                return new List<CShellItem>(m_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void CopyTo(Array array, int index)
        {
            lock (m_syncRoot)
                ((ICollection)m_items).CopyTo(array, index);
        }

        public CShellItem[] ToArray()
        {
            lock (m_syncRoot)
                return m_items.ToArray();
        }
    }
}
