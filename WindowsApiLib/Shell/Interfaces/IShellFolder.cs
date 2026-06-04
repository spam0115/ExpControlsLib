using System.Runtime.InteropServices;
using static System.Windows.Forms.Design.AxImporter;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static WindowsApiLib.Shell.ShellAPI;


namespace WindowsApiLib.Shell
{
    [ComImport()]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    public interface IShellFolder
    {
        /// <summary>
        /// Converts a display name (such as a file system path or Shell namespace path)
        /// into an item identifier list (PIDL) relative to this folder.
        /// </summary>
        /// <param name="hwnd">
        /// Optional owner window handle used for any UI the Shell might display during parsing.
        /// Pass <see cref="IntPtr.Zero"/> if no UI is needed.
        /// </param>
        /// <param name="pbc">
        /// Optional bind context that provides parameters used during parsing.
        /// Can be <see langword="null"/>.
        /// </param>
        /// <param name="pszDisplayName">
        /// The null-terminated display name to parse.
        /// </param>
        /// <param name="pchEaten">
        /// On return, receives the number of characters consumed from <paramref name="pszDisplayName"/>.
        /// </param>
        /// <param name="ppidl">
        /// On success, receives a pointer to the resulting relative PIDL.
        /// The caller is responsible for freeing this PIDL with the Shell allocator (for example, <c>CoTaskMemFree</c>).
        /// </param>
        /// <param name="pdwAttributes">
        /// Input/output SFGAO flags. On input, specifies requested attributes; on output, receives the attributes for the parsed item.
        /// Can be <see langword="null"/> if attributes are not required.
        /// </param>
        /// <returns>
        /// Returns an HRESULT:
        /// <list type="bullet">
        /// <item><description><c>S_OK</c> if parsing succeeds.</description></item>
        /// <item><description>An error HRESULT (for example <c>E_INVALIDARG</c> or Shell-specific failure codes) if parsing fails.</description></item>
        /// </list>
        /// </returns>
        [PreserveSig()]
        int ParseDisplayName(int hwndOwner, IntPtr pbcReserved, [MarshalAs(UnmanagedType.LPWStr)] string lpszDisplayName, ref int pchEaten, ref IntPtr ppidl, ref int pdwAttributes);

        [PreserveSig()]
        int EnumObjects(int hwndOwner, [MarshalAs(UnmanagedType.U4)] SHCONTF grfFlags, ref IEnumIDList ppenumIDList);

        [PreserveSig()]
        int BindToObject(IntPtr pidl, IntPtr pbcReserved, ref Guid riid, ref IntPtr ppvOut);

        // IShellFolder) As Integer

        [PreserveSig()]
        int BindToStorage(IntPtr pidl, IntPtr pbcReserved, ref Guid riid, IntPtr ppvObj);

        /// <summary>
        /// Determines the relative order of two file objects or folders, given
        /// their item identifier lists (PIDLs). The comparison is performed
        /// relative to this shell folder (i.e., the PIDLs must be children of
        /// this folder, not absolute PIDLs).
        /// </summary>
        /// <param name="lParam">
        /// A value that specifies how the comparison should be performed.
        /// The lower 16 bits (LOWORD) contain a folder-specific value that
        /// defines the sorting rule (column index). Pass 0 for the default sort.
        /// The upper 16 bits (HIWORD) contain flags that modify the comparison:
        ///   - SHCIDS_CANONICALONLY (0x10000000): Perform a canonical comparison
        ///     only. Ignores display-name differences such as casing. Use this
        ///     flag to test for logical identity (i.e., do both PIDLs point to
        ///     the same item?). This is the recommended flag for equality checks.
        ///   - SHCIDS_ALLFIELDS (0x80000000): Perform a strict comparison using
        ///     all available fields. Two PIDLs are only equal if every field
        ///     matches exactly. Similar in behavior to a raw memcmp.
        /// </param>
        /// <param name="pidl1">
        /// A pointer to the first PIDL (PCUITEMID_CHILD) to compare.
        /// This must be a single-level child PIDL relative to this folder —
        /// NOT an absolute PIDL. Use ILFindLastID() to extract the child
        /// segment from an absolute PIDL before passing it here.
        /// </param>
        /// <param name="pidl2">
        /// A pointer to the second PIDL (PCUITEMID_CHILD) to compare.
        /// Same constraints as pidl1 — must be a single-level child PIDL
        /// relative to this folder.
        /// </param>
        /// <returns>
        /// Returns an HRESULT where the meaning of the value is as follows:
        ///   - The HRESULT SUCCEEDED (i.e., >= 0) if the comparison was performed.
        ///   - The low 16 bits (LOWORD / short) contain the comparison result:
        ///       0  : pidl1 == pidl2 (the two items are equivalent)
        ///      &lt;0  : pidl1 comes before pidl2 in the sort order
        ///      &gt;0  : pidl1 comes after pidl2 in the sort order
        ///   - E_INVALIDARG (0x80070057) if either PIDL is invalid, NULL,
        ///     or is an absolute PIDL rather than a child PIDL.
        /// IMPORTANT: Do NOT treat the full return value as a simple int.
        /// Always extract the low 16 bits using:
        ///     short result = unchecked((short)(hr &amp; 0xFFFF));
        /// </returns>
        /// <remarks>
        /// This method is the core Shell comparison API and is what
        /// SHCompareIDList() calls internally.
        ///
        /// Common mistakes:
        ///   1. Passing absolute PIDLs — always pass child PIDLs only.
        ///      Use ILFindLastID() to get the last segment of an absolute PIDL.
        ///   2. Comparing the full HRESULT return value directly to 0 instead
        ///      of extracting the low 16 bits first.
        ///   3. Calling this on the Desktop IShellFolder with non-desktop-child
        ///      PIDLs — bind to the correct parent folder first using
        ///      IShellFolder.BindToObject().
        ///
        /// For comparing absolute PIDLs without manual parent resolution,
        /// prefer IShellItem.Compare() with SICHINT_CANONICAL instead.
        /// </remarks>
        [PreserveSig()]
        int CompareIDs(uint lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig()]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, ref IntPtr ppvOut);


