using System.Collections;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// Provides a Synchronized wrapper for a Strongly Typed Arraylist of CShItems. 
    /// </summary>
    /// <remarks></remarks>
    public class CShellItemCollection : IEnumerable, ICollection
    {
        private readonly ArrayList m_items;
        private readonly CShellItem m_item;

        public CShellItemCollection(CShellItem item)
        {
            m_item = item;
            var m_tmp = new ArrayList();
            m_items = ArrayList.Synchronized(m_tmp);
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
                return m_items.SyncRoot;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return m_items.IsSynchronized;
            }
        }

        public int Count
        {
            get
            {
                return m_items.Count;
            }
        }

        public void Sort()
        {
            m_items.Sort();
        }

        internal int Add(CShellItem value)
        {
            if (value.Parent is null)
            {
                value.SetParent(m_item);
            }
            return m_items.Add(value);
        }

        internal void AddRange(ICollection value)
        {
            m_items.AddRange(value);
        }

        internal void Clear()
        {
            m_items.Clear();
        }

        public bool Contains(CShellItem value)
        {
            return m_items.Contains(value);
        }

        public bool Contains(string name)
        {
            foreach (CShellItem itm in this)
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
            // DumpPidl(pidl)
            foreach (CShellItem itm in this)
            {
                if (CPidl.IsEqual(itm.PIDL, pidl))
                {
                    return true;
                }
                else
                {
                    // DumpPidl(itm.PIDL)
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
            int i;
            var loopTo = m_items.Count - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if (string.Compare(((CShellItem)m_items[i]).GetFileName(), name, true) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        public int IndexOf(IntPtr pidl)
        {
            int i;
            var loopTo = m_items.Count - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if (CPidl.IsEqual(((CShellItem)m_items[i]).PIDL, pidl))
                {
                    return i;
                }
            }
            return -1;
        }

        internal void Insert(int index, CShellItem value)
        {
            m_items.Insert(index, value);
        }

        internal void Remove(CShellItem value)
        {
            m_items.Remove(value);
        }

        internal void Remove(string name)
        {
            int index = IndexOf(name);

            if (index > -1)
            {
                RemoveAt(index);
            }
        }

        internal void RemoveAt(int index)
        {
            m_items.RemoveAt(index);
        }

        public CShellItem this[int index]
        {
            get
            {
                return (CShellItem)m_items[index];
            }
        }

        public CShellItem this[string name]
        {
            get
            {
                int index = IndexOf(name);
                if (index > -1)
                {
                    return (CShellItem)m_items[index];
                }
                else
                {
                    return null;
                }
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
                if (index > -1)
                {
                    return (CShellItem)m_items[index];
                }
                else
                {
                    return null;
                }
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

        public IEnumerator GetEnumerator()
        {
            return m_items.GetEnumerator();
        }
        /// <summary>
    /// Copys all CShItems contained in this instance to an Array (of CShItems), starting at the supplied
    /// index into the Array.
    /// </summary>
    /// <param name="array">CShellItem Array to copy to.</param>
    /// <param name="index">Index into array to copy the first instance of CShellItem.</param>
    /// <remarks>Is Thread save.</remarks>
        public void CopyTo(Array array, int index)
        {
            lock (m_items.SyncRoot)
                m_items.CopyTo(array, index);
        }
        /// <summary>
    /// Returns all CShItems contained in this instance.
    /// </summary>
    /// <returns>An Array of CShItems</returns>
    /// <remarks>Is Thread safe.</remarks>
        public CShellItem[] ToArray()
        {
            lock (m_items.SyncRoot)
                return (CShellItem[])m_items.ToArray(typeof(CShellItem));
        }
    }
}