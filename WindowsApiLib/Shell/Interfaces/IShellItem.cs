using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        int BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppv);

        int GetParent(out IShellItem ppsi);

        int GetDisplayName(
            SIGDN sigdnName,
            out IntPtr ppszName);

        int GetAttributes(
            uint sfgaoMask,
            out uint psfgaoAttribs);

        /// <summary>
        /// Compares this IShellItem against another IShellItem to determine
        /// their relative order or logical identity. Unlike
        /// IShellFolder.CompareIDs(), this method accepts any two IShellItem
        /// objects regardless of their location in the shell namespace — there
        /// is no requirement to manually resolve parent folders or extract
        /// child PIDLs first.
        /// </summary>
        /// <param name="psi">
        /// A reference to the IShellItem to compare against this item.
        /// Must not be null. The item can be located anywhere in the shell
        /// namespace — it does not need to share the same parent folder as
        /// this item. Internally, Windows will resolve the correct parent
        /// folder and delegate to IShellFolder.CompareIDs() automatically.
        /// </param>
        /// <param name="hint">
        /// A value from the SICHINTF enumeration that controls how the
        /// comparison is performed. The most commonly used values are:
        ///
        ///   - SICHINT_DISPLAY (0x00000000):
        ///       Compare by display name only (the name shown to the user).
        ///       Two items with the same display name are considered equal
        ///       even if they are different items. Not recommended for
        ///       identity checks.
        ///
        ///   - SICHINT_CANONICAL (0x10000000):
        ///       Perform a canonical comparison that tests for logical
        ///       identity. This is the recommended flag when you want to
        ///       know if two IShellItems point to the same underlying item,
        ///       regardless of how their PIDLs were constructed or whether
        ///       their binary representations differ.
        ///       Returns S_OK (0) if the items are logically identical.
        ///
        ///   - SICHINT_ALLFIELDS (0x80000000):
        ///       Perform a strict binary comparison across all fields.
        ///       Both items must match exactly in every field. Equivalent
        ///       in behavior to a raw memcmp on the underlying PIDLs.
        ///       Use this only when you need exact binary equality.
        ///
        ///   - SICHINT_TEST_FILESYSPATH_IF_NOT_EQUAL (0x20000000):
        ///       If the canonical comparison determines the items are not
        ///       equal, this flag additionally compares their file system
        ///       paths as a fallback. Useful when dealing with items that
        ///       may have inconsistent PIDLs but share the same file path
        ///       (e.g., mapped drives vs. UNC paths pointing to the same
        ///       location).
        /// </param>
        /// <param name="piOrder">
        /// When this method returns, contains an integer indicating the
        /// relative sort order of the two items:
        ///       0  : This item is equivalent to psi.
        ///      &lt;0  : This item comes before psi in the sort order.
        ///      &gt;0  : This item comes after psi in the sort order.
        /// This value is only meaningful if the method returns S_OK or S_FALSE.
        /// Do not use this value if the method returns a failure HRESULT.
        /// </param>
        /// <returns>
        /// Returns an HRESULT indicating the result of the comparison:
        ///   - S_OK     (0x00000000): The two items are equal (when using
        ///                            SICHINT_CANONICAL or SICHINT_ALLFIELDS).
        ///   - S_FALSE  (0x00000001): The two items are not equal. Check
        ///                            piOrder for their relative sort order.
        ///   - E_INVALIDARG           One or both items are invalid or null.
        ///   - Other failure HRESULT: The comparison could not be performed
        ///                            (e.g., the shell namespace could not
        ///                            be accessed).
        ///
        /// IMPORTANT: For equality checks, test the HRESULT return value
        /// directly — do NOT use piOrder alone:
        ///     bool equal = (item1.Compare(item2, SICHINT_CANONICAL, out int order) == 0);
        /// Unlike IShellFolder.CompareIDs(), there is no need to mask the
        /// low 16 bits — the full HRESULT is meaningful here.
        /// </returns>
        /// <remarks>
        /// This is the recommended high-level API for comparing shell items
        /// in modern Windows applications (.NET / Win32 Vista+). It is
        /// preferable to IShellFolder.CompareIDs() for most use cases because:
        ///
        ///   1. It accepts absolute IShellItems — no need to manually resolve
        ///      parent folders or extract child PIDLs with ILFindLastID().
        ///   2. It handles cross-folder comparisons automatically.
        ///   3. The return value semantics are simpler — no low-16-bit masking
        ///      required, unlike IShellFolder.CompareIDs().
        ///
        /// Common mistakes:
        ///   1. Using SICHINT_DISPLAY for identity checks — two different items
        ///      can share the same display name, so this will produce false
        ///      positives. Always use SICHINT_CANONICAL for identity checks.
        ///   2. Checking piOrder instead of the HRESULT return value for
        ///      equality — piOrder is a sort hint, not a definitive equality
        ///      indicator on its own.
        ///   3. Forgetting to call Marshal.ReleaseComObject() on IShellItem
        ///      instances when finished, causing COM reference count leaks.
        /// </remarks>
        int Compare(
            IShellItem psi,
            uint hint,
            out int piOrder);
    }
}
