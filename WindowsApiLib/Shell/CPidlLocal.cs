using System.Collections;
using System.Runtime.InteropServices;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{
    /// <summary>
    /// A <see cref="CPidl"/> descendant that maintains a local managed (byte[])
    /// copy of a PIDL. Use this when you need to hold the PIDL bytes in .NET memory;
    /// use the base <see cref="CPidl"/> for static/native-IntPtr operations that do
    /// not require a local byte copy.
    /// </summary>
    public class CPidlLocal : CPidl
    {
        #region        Private Fields
        private readonly byte[] m_bytes;   // The local copy of the PIDL
        private readonly int m_ItemCount;      // the # of ItemIDs in this ItemIDList (PIDL)

        // Private ReadOnly m_OffsetToRelative As Integer 'the index of the start of the last itemID in m_bytes
        #endregion

        #region        Constructors
        /// <summary>
        /// Given an IntPtr pointing to a valid PIDL allocated via the COM allocator
        /// (e.g. by <c>ILCreateFromPathW</c>, <c>ILClone</c>, <c>ILCombine</c>),
        /// this instance takes ownership of that native PIDL, copies its bytes into
        /// a managed <see cref="byte"/>[], and releases the native PIDL when this
        /// instance is disposed or garbage-collected.
        /// </summary>
        /// <remarks>
        /// Callers must NOT continue to use or free the passed <paramref name="pidl"/>
        /// after this call — ownership has been transferred. To retain ownership of
        /// the original, pass a clone, e.g.
        /// <c>new CPidlLocal(CPidl.Clone(myPidl))</c>.
        /// </remarks>
        /// <param name="pidl">A native PIDL allocated via the COM allocator. May be
        /// <see cref="IntPtr.Zero"/> (treated as the empty/Desktop PIDL).</param>
        public CPidlLocal(IntPtr pidl)
        {
            // Take ownership of the native PIDL so it is released on Dispose/GC.
            TakeOwnershipOf(pidl);

            int cb = GetPidlLength(pidl);
            if (cb > 0)
            {
                m_bytes = new byte[cb + 1 + 1];
                Marshal.Copy(pidl, m_bytes, 0, cb);
            }
            else
            {
                m_bytes = new byte[2];
            }  // This is the DeskTop (we hope)
                // ensure nulnul
            m_bytes[m_bytes.Length - 2] = 0;
            m_bytes[m_bytes.Length - 1] = 0;
            m_ItemCount = SegmentCount(pidl);
        }

        public CPidlLocal(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);

            if (pidl == IntPtr.Zero) throw new ArgumentException("Invalid path provided to CPidlLocal.");

            // ILCreateFromPathW allocates the PIDL via the COM allocator; this instance
            // takes ownership so it is released on Dispose / finalization.
            TakeOwnershipOf(pidl);

            int cb = GetPidlLength(pidl);
            if (cb > 0)
            {
                m_bytes = new byte[cb + 1 + 1];
                Marshal.Copy(pidl, m_bytes, 0, cb);
            }
            else
            {
                m_bytes = new byte[2];
            }  // This is the DeskTop (we hope)
               // ensure nulnul
            m_bytes[m_bytes.Length - 2] = 0;
            m_bytes[m_bytes.Length - 1] = 0;
            m_ItemCount = SegmentCount(pidl);
        }
        #endregion

        #region        Public Properties
        /// <summary>
        /// Returns this cPIDL's Byte() containing the Bytes of the original PIDL
        /// </summary>
        /// <returns>This cPIDL's Byte() containing the Bytes of the original PIDL</returns>
        public override byte[] PidlBytes
        {
            get
            {
                return m_bytes;
            }
        }

        /// <summary>
        /// Returns the number of Bytes in this cPidl
        /// </summary>
        /// <returns>The number of Bytes in this cPidl</returns>
        public override int Length
        {
            get
            {
                return m_bytes.Length;
            }
        }

        /// <summary>
        /// Returns the number of Item IDs in this instance
        /// </summary>
        /// <returns>The number of Item IDs in this cPidl</returns>
        public override int ItemCount
        {
            get
            {
                return m_ItemCount;
            }
        }

        #endregion

        #region        Public instance methods

        /// <summary>
        /// Returns True if input cPidl's content exactly match the
        /// contents of this instance, False otherwise
        /// </summary>
        /// <param name="other">The CPidl to compare to this instance</param>
        /// <returns>True if "other" is Equal to this instance, False otherwise</returns>
        public override bool IsBinaryEqual(CPidl other)
        {
            bool IsEqualRet = default;
            IsEqualRet = false;     // assume not
            if (other.Length != Length)
                return IsEqualRet;
            byte[] ob = other.PidlBytes;
            int i;
            var loopTo = Length - 1;
            for (i = 0; i <= loopTo; i++)  // note: we look at nulnul also
            {
                if (ob[i] != m_bytes[i])
                    return IsEqualRet;
            }
            return true;         // all equal on fall thru
        }

        /// <summary>Returns true if the CPidl input parameter exactly matches the
        /// beginning of this instance of CPidl</summary>
        /// <returns>True if the CPidl input parameter exactly matches the
        /// beginning of this instance of CPidl</returns>
        public override bool StartsWith(CPidl cp)
        {
            byte[] b = cp.PidlBytes;
            if (b.Length > m_bytes.Length)
                return false; // input is longer
            int i;
            var loopTo = b.Length - 3;
            for (i = 0; i <= loopTo; i++) // allow for nulnul at end of cp.PidlBytes
            {
                if (b[i] != m_bytes[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Copy the contents of a byte() containing a PIDL to
        /// CoTaskMemory, returning an IntPtr that points to that mem block
        /// Assumes that this cPidl is properly terminated, as all New
        /// cPidls are.
        /// </summary>
        /// <returns>The newly created PIDL</returns>
        /// <remarks> Caller must Free the returned IntPtr when done with the returned PIDL.</remarks>
        public override IntPtr ToPIDL()
        {
            IntPtr ToPIDLRet = default;
            ToPIDLRet = BytesToPidl(m_bytes);
            return ToPIDLRet;
        }

        /// <summary>
        /// Returns an object containing a byte() for each of this cPidl's
        /// ITEMIDs (individual PIDLS), in order such that obj(0) is
        /// a byte() containing the bytes of the first ITEMID, etc.
        /// Each ITEMID is properly terminated with a nulnul    ''' </summary>
        /// <returns>An Object containing a Byte() for each ID in the PIDL</returns>
        /// <remarks></remarks>
        public override object[] Decompose()
        {
            var bArrays = new object[ItemCount];
            ICPidlEnumerator eByte = (ICPidlEnumerator)GetEnumerator();
            var i = default(int);
            while (eByte.MoveNext())
            {
                bArrays[i] = eByte.Current;
                i += 1;
            }
            return bArrays;
        }

        /// <summary>
        /// Obtains a new Enumerator for this cPidl
        /// </summary>
        /// <returns>a new Enumerator for this cPidl</returns>
        public override IEnumerator GetEnumerator()
        {
            return new ICPidlEnumerator(m_bytes);
        }

        public override IShellFolder GetIShellFolder()
        {
            return GetIShellFolder(this.PidlBytes);
        }

        #endregion

        #region        CPIDL enumerator Class
        private class ICPidlEnumerator : IEnumerator
        {

            private int m_sPos;   // the first index in the current PIDL
            private int m_ePos;   // the last index in the current PIDL
            private readonly byte[] m_bytes;   // the local copy of the PIDL
            private readonly bool m_NotEmpty = false; // the desktop PIDL is zero length

            /// <summary>
            /// Creates a New instance of ICPidlEnumerator
            /// </summary>
            /// <param name="b">A Byte() containing a valid PIDL</param>
            public ICPidlEnumerator(byte[] b)
            {
                m_bytes = b;
                if (b.Length > 0)
                    m_NotEmpty = true;
                m_sPos = -1;
                m_ePos = -1;
            }

            /// <summary>
            /// Returns the Byte() containing the Current Item ID
            /// </summary>
            /// <returns>Current ID as Byte()</returns>
            public object Current
            {
                get
                {
                    if (m_sPos < 0)
                        throw new InvalidOperationException("ICPidlEnumerator --- attempt to get Current with invalidated list");
                    var b = new byte[m_ePos - m_sPos + 2 + 1];    // room for nulnul
                    Array.Copy(m_bytes, m_sPos, b, 0, b.Length - 2);
                    b[b.Length - 2] = 0;
                    b[b.Length - 1] = 0; // add nulnul
                    return b;
                }
            }

            /// <summary>
            /// Moves the Current pointer to the Next Item ID in this cPidl
            /// </summary>
            /// <returns>True if successful, False if there is no Next Item ID</returns>
            /// <remarks></remarks>
            public bool MoveNext()
            {
                if (m_NotEmpty)
                {
                    if (m_sPos < 0)
                    {
                        m_sPos = 0;
                        m_ePos = -1;
                    }
                    else
                    {
                        m_sPos = m_ePos + 1;
                    }
                    if (m_bytes.Length < m_sPos + 1)
                        throw new InvalidCastException("Malformed PIDL");
                    int cb = m_bytes[m_sPos] + m_bytes[m_sPos + 1] * 256;
                    if (cb == 0)
                    {
                        return false; // have passed all back
                    }
                    else
                    {
                        m_ePos += cb;
                    }
                }
                else
                {
                    m_sPos = 0;
                    m_ePos = 0;
                    return false;
                }        // in this case, we have exhausted the list of 0 ITEMIDs
                return true;
            }

            /// <summary>
            /// Resets the Current pointer to the beginning of this cPidl
            /// </summary>
            /// <remarks></remarks>
            public void Reset()
            {
                m_sPos = -1;
                m_ePos = -1;
            }
        }
        #endregion

    }
}
