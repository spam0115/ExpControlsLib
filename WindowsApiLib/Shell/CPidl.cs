using System.Collections;
using System.Runtime.InteropServices;
using WindowsApiLib;
using WindowsApiLib.Shell;

namespace WindowsApiLib
{
    /// <summary>cPidl class contains a Byte() representation of a PIDL and
    /// certain Methods and Properties for comparing one cPidl to another</summary>
    public class CPidl : IEnumerable
    {

        #region        Private Fields
        private readonly byte[] m_bytes;   // The local copy of the PIDL
        private readonly int m_ItemCount;      // the # of ItemIDs in this ItemIDList (PIDL)
                                               // Private ReadOnly m_OffsetToRelative As Integer 'the index of the start of the last itemID in m_bytes
        #endregion

        #region        Constructor
        /// <summary>
    /// Given an IntPtr pointing to a valid PIDL, copy the bytes of that PIDL to a Byte()
    /// </summary>
    /// <param name="Pidl">IntPtr pointing to a valid PIDL</param>
        public CPidl(IntPtr Pidl)
        {
            int cb = CShellItem.ItemIDListSize(Pidl);
            if (cb > 0)
            {
                m_bytes = new byte[cb + 1 + 1];
                Marshal.Copy(Pidl, m_bytes, 0, cb);
            }
            // DumpPidl(pidl)
            else
            {
                m_bytes = new byte[2];
            }  // This is the DeskTop (we hope)
               // ensure nulnul
            m_bytes[m_bytes.Length - 2] = 0;
            m_bytes[m_bytes.Length - 1] = 0;
            m_ItemCount = CShellItem.PidlCount(Pidl);
        }
        #endregion

        #region        Public Properties
        /// <summary>
    /// Returns this cPIDL's Byte() containing the Bytes of the original PIDL
    /// </summary>
    /// <returns>This cPIDL's Byte() containing the Bytes of the original PIDL</returns>
        public byte[] PidlBytes
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
        public int Length
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
        public int ItemCount
        {
            get
            {
                return m_ItemCount;
            }
        }

        #endregion

        #region        Public Instance Methods -- ToPIDL, Decompose, and IsEqual

        /// <summary> Copy the contents of a byte() containing a PIDL to
    /// CoTaskMemory, returning an IntPtr that points to that mem block
    /// Assumes that this cPidl is properly terminated, as all New 
    /// cPidls are.
    /// </summary>
    /// <returns>The newly created PIDL</returns>
    /// <remarks> Caller must Free the returned IntPtr when done with the returned PIDL.</remarks>
        public IntPtr ToPIDL()
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
        public object[] Decompose()
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
    /// Returns True if input cPidl's content exactly match the 
    /// contents of this instance, False otherwise
    /// </summary>
    /// <param name="other">The CPidl to compare to this instance</param>
    /// <returns>True if "other" is Equal to this instance, False otherwise</returns>
        public bool IsEqual(CPidl other)
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
        #endregion

        #region        Public Shared Methods

        #region            JoinPidlBytes
        /// <summary> Join two byte arrays containing PIDLS. 
    /// Returns NOTHING if error
    /// </summary>
    /// <returns>A Byte() containing the resultant ITEMIDLIST.</returns>
    /// <remarks>Both Byte() must be properly terminated (nulnul)</remarks>
        public static byte[] JoinPidlBytes(byte[] b1, byte[] b2)
        {
            if (CShellItem.IsValidPidl(b1) & CShellItem.IsValidPidl(b2))
            {
                var b = new byte[b1.Length + b2.Length - 3 + 1]; // allow for leaving off first nulnul
                Array.Copy(b1, b, b1.Length - 2);
                Array.Copy(b2, 0, b, b1.Length - 2, b2.Length);
                if (CShellItem.IsValidPidl(b))
                {
                    return b;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }
        #endregion

        #region            BytesToPidl
        /// <summary>
    /// Copy the contents of a byte() containing a pidl to
    /// CoTaskMemory, returning an IntPtr that points to that mem block
    /// Caller must free the IntPtr when done with it
    /// </summary>
    /// <param name="b">A Byte() containing a valid PIDL</param>
    /// <returns>An IntPtr pointing to the newly allocated and created PIDL</returns>
    /// <remarks>Caller is responsible for Freeing the PIDL when no longer required</remarks>
        public static IntPtr BytesToPidl(byte[] b)
        {
            IntPtr BytesToPidlRet = default;
            BytesToPidlRet = IntPtr.Zero;       // assume failure
            if (CShellItem.IsValidPidl(b))
            {
                int bLen = b.Length;
                BytesToPidlRet = Marshal.AllocCoTaskMem(bLen);
                if (BytesToPidlRet.Equals(IntPtr.Zero))
                    return BytesToPidlRet; // another bad error
                Marshal.Copy(b, 0, BytesToPidlRet, bLen);
            }

            return BytesToPidlRet;
        }
        #endregion

        #region            StartsWith
        /// <summary>returns True if the beginning of pidlA matches PidlB exactly for pidlB's entire length</summary>
    /// <returns>True if the beginning of pidlA matches PidlB exactly for pidlB's entire length</returns>
        public static bool StartsWith(IntPtr pidlA, IntPtr pidlB)
        {
            return StartsWith(new CPidl(pidlA), new CPidl(pidlB));
        }

        /// <summary>returns True if the beginning of A matches B exactly for B's entire length</summary>
    /// <returns>True if the beginning of A matches B exactly for pidlB's entire length</returns>
        public static bool StartsWith(CPidl A, CPidl B)
        {
            return A.StartsWith(B);
        }

        /// <summary>Returns true if the CPidl input parameter exactly matches the
    /// beginning of this instance of CPidl</summary>
    /// <returns>True if the CPidl input parameter exactly matches the
    /// beginning of this instance of CPidl</returns>
        public bool StartsWith(CPidl cp)
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
        #endregion

        #endregion

        #region        GetEnumerator
        /// <summary>
    /// Obtains a new Enumerator for this cPidl
    /// </summary>
    /// <returns>a new Enumerator for this cPidl</returns>
        public IEnumerator GetEnumerator()
        {
            return new ICPidlEnumerator(m_bytes);
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