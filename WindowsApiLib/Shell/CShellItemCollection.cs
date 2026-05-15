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
        private readonly List<CShellItem> m_items;
        private readonly CShellItem m_item;
        private readonly object m_syncRoot = new object();

        public CShellItemCollection(CShellItem item)
        {
            m_item = item;
            m_items = new List<CShellItem>();
        }

        public CShellItem CShellItem
        {
            get
            {
                return m_item;
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
        

        public void Sort()
        {
            lock (m_syncRoot)
                m_items.Sort();
        }

        internal int Add(CShellItem value)
        {
            if (value.Parent is null)
            {
                value.SetParent(m_item);
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
                if (CPidl.IsEqual(itm.PIDL, pidl))
                {
                    return true;
                }
            }
            return false;
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
                if (CPidl.IsEqual(m_items[i].PIDL, pidl))
                {
                    return i;
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

        public CShellItem this[IntPtr pidl]
        {
            get
            {
                int index = IndexOf(pidl);
                return index > -1 ? m_items[index] : null;
            }
            set
            {
                int index = IndexOf(pidl);
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
