using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{
    /// <summary>cPidl class contains a Byte() representation of a PIDL and
    /// certain Methods and Properties for comparing one cPidl to another</summary>
    public class CPidl : IEnumerable
    {
        #region        Private Fields
        private readonly byte[] m_bytes;   // The local copy of the PIDL
        private readonly int m_ItemCount;      // the # of ItemIDs in this ItemIDList (PIDL)
        private string value;

        // Private ReadOnly m_OffsetToRelative As Integer 'the index of the start of the last itemID in m_bytes
        #endregion

        #region        Constructor
        /// <summary>
        /// Given an IntPtr pointing to a valid PIDL, copy the bytes of that PIDL to a Byte()
        /// </summary>
        /// <param name="Pidl">IntPtr pointing to a valid PIDL</param>
        public CPidl(IntPtr Pidl)
        {
            int cb = ItemIDListSize(Pidl);
            if (cb > 0)
            {
                m_bytes = new byte[cb + 1 + 1];
                Marshal.Copy(Pidl, m_bytes, 0, cb);
            }
            else
            {
                m_bytes = new byte[2];
            }  // This is the DeskTop (we hope)
               // ensure nulnul
            m_bytes[m_bytes.Length - 2] = 0;
            m_bytes[m_bytes.Length - 1] = 0;
            m_ItemCount = SegmentCount(Pidl);
        }

        public CPidl(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);

            if (pidl == IntPtr.Zero) throw new ArgumentException("Invalid path provided to CPidl.");
                
            int cb = ItemIDListSize(pidl);
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

        #region        Public Static Methods

        /// <summary> Join two byte arrays containing PIDLS into a managed (non-com) array. 
        /// Returns NOTHING if error
        /// </summary>
        /// <returns>A Byte() containing the resultant ITEMIDLIST.</returns>
        /// <remarks>Both Byte() must be properly terminated (nulnul)</remarks>
        public static byte[] JoinPidlBytes(byte[] b1, byte[] b2)
        {
            if (IsValid(b1) & IsValid(b2))
            {
                var b = new byte[b1.Length + b2.Length - 3 + 1]; // allow for leaving off first nulnul
                Array.Copy(b1, b, b1.Length - 2);
                Array.Copy(b2, 0, b, b1.Length - 2, b2.Length);
                if (IsValid(b))
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
            if (IsValid(b))
            {
                int bLen = b.Length;
                BytesToPidlRet = Marshal.AllocCoTaskMem(bLen);
                if (BytesToPidlRet.Equals(IntPtr.Zero))
                    return BytesToPidlRet; // another bad error
                Marshal.Copy(b, 0, BytesToPidlRet, bLen);
            }

            return BytesToPidlRet;
        }

        /// <summary>returns True if the beginning of pidlA matches PidlB exactly for pidlB's entire length</summary>
        /// <returns>True if the beginning of pidlA matches PidlB exactly for pidlB's entire length</returns>
        public static bool StartsWith(IntPtr pidlA, IntPtr pidlB)
        {
            return StartsWith(new CPidl(pidlA), new CPidl(pidlB));
        }

        /// <summary>returns True if the beginning of A matches B exactly for B's entire length</summary>
        /// <returns>True if the beginning of A matches B exactly for B's entire length</returns>
        public static bool StartsWith(CPidl A, CPidl B)
        {
            return A.StartsWith(B);
        }


        /// <summary>
        /// Computes the actual size of the ItemIDList (all SHItems) pointed to by pidl.
        /// </summary>
        /// <param name="pidl">The pidl pointing to an ItemIDList</param>
        /// <returns> Returns actual size of the ItemIDList, less the terminating nulnul</returns>
        public static int ItemIDListSize(IntPtr pidl)
        {
            if (!pidl.Equals(IntPtr.Zero))
            {
                return (int)ILGetSize(pidl);
                //int i = ItemIDSize(pidl);
                //int b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
                //while (b > 0)
                //{
                //    i += b;
                //    b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
                //}
                //return i;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Counts the total number of SHItems in input pidl
        /// </summary>
        /// <param name="pidl">The pidl to obtain the count for</param>
        /// <returns> Returns the count of SHItems pointed to by pidl</returns>
        public static int SegmentCount(IntPtr pidl)
        {
            if (!pidl.Equals(IntPtr.Zero))
            {
                int cnt = 0;
                int i = 0;
                int b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
                while (b > 0)
                {
                    cnt += 1;
                    i += b;
                    b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
                }
                return cnt;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Given a PIDL(Pointer to ID List) as IntPtr, return an Array of PIDL, one for each ID in the List.
        /// Each PIDL in the returned Array will be a single, well formed and terminated ID.
        /// </summary>
        /// <param name="pidl">The PIDL to be Factored</param>
        /// <returns>An Array of PIDL, each a Single Relative PIDL</returns>
        /// <remarks>The returned PIDLs must be Released when no longer needed by calling PIDLFree.</remarks>
        public static IntPtr[] Decompose(IntPtr pidl)
        {
            int lim = (int)ItemIDListSize(pidl);
            var PIDLs = new IntPtr[(SegmentCount(pidl))];
            int i = 0;
            var curB = default(int);
            int offSet;
            while (curB < lim)
            {
                var thisPtr = new IntPtr(pidl.ToInt64() + curB); // 6/8/2012 - ToInt64 works on both 32 & 64 bit systems
                offSet = Marshal.ReadByte(thisPtr) + Marshal.ReadByte(thisPtr, 1) * 256;
                PIDLs[i] = Marshal.AllocCoTaskMem(offSet + 2);
                var b = new byte[offSet + 1 + 1];
                Marshal.Copy(thisPtr, b, 0, offSet);
                b[offSet] = 0;
                b[offSet + 1] = 0;
                Marshal.Copy(b, 0, PIDLs[i], offSet + 2);
                // DumpPidl(PIDLs(i))
                curB += offSet;
                i += 1;
            }
            return PIDLs;
        }

        /// <summary>
        /// AreBytesEqual performs a binary comparison of the contents of two ItemIDLists pointed to by two Pidls.
        /// </summary>
        /// <param name="Pidl1">IntPtr pointing to an ItemIDList.</param>
        /// <param name="pidl2">IntPtr pointing to an ItemIDList.</param>
        /// <returns>True if all bytes are the same, False otherwise.</returns>
        /// <remarks>A substitute for ILIsEqual on pre-Win2K systems, and used by IsReallyEqual when binary
        /// comparison is needed on Win2K and above systems.</remarks>
        public static bool AreBytesEqual(IntPtr Pidl1, IntPtr pidl2)
        {
            int cb1;
            int cb2;
            cb1 = ItemIDListSize(Pidl1);
            cb2 = ItemIDListSize(pidl2);
            if (cb1 != cb2)
                return false;
            int lim32 = cb1 / 4;

            int i;
            var loopTo = lim32 - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if (Marshal.ReadInt32(Pidl1, i * 4) != Marshal.ReadInt32(pidl2, i * 4))
                {
                    // Debug.WriteLine("Mismatch at Byte " & i * 4 & " (&H" & Hex(i * 4) & ")")
                    return false;
                }
            }
            int limB = cb1 % 4;
            int offset = lim32 * 4;
            var loopTo1 = limB - 1;
            for (i = 0; i <= loopTo1; i++)
            {
                if (Marshal.ReadByte(Pidl1, offset + i) != Marshal.ReadByte(pidl2, offset + i))
                {
                    // Debug.WriteLine("Mismatch at Byte " & i + offset & " (&H" & Hex(i + offset) & ")")
                    return false;
                }
            }
            return true; // made it to here, so they are equal
        }

        /// <summary>
        /// IsEqual compares two ItemIDLists. On Win2K and above systems, it uses the ILIsEqual API, which only
        /// compares portions of each ItemID. On such systems, the other portions of the ItemID may differ in a 
        /// few bytes -- typically this is desired behavior, but not in UPDATEDIR cases which do a Byte 
        /// comparison in addition to IsEqual.
        /// </summary>
        /// <param name="Pidl1">IntPtr pointing to an ItemIDList.</param>
        /// <param name="Pidl2">IntPtr pointing to an ItemIDList.</param>
        /// <returns>True if ILIsEqual returns or would return True, False otherwise.</returns>
        /// <remarks></remarks>
        public static bool IsEqual(IntPtr Pidl1, IntPtr Pidl2)
        {
            if (Pidl1 == Pidl2) return true;
            try
            {
                return ILIsEqual(Pidl1, Pidl2);
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                throw;
            }
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
        /// Concatenates the contents of two pidls into a new Pidl (ended by 00)
        /// allocating CoTaskMem to hold the result,
        /// placing the concatenation (followed by 00) into the
        /// allocated Memory,
        /// and returning an IntPtr pointing to the allocated mem
        /// </summary>
        /// <param name="pidl1">IntPtr to a well formed SHItemIDList or IntPtr.Zero</param>
        /// <param name="pidl2">IntPtr to a well formed SHItemIDList or IntPtr.Zero</param>
        /// <returns>Returns a ptr to an ItemIDList containing the 
        ///   concatenation of the two (followed by the req 2 zeros
        ///   Caller must Free this pidl when done with it</returns>
        /// <remarks>On Win2k or above systems, will use the API function ILCombine, otherwise performs
        /// byte array manipulation to accomplish the same thing.
        /// Caller must free the returned Pidl when no longer needed.</remarks>
        public static IntPtr Concatenate(IntPtr pidl1, IntPtr pidl2)
        {
            return ILCombine(pidl1, pidl2);
            //if (WinSDK.Win2KOrAbove)
            //{
            //    return ILCombine(pidl1, pidl2);
            //}
            //else
            //{
            //    int cb1;
            //    int cb2;
            //    cb1 = ItemIDListSize(pidl1);
            //    cb2 = ItemIDListSize(pidl2);
            //    int rawCnt = cb1 + cb2;
            //    if (rawCnt > 0)
            //    {
            //        var b = new byte[rawCnt + 1 + 1];
            //        if (cb1 > 0)
            //        {
            //            Marshal.Copy(pidl1, b, 0, cb1);
            //        }
            //        if (cb2 > 0)
            //        {
            //            Marshal.Copy(pidl2, b, cb1, cb2);
            //        }
            //        var rVal = Marshal.AllocCoTaskMem(cb1 + cb2 + 2);
            //        b[rawCnt] = 0;
            //        b[rawCnt + 1] = 0;
            //        Marshal.Copy(b, 0, rVal, rawCnt + 2);
            //        return rVal;
            //    }
            //    else
            //    {
            //        return IntPtr.Zero;
            //    }
            //}
        }

        /// <summary>
        /// Trim the last path item from a pidl and return the parent.
        /// </summary>
        /// <param name="pidl"></param>
        /// <returns></returns>
        public static IntPtr TrimLast(IntPtr pidl)
        {
            IntPtr pidlCopy = ILClone(pidl);
            ILRemoveLastID(pidlCopy);

            return pidlCopy;
        }

        /// <summary>
        /// SplitPidl returns an ItemIDList with the last ItemID trimmed off.
        /// It's purpose is to generate an ItemIDList for the Parent of a
        /// Special Folder which can then be processed with DesktopBase.BindToObject,
        /// yeilding a Folder for the parent of the Special Folder
        /// It also creates and passes back a RELATIVE pidl for this item
        /// </summary>
        /// <param name="pidl">A pointer to a well formed ItemIDList. The PIDL to trim</param>
        /// <param name="relPidl">BYREF IntPtr which will point to a new relative pidl
        ///        containing the contents of the last ItemID in the ItemIDList
        ///        terminated by the required 2 nulls.</param>
        /// <returns> an ItemIDList with the last element removed.</returns>
        /// <remarks>Caller must Free BOTH the returned, Trimmed PIDL and the 
        /// returned relPidl.
        /// </remarks>
        public static PidlSplitResult Split(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero)
                throw new ArgumentNullException(nameof(pidl));

            const int cbSize = sizeof(ushort);

            int offset = 0;
            int lastItemOffset = -1;
            int lastItemSize = 0;

            while (true)
            {
                ushort cb = unchecked((ushort)Marshal.ReadInt16(pidl, offset));

                if (cb == 0)
                    break;

                if (cb < cbSize)
                    throw new InvalidOperationException("Invalid PIDL: SHITEMID.cb is smaller than sizeof(USHORT).");

                lastItemOffset = offset;
                lastItemSize = cb;
                offset += cb;
            }

            // offset currently points to the terminating USHORT cb == 0.
            int originalSize = offset + cbSize;

            if (lastItemOffset < 0)
                throw new InvalidOperationException("PIDL has no elements to remove.");

            // The shortened PIDL consists of everything before the last item,
            // plus a terminating USHORT cb == 0.
            int shortenedSize = lastItemOffset + cbSize;

            // The last element PIDL consists of the final SHITEMID,
            // plus a terminating USHORT cb == 0.
            int lastElementPidlSize = lastItemSize + cbSize;

            IntPtr shortenedPidl = IntPtr.Zero;
            IntPtr lastElementPidl = IntPtr.Zero;

            try
            {
                shortenedPidl = Marshal.AllocCoTaskMem(shortenedSize);
                lastElementPidl = Marshal.AllocCoTaskMem(lastElementPidlSize);

                // Copy everything before the last SHITEMID into the shortened PIDL.
                if (lastItemOffset > 0)
                {
                    byte[] prefixBytes = new byte[lastItemOffset];
                    Marshal.Copy(pidl, prefixBytes, 0, lastItemOffset);
                    Marshal.Copy(prefixBytes, 0, shortenedPidl, lastItemOffset);
                }

                // Write terminating USHORT cb == 0.
                Marshal.WriteInt16(shortenedPidl, lastItemOffset, 0);

                // Copy the last SHITEMID into its own PIDL.
                byte[] lastItemBytes = new byte[lastItemSize];
                Marshal.Copy(IntPtr.Add(pidl, lastItemOffset), lastItemBytes, 0, lastItemSize);
                Marshal.Copy(lastItemBytes, 0, lastElementPidl, lastItemSize);

                // Write terminating USHORT cb == 0.
                Marshal.WriteInt16(lastElementPidl, lastItemSize, 0);

                return new PidlSplitResult(shortenedPidl, lastElementPidl);
            }
            catch
            {
                if (shortenedPidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(shortenedPidl);

                if (lastElementPidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(lastElementPidl);

                throw;
            }
        }


        /// <summary>ILFindLastID -- returns a pointer to the last ITEMID in a valid
        /// ITEMIDLIST. Returned pointer SHOULD NOT be released since it
        /// points to place within the original PIDL</summary>
        /// <returns>IntPtr pointing to last ITEMID in ITEMIDLIST structure,
        /// Returns IntPtr.Zero if given a null pointer.
        /// If given a pointer to the Desktop, will return same pointer.</returns>
        /// <remarks>Uses the API ILFindLastID function if Win2k or above, otherwise
        /// computes the same thing.</remarks>
        public static IntPtr ILFindLastID(IntPtr pidl)
        {
            return ShellAPI.ILFindLastID(pidl);
            //if (WinSDK.Win2KOrAbove)
            //{
            //    return ShellAPI.ILFindLastID(pidl);
            //}
            //else
            //{
            //    int prev = 0;
            //    int i = 0;
            //    int b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
            //    while (b > 0)
            //    {
            //        prev = i;
            //        i += b;
            //        b = Marshal.ReadByte(pidl, i) + Marshal.ReadByte(pidl, i + 1) * 256;
            //    }
            //    return new IntPtr(pidl.ToInt64() + prev);
            //}  // 6/8/2012 - ToInt64 works on both 32 & 64 bit systems (though code is never executed on 64 bit systems)
        }

        /// <summary>It is impossible to validate a PIDL completely since its contents
        /// are arbitrarily defined by the creating Shell Namespace.  However, it
        /// is possible to validate the structure of a PIDL.</summary>
        /// <returns>True if input Byte() contains a valid PIDL structure, False Otherwise</returns>
        public static bool IsValid(byte[] b)
        {
            bool IsValidPidlRet = default;
            IsValidPidlRet = false;     // assume failure
            int bMax = b.Length - 1;   // max value that index can have
            if (bMax < 1)
                return IsValidPidlRet; // min size is 2 bytes
            int cb = b[0] + b[1] * 256;
            int indx = 0;
            while (cb > 0)
            {
                if (indx + cb + 1 > bMax)
                    return IsValidPidlRet; // an error
                indx += cb;
                cb = b[indx] + b[indx + 1] * 256;
            }
            // on fall thru, it is ok as far as we can check
            IsValidPidlRet = true;
            return IsValidPidlRet;
        }


        /// <summary>IsAncestorOf tests if Pidl1 is an ancestor of Pidl2.</summary>
        /// <param name="Pidl1">Relative or Absolute PIDL of potential ancestor.</param>
        /// <param name="Pidl2">Absolute PIDL of potential descendant.</param>
        /// <param name="ImmediateOnly">If True, returns True only if Pidl1 is the Immediate Ancestor of Pidl2.</param>
        /// <returns>True if Pidl1 is an ancestor of Pidl2, False otherwise.</returns>
        public static bool IsAncestorOf(IntPtr Pidl1, IntPtr Pidl2, bool ImmediateOnly = false)
        {
            if (Pidl1.Equals(IntPtr.Zero) || Pidl2.Equals(IntPtr.Zero)) return false;
            return ILIsParent(Pidl1, Pidl2, ImmediateOnly);
        }

        /// <summary>IsAncestorOf tests if Item1 is an ancestor of Item2.</summary>
        /// <param name="Item1">Potential ancestor CShellItem.</param>
        /// <param name="Item2">Potential descendant CShellItem.</param>
        /// <param name="ImmediateOnly">If True, returns True only if Item1 is the Immediate Ancestor of Item2.</param>
        /// <returns>True if Item1 is an ancestor of Item2, False otherwise.</returns>
        public static bool IsAncestorOf(CShellItem Item1, CShellItem Item2, bool ImmediateOnly = false)
        {
            return IsAncestorOf(Item1.PIDL, Item2.PIDL, ImmediateOnly);
        }


        /// <summary>
        /// Returns the filesystem path for a PIDL, or null if it isn't a filesystem item.
        /// </summary>
        public static string? GetFileSystemPath(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) throw new ArgumentNullException(nameof(pidl));

            // Preferred modern call — works for long paths too.
            IntPtr psz = IntPtr.Zero;
            try
            {
                if (SHGetNameFromIDList(pidl, SIGDN.FILESYSPATH, out psz) >= 0 && psz != IntPtr.Zero)
                    return Marshal.PtrToStringUni(psz);
            }
            finally
            {
                if (psz != IntPtr.Zero) Marshal.FreeCoTaskMem(psz);
            }

            // Fallback: classic API (MAX_PATH limit).
            char[] buffer = new char[WinSDK.MAX_PATH];
            if (!SHGetPathFromIDListW(pidl, buffer))
                return null;

            // Trim at the first null terminator.
            int len = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, len < 0 ? buffer.Length : len);
        }

        /// <summary>
        /// Returns a parsing name path even for virtual items (e.g. "::{GUID}\..."), useful when
        /// the PIDL isn't a real filesystem object.
        /// </summary>
        public static string? GetParsingPath(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) throw new ArgumentNullException(nameof(pidl));

            IntPtr psz = IntPtr.Zero;
            try
            {
                if (SHGetNameFromIDList(pidl, SIGDN.DESKTOPABSOLUTEPARSING, out psz) >= 0 && psz != IntPtr.Zero)
                    return Marshal.PtrToStringUni(psz);
                return null;
            }
            finally
            {
                if (psz != IntPtr.Zero) Marshal.FreeCoTaskMem(psz);
            }
        }

        /// <summary>
        /// Converts a PIDL to a readable string.
        /// Tries parsing name first, then falls back to normal display name.
        /// Returns null if conversion fails.
        /// </summary>
        public static string? ToString(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero)
                throw new ArgumentNullException(nameof(pidl));

            // 1) Try full parsing name (often path-like).
            string? s = TryGetName(pidl, SIGDN.DESKTOPABSOLUTEPARSING);
            if (!string.IsNullOrEmpty(s))
                return s;

            // 2) Fallback to friendly display name.
            return TryGetName(pidl, SIGDN.NORMALDISPLAY);
        }

        /// <summary>
        /// Resolves a shell namespace GUID path to its corresponding file system path, if available.
        /// </summary>
        /// <remarks>This method attempts to convert a shell namespace GUID (such as those used for known
        /// folders) to a file system path. If the GUID refers to a virtual folder or a location without a file system
        /// path, the method returns null. The caller should check the return value before using it.</remarks>
        /// <param name="guidPath">The shell namespace GUID path to resolve. This should be a string in the format '::{GUID}' representing a
        /// known folder or shell object. Cannot be null or empty.</param>
        /// <returns>The file system path corresponding to the specified shell GUID path, or null if the path cannot be resolved
        /// or does not represent a file system location.</returns>
        public static string? ResolveShellGUID(string guidPath)
        {
            IntPtr pidl = IntPtr.Zero;
            uint sfgao;

            int hr = ShellAPI.SHParseDisplayName(guidPath, IntPtr.Zero, out pidl, 0, out sfgao);
            if (hr != 0)
            {
                Console.WriteLine($"SHParseDisplayName failed: 0x{hr:X8}");
                if (hr == -2147024809)
                    Console.WriteLine($"reason: invalid argument");

                return null;
            }

            if (pidl == IntPtr.Zero)
            {
                Console.WriteLine("pidl is null");
                return null;
            }

            try
            {
                var sb = new StringBuilder(WinSDK.MAX_PATH);
                if (ShellAPI.SHGetPathFromIDList(pidl, sb))
                    return sb.ToString();
                else
                    Console.WriteLine("SHGetPathFromIDList failed - may be a virtual folder");
            }
            finally
            {
                WinSDK.CoTaskMemFree(pidl);
            }

            return null;
        }


        /// <summary>
        /// Takes input requiring special values like Environment.SpecialFolder.DesktopDirectory which equals "::{00021400-0000-0000-C000-000000000046}".
        /// </summary>
        /// <param name="parsingName">The text name for a Shell location</param>
        /// <returns></returns>
        public static string? GetFileSystemPathFromShellParsingName(string parsingName)
        {
            if (string.IsNullOrWhiteSpace(parsingName))
                return null;

            // 1) Fast path for direct filesystem paths
            if (Path.IsPathRooted(parsingName))
            {
                if (File.Exists(parsingName) || Directory.Exists(parsingName))
                    return parsingName;
            }

            // 2) Pre-filter virtual shell namespace CLSID forms
            //    (prevents calling SHCreateItemFromParsingName for obvious non-filesystem inputs)
            if (IsDefinitelyVirtualNamespace(parsingName))
                return null;

            IShellItem? item = null;
            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                ShellAPI.SHCreateItemFromParsingName(
                    parsingName,
                    IntPtr.Zero,
                    ShellAPI.IID_IShellItem,
                    out item);

                // Important: only request FILESYSPATH for filesystem-backed items.
                uint attrs;
                item.GetAttributes((uint)SFGAOF.FILESYSTEM, out attrs);
                if ((attrs & (uint)SFGAOF.FILESYSTEM) == 0)
                    return null;

                item.GetDisplayName(SIGDN.FILESYSPATH, out pathPtr);
                return pathPtr == IntPtr.Zero ? null : Marshal.PtrToStringUni(pathPtr);
            }
            catch (COMException)
            {
                // Includes virtual folders and other non-filesystem shell items
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EXCEPTION! parsingName = {parsingName} : {ex}");
                return null;
            }
            finally
            {
                //if (pathPtr != IntPtr.Zero) WinSDK.CoTaskMemFree(pathPtr);
                if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
                if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            }
        }

        /// <summary>
        /// Returns true if the PIDL represents the root of the Shell namespace (Desktop root).
        /// </summary>
        /// <param name="pidl">Pointer to an absolute PIDL.</param>
        /// <returns>
        /// True if the PIDL is root (empty list terminator as first SHITEMID); otherwise false.
        /// </returns>
        public static bool IsShellNamespaceRoot(nint pidl)
        {
            // Some code paths treat null like "desktop/root"; adjust if you prefer strict behavior.
            if (pidl == 0)
                return true;

            // ITEMIDLIST starts with SHITEMID.cb (USHORT).
            // Root PIDL is "empty": first cb == 0.
            short cb = Marshal.ReadInt16(pidl);
            return cb == 0;
        }

        private static bool IsDefinitelyVirtualNamespace(string parsingName)
        {
            // Common virtual forms
            if (parsingName.StartsWith("::", StringComparison.Ordinal))
                return true;

            if (parsingName.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        #region    DumpPidl Routines
        /// <summary>
        /// Dumps, to the Debug output, the contents of the mem block pointed to by
        /// a PIDL. Depends on the internal structure of a PIDL
        /// </summary>
        /// <param name="pidl">The IntPtr(a PIDL) pointing to the block to dump</param>
        public static void Dump(IntPtr pidl)
        {
            int cb = ItemIDListSize(pidl);
            Debug.WriteLine("PIDL " + pidl.ToString() + " contains " + cb + " bytes");
            if (cb > 0)
            {
                var b = new byte[cb + 1 + 1];
                Marshal.Copy(pidl, b, 0, cb + 1);
                int pidlCnt = 1;
                int i = b[0] + b[1] * 256;
                int curB = 0;
                while (i > 0)
                {
                    Debug.Write("ItemID #" + pidlCnt + " Length = " + i);
                    DumpHex(b, curB, curB + i - 1);
                    pidlCnt += 1;
                    curB += i;
                    i = b[curB] + b[curB + 1] * 256;
                }
            }
        }

        /// <summary>Dump a portion or all of a Byte Array to Debug output</summary>
        /// <param name = "b">A single dimension Byte Array</param>
        /// <param name = "sPos">Optional start index of area to dump (default = 0)</param>
        /// <param name = "epos">Optional last index position to dump (default = end of array)</param>
        public static void DumpHex(byte[] b, int sPos = 0, int ePos = 0)
        {
            if (ePos == 0)
                ePos = b.Length - 1;
            int j;
            int curB = sPos;
            string sTmp;
            char ch;
            var SBH = new StringBuilder();
            var SBT = new StringBuilder();
            var loopTo = ePos - sPos;
            for (j = 0; j <= loopTo; j++)
            {
                if (j % 16 == 0)
                {
                    Debug.WriteLine(SBH.ToString() + SBT.ToString());
                    SBH = new StringBuilder();
                    SBT = new StringBuilder("          ");
                    SBH.Append(HexNum(j + sPos, 4) + "). ");
                }
                if (b[curB] < 16)
                {
                    sTmp = b[curB].ToString("X2");
                }
                else
                {
                    sTmp = b[curB].ToString("X");
                }
                SBH.Append(sTmp);
                SBH.Append(" ");
                ch = (char)b[curB];
                if (char.IsControl(ch))
                {
                    SBT.Append(".");
                }
                else
                {
                    SBT.Append(ch);
                }
                curB += 1;
            }

            int fill = j % 16;
            if (fill != 0)
            {
                SBH.Append(' ', 48 - 3 * (j % 16));
            }
            Debug.WriteLine(SBH.ToString() + SBT.ToString());
        }

        /// <summary>
        /// Formats an Integer into a String representation of the Hexidecimal representation of that number with
        /// enough leading zero Chars to fill nrChars number of characters.
        /// </summary>
        /// <param name="num">The Integer to Format</param>
        /// <param name="nrChrs">The desired size of the returned String</param>
        /// <returns>A String with the Hex representation of the Integer parameter</returns>
        /// <remarks></remarks>
        public static string HexNum(int num, int nrChrs)
        {
            string h = num.ToString("X");
            var SB = new StringBuilder();
            int i;
            var loopTo = nrChrs - h.Length;
            for (i = 1; i <= loopTo; i++)
                SB.Append("0");
            SB.Append(h);
            return SB.ToString();
        }

        /// <summary>
        /// Fast hash tuned for PIDLs that share the same parent path.
        /// Non-last segments are sampled; last segment is hashed fully.
        /// </summary>
        /// <param name="pidl"></param>
        /// <returns></returns>

        public static unsafe uint HashPidlFastLastFull(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero)
                return 0;

            const uint offset = 2166136261u; // FNV-1a offset basis
            const uint prime = 16777619u;   // FNV-1a prime

            uint h = offset;

            byte* start = (byte*)pidl;
            byte* p = start;

            byte* lastItem = null;
            ushort lastCb = 0;

            uint itemCount = 0;
            uint totalBytes = 0;

            // Pass 1: hash structure + sampled bytes for each segment,
            // and remember where the last segment is.
            while (true)
            {
                ushort cb = *(ushort*)p;

                if (cb == 0)
                {
                    totalBytes += 2; // terminator USHORT
                    h ^= 0xFFFFu;
                    h *= prime;
                    break;
                }

                if (cb < 2)
                    return 0; // invalid PIDL guard

                itemCount++;
                totalBytes += cb;

                // Mix length for every segment
                h ^= cb;
                h *= prime;

                int payloadLen = cb - 2;
                byte* d = p + 2;

                // Fast sampling for this segment
                if (payloadLen > 0)
                {
                    h ^= d[0]; h *= prime;                   // first
                    h ^= d[payloadLen - 1]; h *= prime;     // last
                }
                if (payloadLen > 2)
                {
                    h ^= d[payloadLen >> 1]; h *= prime;    // middle
                }

                // Track last real segment
                lastItem = p;
                lastCb = cb;

                p += cb;
            }

            // Pass 2: hash FULL payload of last segment
            if (lastItem != null && lastCb > 2)
            {
                byte* d = lastItem + 2;
                int payloadLen = lastCb - 2;

                // Include marker so "full-last" contribution is explicit
                h ^= 0xA5A5A5A5u;
                h *= prime;

                for (int i = 0; i < payloadLen; i++)
                {
                    h ^= d[i];
                    h *= prime;
                }
            }

            // Final mixing
            h ^= itemCount + 0x9E3779B9u;
            h *= prime;
            h ^= totalBytes + 0x85EBCA6Bu;
            h *= prime;
            h ^= (h >> 16);

            return h;
        }

        #endregion


        #endregion

        #region        Public instance methods

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

        /// <summary>
        /// Not currently used. Compares two PIDLs Relative to the instance Folder using the folder.CompareIDs API call.
        /// </summary>
        /// <param name="RelPidl1">First Relative PIDL to compare.</param>
        /// <param name="RelPidl2">Second Relative PIDL to compare.</param>
        /// <returns>True if Equal, False otherwise.</returns>
        /// <remarks></remarks>
        public bool AreEqual(IShellFolder folder, IntPtr RelPidl1, IntPtr RelPidl2)
        {
            bool PidlsEqualRet = default;
            if (folder is null)
                return IsEqual(RelPidl1, RelPidl2);
            PidlsEqualRet = false;            // assume not equal
            uint lParam = (uint)SHCIDS.CANONICALONLY;
            int H;
            H = folder.CompareIDs(lParam, RelPidl1, RelPidl2);
            if (H >= 0)
            {
                int Code = H & 0x7777;
                if (Code == 0)
                    return true;
            }
            else
            {
                return IsEqual(RelPidl1, RelPidl2);
            }

            return PidlsEqualRet;
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Get Size in bytes of the first (possibly only)
        /// SHItem in an ID list.  Note: the full size of
        ///   the item is the sum of the sizes of all SHItems
        ///   in the list!!
        /// </summary>
        /// <param name="pidl">A pointer to a PIDL.</param>
        private static int ItemIDSize(IntPtr pidl)
        {
            if (!pidl.Equals(IntPtr.Zero))
            {
                var b = new byte[2];
                Marshal.Copy(pidl, b, 0, 2);
                return b[1] * 256 + b[0];
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Given a PIDL as IntPtr, Allocate memory for and return a Clone of the input PIDL.
        /// </summary>
        /// <param name="pidl">A PIDL to be Cloned</param>
        /// <returns>A Clone of the input PIDL</returns>
        /// <remarks>The Clone must be Released when no longer needed by calling PIDLFree</remarks>
        internal static IntPtr PIDLClone(IntPtr pidl)
        {
            IntPtr PIDLCloneRet = default;
            var cb = (int)ItemIDListSize(pidl);
            var b = new byte[cb + 1 + 1];
            Marshal.Copy(pidl, b, 0, cb); // not including terminating nulnul
            b[cb] = 0;
            b[cb + 1] = 0; // force to nulnul
            PIDLCloneRet = Marshal.AllocCoTaskMem(cb + 2);
            Marshal.Copy(b, 0, PIDLCloneRet, cb + 2);
            return PIDLCloneRet;
        }

        /// <summary>
        /// Frees a PIDL, releasing its' allocated memory
        /// </summary>
        /// <param name="pidl">The PIDL to be Freed</param>
        /// <remarks></remarks>
        internal static void PIDLFree(IntPtr pidl)
        {
            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        /*
        // TODO: Test IsReallyEqual on Fat32.
        /// <summary>
        /// IsReallyEqual compares Pidls using the IsEqual routine. If IsEqual declares them Equal, IsReallyEqual
        /// checks the Last (or relative) Pidls using a byte by byte comparison. This is necessary because new file
        /// versions created by File->Save will compare Equal in IsEqual, when we really want to know that a new version
        /// of a file has been created. Fortunately, the relative Pidl of a new version will differ in a few bytes from
        /// the relative Pidl of the previous version.
        /// This Function is no longer used by WindowsApiLib.
        /// </summary>
        /// <param name="Pidl1">IntPtr pointing to an ItemIDList.</param>
        /// <param name="Pidl2">IntPtr pointing to an ItemIDList.</param>
        /// <returns>True is completely equal, False otherwise.</returns>
        /// <remarks>At this point, this has been tested on NTFS file systems only.</remarks>
        internal static bool IsReallyEqual(IntPtr Pidl1, IntPtr Pidl2)
        {
            IsReallyEqual = IsEqual(Pidl1, Pidl2)
             If IsReallyEqual AndAlso Win2KOrAbove Then           'IsEqual says they are -- if Win2KOrAbove, then check the last ItemID
             IsReallyEqual = AreBytesEqual(ILFindLastID(Pidl1), ILFindLastID(Pidl2))
             'If Not IsReallyEqual Then
             '    Debug.WriteLine("IsReallyEqual found mismatch")
             '    DumpPidl(Pidl1)
             '    DumpPidl(Pidl2)
             'End If
             End If
        }
        */

        private static string? TryGetName(IntPtr pidl, SIGDN sigdn)
        {
            IntPtr psz = IntPtr.Zero;
            try
            {
                int hr = ShellAPI.SHGetNameFromIDList(pidl, sigdn, out psz);
                if (hr < 0 || psz == IntPtr.Zero) // FAILED(hr)
                    return null;

                return Marshal.PtrToStringUni(psz);
            }
            finally
            {
                if (psz != IntPtr.Zero)
                {
                    // SHGetNameFromIDList allocates with CoTaskMemAlloc
                    Marshal.FreeCoTaskMem(psz);
                }
            }
        }



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

    /// <summary>
    /// todo: it may be better to turn this into a class so that we can put Marshal.FreeCoTaskMem on the members
    /// </summary>
    public readonly struct PidlSplitResult
    {
        public PidlSplitResult(IntPtr shortenedPidl, IntPtr lastElementPidl)
        {
            ParentPidl = shortenedPidl;
            ChildPidl = lastElementPidl;
        }

        public IntPtr ParentPidl { get; }
        public IntPtr ChildPidl { get; }
    }
}
