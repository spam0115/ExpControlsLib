using System.Runtime.InteropServices;
using System.Text;
using WindowsApiLib.Shell;

namespace WindowsApiLib
{
    public static class ShellPidl //todo: merge with cpidl
    {

        /// <summary>
        /// Converts a PIDL to a readable string.
        /// Tries parsing name first, then falls back to normal display name.
        /// Returns null if conversion fails.
        /// </summary>
        public static string? PidlToString(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero)
                throw new ArgumentNullException(nameof(pidl));

            // 1) Try full parsing name (often path-like).
            string? s = TryGetName(pidl, SIGDN.NORMALDISPLAY);
            if (!string.IsNullOrEmpty(s))
                return s;
            // 2) Fallback to friendly display name.
            return TryGetName(pidl, SIGDN.NORMALDISPLAY);
        }

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
        /// Takes input requiring special values like Environment.SpecialFolder.DesktopDirectory which equals 0x0010.
        /// </summary>
        /// <param name="parsingName">special values like Environment.SpecialFolder.DesktopDirectory which equals 0x0010</param>
        /// <returns></returns>
        public static string? TryGetFileSystemPathFromShellParsingName(string parsingName)
        {
            try
            {
                var IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"); //for some unknown reason the class level "static readonly Guid IID_IShellItem" is zero.

                ShellAPI.SHCreateItemFromParsingName(
                    parsingName,
                    IntPtr.Zero,
                    IID_IShellItem,
                    out IShellItem item);

                item.GetDisplayName(SIGDN.FILESYSPATH, out IntPtr pathPtr);

                try
                {
                    return Marshal.PtrToStringUni(pathPtr);
                }
                finally
                {
                    if (pathPtr != IntPtr.Zero)
                        WinSDK.CoTaskMemFree(pathPtr);
                }
            }
            catch (COMException)
            {
                return null;
            }
        }

    }
}