        // IUnknown) As Integer

        [PreserveSig()]
        int GetAttributesOf(int cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref SFGAO rgfInOut);


        /// <summary>
        /// Asks a folder to give you a COM interface for one or more items in that folder, usually for UI-related actions.
        /// Example: You pass:
        ///     a parent window handle(HWND, optional),
        ///     an array of child PIDLs(items relative to that folder),
        ///     the interface ID you want(riid).
        /// </summary>
        /// <param name="hwndOwner">Owner window handle for any UI the returned object might show (context menu, dialogs, etc.).
        ///     Can often be NULL if you have no UI owner.</param>
        /// <param name="cidl">Number of item PIDLs in apidl.</param>
        /// <param name="apidl">Array of child PIDLs (items relative to this folder).  These are not absolute desktop PIDLs.</param>
        /// <param name="riid">The interface ID you want back (for example IID_IContextMenu, IID_IDataObject, etc.).</param>
        /// <param name="rgfReserved">Reserved; should be NULL (or ignored).  Historically present for extensibility.</param>
        /// <param name="ppvOut">The output COM interface of the object you requested</param>
        /// <returns>S_OK (0) on success.  Failure codes like: E_NOINTERFACE(requested riid not supported), 
        ///     E_INVALIDARG(bad args), other COM/Shell error HRESULT
        /// </returns>
        [PreserveSig()]
        int GetUIObjectOf(IntPtr hwndOwner, UInt32 cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppvOut);


        // ByRef ppvOut As IUnknown) As Integer
        // ByRef ppvOut As IDropTarget) As Integer

        [PreserveSig()]
        int GetDisplayNameOf(IntPtr pidl, [MarshalAs(UnmanagedType.U4)] SHGDN uFlags, IntPtr lpName);

        [PreserveSig()]
        int SetNameOf(int hwndOwner, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string lpszName, [MarshalAs(UnmanagedType.U4)] SHGDN uFlags, ref IntPtr ppidlOut);

    }
}