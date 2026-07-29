using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// ShellAPI contains many declarations of Shell API functions, Constants, Structures, Enums used by WindowsApiLib.
    /// Certain other declarations of Shell API components are declared outside of this Class, typically in those Classes that
    /// are the only place that such declarations are needed.
    /// </summary>
    /// <remarks></remarks>
    public partial class ShellAPI
    {

        #region    Constants
        public const int FILE_ATTRIBUTE_READONLY = 0x1;
        public const int FILE_ATTRIBUTE_HIDDEN = 0x2;
        public const int FILE_ATTRIBUTE_SYSTEM = 0x4;
        public const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const int FILE_ATTRIBUTE_ARCHIVE = 0x20;
        public const int FILE_ATTRIBUTE_NORMAL = 0x80;
        public const int FILE_ATTRIBUTE_TEMPORARY = 0x100;
        public const int FILE_ATTRIBUTE_COMPRESSED = 0x800;

        public const int NOERROR = 0;
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_FAIL = -2147467259;

        public const int DRAGDROP_S_DROP = 0x40100;
        public const int DRAGDROP_S_CANCEL = 0x40101;
        public const int DRAGDROP_S_USEDEFAULTCURSORS = 0x40102;

        public static int SHFILEINFO_size = Marshal.SizeOf(typeof(SHFILEINFO));
        public static int MENUITEMINFO_size = Marshal.SizeOf(typeof(MENUITEMINFO));
        public static int CMInvokeCommandInfoEx_size = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx));
        // Public Const cbTpmParams As Integer = Marshal.SizeOf(GetType(TPMPARAMS))

        // ListView Message Constants
        public const int LVM_FIRST = 0x1000;
        public const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        public const int LVM_ENSUREVISIBLE = LVM_FIRST + 19;
        public const int LVM_SCROLL = LVM_FIRST + 20;
        public const int LVM_GETEDITCONTROL = LVM_FIRST + 24;
        public const int LVM_SETITEMSTATE = LVM_FIRST + 43;
        public const int LVM_SETBKIMAGE = LVM_FIRST + 68;
        public const int LVM_SETTEXTBKCOLOR = LVM_FIRST + 38;
        public const int LVM_ENABLEGROUPVIEW = LVM_FIRST + 157;
        public const int LVM_INSERTGROUP = LVM_FIRST + 145;
        public const int LVM_REMOVEALLGROUPS = LVM_FIRST + 160;
        public const int LVM_SETITEM = LVM_FIRST + 6;
        public const int LVM_SETSELECTEDCOLUMN = LVM_FIRST + 140;
        public const int LVM_GETHEADER = 4127;
        public const int LVM_SETCOLUMN = 4122;
        public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
        public const int LVNI_VISIBLEONLY = 0x0040;

        // 'For ListItem State
        public const int LVIF_STATE = 0x8;
        public const int LVIS_SELECTED = 0x2;
        public const int LVIS_FOCUSED = 0x1;
        public const int LVIS_CUT = 0x4;

        // For BackgroundImage
        public const int LVBKIF_SOURCE_NONE = 0x0;
        public const int LVBKIF_SOURCE_URL = 0x2;
        public const int LVBKIF_STYLE_TILE = 0x10;
        public const int LVBKIF_STYLE_NORMAL = 0x0;

        // For ColumnHeader Images
        public const int HDM_SETIMAGELIST = 0x1208;
        public const int LVCF_FMT = 0x1;
        public const int LVCF_IMAGE = 0x10;
        public const int LVCFMT_IMAGE = 0x800;
        public const int LVCF_BITMAP_ON_RIGHT = 0x1000;
        public const int LVCF_STRING = 0x4000;

        // For ToolTips
        public const int LVS_EX_LABELTIP = 0x4000;

        // For ImageList_Draw
        public const int ILD_NORMAL = 0x0;
        public const int ILD_TRANSPARENT = 0x1;
        public const int ILD_BLEND25 = 0x2;
        public const int ILD_SELECTED = 0x4;
        public const int ILD_MASK = 0x10;
        public const int ILD_IMAGE = 0x20;

        // 'Other...
        public const int CLR_NONE = -0x1;

        #endregion

        #region    Shell GUIDs
        /// <summary>
        /// all of these should be read only but the problem is you can't use readonly instances with COM so they must not be readonly.
        /// </summary>
        public static Guid IID_IMalloc = new Guid("{00000002-0000-0000-C000-000000000046}");
        public static Guid IID_IShellFolder = new Guid("{000214E6-0000-0000-C000-000000000046}");
        public static Guid IID_IFolderFilterSite = new Guid("{C0A651F5-B48B-11d2-B5ED-006097C686F6}");
        public static Guid IID_IFolderFilter = new Guid("{9CC22886-DC8E-11d2-B1D0-00C04F8EEB3E}");
        public static Guid DesktopGUID = new Guid("{00021400-0000-0000-C000-000000000046}");

        public static Guid IID_IDropTarget = new Guid("{00000122-0000-0000-C000-000000000046}");
        public static Guid IID_IDataObject = new Guid("{0000010e-0000-0000-C000-000000000046}");

        public static Guid IID_IContextMenu = new Guid("{000214e4-0000-0000-c000-000000000046}");
        public static Guid IID_IContextMenu2 = new Guid("{000214f4-0000-0000-c000-000000000046}");
        public static Guid IID_IContextMenu3 = new Guid("{bcfce0a0-ec17-11d0-8d10-00a0c90f2719}");

        public static Guid IID_IExtractImage = new Guid("{BB2E617C-0920-11d1-9A0B-00C04FC2D6C1}");

        public static Guid IID_IQueryInfo = new Guid("{00021500-0000-0000-C000-000000000046}");
        public static Guid IID_IPersistFile = new Guid("{0000010b-0000-0000-C000-000000000046}");

        public static Guid CLSID_DragDropHelper = new Guid("{4657278A-411B-11d2-839A-00C04FD918D0}");
        public static Guid CLSID_NewMenu = new Guid("{D969A300-E7FF-11d0-A93B-00A0C90F2719}");
        public static Guid IID_IDragSourceHelper = new Guid("{DE5BF786-477A-11d2-839D-00C04FD918D0}");
        public static Guid IID_IDropTargetHelper = new Guid("{4657278B-411B-11d2-839A-00C04FD918D0}");

        public static Guid IID_IShellExtInit = new Guid("{000214e8-0000-0000-c000-000000000046}");
        public static Guid IID_IStream = new Guid("{0000000c-0000-0000-c000-000000000046}");
        public static Guid IID_IStorage = new Guid("{0000000B-0000-0000-C000-000000000046}");

        public static Guid CLSID_ShellLink = new Guid("{00021401-0000-0000-C000-000000000046}");
        public static Guid CLSID_InternetShortcut = new Guid("{FBF23B40-E3F0-101B-8488-00AA003E56F8}");

        public static Guid IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        public static Guid IID_IShellItemImageFactory = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");

        #endregion

        #region    Shell Structures

        #region        SHFILEINFO
        /// <summary>
        /// Contains information about a file object retrieved by <see cref="SHGetFileInfo"/>.
        /// Includes the icon handle, display name, type name, and attributes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public SFGAO dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_PATH)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        #endregion

        #region        STRRET Structures
        /// <summary>
        /// Represents a string returned by an IShellFolder method. The union layout supports
        /// multiple storage strategies (OLE string pointer, offset into an item ID, or inline buffer).
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct STRRET
        {
            [FieldOffset(0)]
            public int uType;       // One of the STRRET_* values
            [FieldOffset(4)]
            public int pOleStr; // must be freed by caller of GetDisplayNameOf
            [FieldOffset(4)]
            public int uOffset; // Offset into SHITEMID
            [FieldOffset(4)]
            public int pStr;    // NOT USED
        }
        #endregion

        #region        W32Find_Data
        /// <summary>
        /// W32Find_Data is a Class representation of the Win32_Find_Data Structure. It should be an exact replacement for
        /// that structure, but, for some reason, which I do not care to explore, is not.
        /// There are some references to Win32_Find_Data in the ShellDll Namespace which will simply cause the app to quit
        /// if given a W32Find_Data. I suspect it has to do with the problem API calls not having the necessary attributes on 
        /// the parameter.
        /// The references related to FindFirstFile and FindNextFile work just fine when called with this class rather than
        /// Win32_Find_Data. I do not care to pursue this, so I define both versions here.
        /// </summary>
        /// <remarks></remarks>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        [BestFitMapping(false)]
        public class W32Find_Data
        {
            public int dwFileAttributes;
            public uint ftCreationTimeLow;
            public uint ftCreationTimeHigh;
            public uint ftLastAccessTimeLow;
            public uint ftLastAccessTimeHigh;
            public uint ftLastWriteTimeLow;
            public uint ftLastWriteTimeHigh;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;
            
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_PATH)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;

            private string m_directoryname;

            public W32Find_Data(string DirectoryName)
            {
                m_directoryname = DirectoryName;
            }

            #region    Public Properties
            public FileAttributes Attributes
            {
                get => (FileAttributes)dwFileAttributes;
                set => dwFileAttributes = (int)value;
            }

            public bool IsCompressed
            {
                get
                {
                    return (dwFileAttributes & (int)FileAttributes.Compressed) == (int)FileAttributes.Compressed;
                }
            }

            public bool IsEncrypted
            {
                get
                {
                    return (dwFileAttributes & (int)FileAttributes.Encrypted) == (int)FileAttributes.Encrypted;
                }
            }

            public long Length
            {
                get
                {
                    return ((long)nFileSizeHigh << 0x20 | ((((long)nFileSizeLow & ((unchecked((int)0xFFFFFFFF)))))));
                }
            }

            public DateTime CreationTimeUTC
            {
                get
                {
                    long filetime = (long)ftCreationTimeHigh << 0x20 | ftCreationTimeLow;
                    return DateTime.FromFileTimeUtc(filetime);
                }
            }

            public DateTime CreationTime
            {
                get
                {
                    return CreationTimeUTC.ToLocalTime();
                }
            }

            public DateTime LastWriteTimeUTC
            {
                get
                {
                    long filetime = (long)ftLastWriteTimeHigh << 0x20 | ftLastWriteTimeLow;
                    return DateTime.FromFileTimeUtc(filetime);
                }
            }

            public DateTime LastWriteTime
            {
                get
                {
                    return LastWriteTimeUTC.ToLocalTime();
                }
            }

            public DateTime LastAccessTimeUTC
            {
                get
                {
                    long filetime = (long)ftLastAccessTimeHigh << 0x20 | ftLastAccessTimeLow;
                    return DateTime.FromFileTimeUtc(filetime);
                }
            }

            public DateTime LastAccessTime
            {
                get
                {
                    return LastAccessTimeUTC.ToLocalTime();
                }
            }

            public string Name
            {
                get
                {
                    return cFileName;
                }
            }

            public string DirectoryName
            {
                get
                {
                    if (m_directoryname is null || string.IsNullOrEmpty(m_directoryname))
                    {
                        return "";
                    }
                    else
                    {
                        return m_directoryname;
                    }
                }
                set
                {
                    m_directoryname = value;
                }
            }

            public string FullName
            {
                get
                {
                    if (DirectoryName.Equals(""))
                    {
                        return Name;
                    }
                    else
                    {
                        return DirectoryName + Path.DirectorySeparatorChar + Name;
                    }
                }
            }
            #endregion

        }

        #endregion

        #region    FindFirstFile and related declarations
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeFindHandle FindFirstFile(string fileName, [In()] W32Find_Data data);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool FindNextFile(SafeFindHandle hndFindFile, [In()][MarshalAs(UnmanagedType.LPStruct)] W32Find_Data lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr handle);

        /// <summary>
        /// Provides a <see cref="SafeHandleZeroOrMinusOneIsInvalid">SafeHandleZeroOrMinusOneIsInvalid</see> to FindFirstFile and
        /// FindNextFile, preset that the Handle will be reliably released.
        /// </summary>
        public sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeFindHandle() : base(true)
            {
            }

            /// <summary>
            /// Releases this Handle
            /// </summary>
            /// <returns></returns>
            /// <remarks></remarks>
            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }
        }
        #endregion

        #region        W32_FIND_DATA
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]

        public struct WIN32_FIND_DATA
        {
            public int dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_PATH)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }
        #endregion

        /// <summary>
        /// Contains the information needed to create a drag image during a drag-and-drop operation.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SHDRAGIMAGE
        {
            public Size sizeDragImage;
            public POINT ptOffset;
            public IntPtr hbmpDragImage;
            public Color crColorKey;
        }

        /// <summary>
        /// Represents a 64-bit file time as two 32-bit parts, counting 100-nanosecond intervals since January 1, 1601.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public int dwLowDateTime;
            public int dwHighDateTime;
        }


        /// <summary>
        /// Contains statistical data about an open storage, stream, or byte-array object.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct STATSTG
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwcsName;
            public STGTY @type;
            public long cbSize;
            public FILETIME mtime;
            public FILETIME ctime;
            public FILETIME atime;
            public STGM grfMode;
            public LOCKTYPE grfLocksSupported;
            public Guid clsid;
            public int grfStateBits;
            public int reserved;
        }

        #region        ImageList Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public POINT(int xValue, int yValue)
            {
                x = xValue;
                y = yValue;
            }
            public int x;
            public int y;
        }

        /// <summary>
        /// Contains information about a ListView background image, including source URL and tiling options.
        /// Used with <c>LVM_SETBKIMAGE</c>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct LVBKIMAGE
        {
            public int ulFlags;
            public IntPtr hbm;
            public string pszImage;
            public int cchImageMax;
            public int xOffsetPercent;
            public int yOffsetPercent;
        }

        /// <summary>
        /// Contains information about a ListView item (row) such as state, text, image, and indent.
        /// Used with <c>LVM_SETITEM</c> and related messages.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct LVITEM
        {
            public int mask;
            public int iItem;
            public int iSubItem;
            public int state;
            public int stateMask;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszText;
            public string cchTextMax;
            public int iImage;
            public int lParam;
            public int iIndent;
            public int iGroupId;
            public int cColumns;
            public int puColumns;
        }

        /// <summary>
        /// Contains information about a ListView column such as format, width, text, and image.
        /// Used with <c>LVM_SETCOLUMN</c> and related messages.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Auto)]
        public struct LVCOLUMN
        {
            public int mask;
            public int fmt;
            public int cx;
            public IntPtr pszText;
            public int cchTextMax;
            public int iSubItem;
            public int iImage;
            public int iOrder;
        }
        #endregion

        #endregion

        #region        shell32 Dll Declarations

        #region        DragQueryFiles
        /// <summary>
        /// Retrieves the names of dropped files that result from a successful drag-and-drop operation.
        /// </summary>
        /// <param name="hDrop">Handle to the internal drop structure (from <c>WM_DROPFILES</c>).</param>
        /// <param name="iFile">Index of the file to query, or <c>0xFFFFFFFF</c> to get the count of files.</param>
        /// <param name="lpszFile">Buffer that receives the file name. May be <c>null</c> when requesting the count.</param>
        /// <param name="cch">Size of the <paramref name="lpszFile"/> buffer, in characters.</param>
        /// <returns>
        /// When <paramref name="iFile"/> is <c>0xFFFFFFFF</c>, returns the number of dropped files.
        /// Otherwise returns a nonzero value on success, or zero on failure.
        /// </returns>
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern int DragQueryFile(IntPtr hDrop, int iFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFile, int cch);




        #endregion

        #region        IL functions
        /// <summary>
        /// Tests whether two PIDLs refer to the same object using a binary comparison.
        /// </summary>
        /// <param name="pidl1">First PIDL to compare.</param>
        /// <param name="pidl2">Second PIDL to compare.</param>
        /// <returns><c>true</c> if the PIDLs are equal; otherwise <c>false</c>.</returns>
        [DllImport("shell32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ILIsEqual(IntPtr pidl1, IntPtr pidl2);

        /// <summary>
        /// Tests whether one PIDL is the parent of another.
        /// </summary>
        /// <param name="pidlParent">The potential parent PIDL.</param>
        /// <param name="pidlBelow">The potential child PIDL.</param>
        /// <param name="fImmediate">If <c>true</c>, tests for an immediate parent-child relationship only.</param>
        /// <returns><c>true</c> if <paramref name="pidlParent"/> is a parent of <paramref name="pidlBelow"/>; otherwise <c>false</c>.</returns>
        [DllImport("shell32", EntryPoint = "#23", CharSet = CharSet.Auto)]
        public static extern bool ILIsParent(IntPtr pidlParent, IntPtr pidlBelow, bool fImmediate);


        /// <summary>
        /// Combines two PIDLs by appending the second to the first, allocating a new PIDL with <c>CoTaskMemAlloc</c>.
        /// </summary>
        /// <param name="pidl1">The first (typically absolute) PIDL.</param>
        /// <param name="pidl2">The second (typically relative) PIDL.</param>
        /// <returns>A newly allocated PIDL containing the concatenation. The caller must free this with <c>CoTaskMemFree</c>.</returns>
        [DllImport("shell32", EntryPoint = "#25", CharSet = CharSet.Auto)]
        public static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        /// <summary>
        /// Returns a pointer to the last item ID in a PIDL. The returned pointer points within the original PIDL
        /// and must not be freed separately.
        /// </summary>
        /// <param name="pidl">The PIDL to inspect.</param>
        /// <returns>A pointer to the last <c>SHITEMID</c> in the PIDL.</returns>
        [DllImport("shell32", EntryPoint = "#16", CharSet = CharSet.Auto)]
        public static extern IntPtr ILFindLastID(IntPtr pidl);

        //[DllImport("shell32", EntryPoint = "#17", CharSet = CharSet.Auto)]
        //public static extern bool ILRemoveLastID([In()] ref IntPtr pidl);

        /// <summary>
        /// Removes the last item ID from a PIDL, shortening the list by one segment.
        /// </summary>
        /// <param name="pidl">The PIDL to modify in place.</param>
        /// <returns><c>true</c> if the last item was successfully removed; <c>false</c> if the PIDL has only one item or is invalid.</returns>
        [DllImport("shell32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ILRemoveLastID(IntPtr pidl);

        /// <summary>
        /// Advances to the next item ID in a PIDL.
        /// </summary>
        /// <param name="pidl">Pointer to the current item ID.</param>
        /// <returns>A pointer to the next item ID, or <see cref="IntPtr.Zero"/> if at the end.</returns>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr ILGetNext(IntPtr pidl);

        /// <summary>
        /// Returns the size, in bytes, of a PIDL including the terminating null.
        /// </summary>
        /// <param name="pidl">The PIDL to measure.</param>
        /// <returns>The size in bytes.</returns>
        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern uint ILGetSize(IntPtr pidl);

        /// <summary>
        /// Clones a PIDL by allocating a copy with <c>CoTaskMemAlloc</c> and duplicating the item IDs.
        /// </summary>
        /// <param name="pidl">The PIDL to clone.</param>
        /// <returns>A newly allocated copy. The caller must free this with <c>CoTaskMemFree</c>.</returns>
        [DllImport("shell32.dll", ExactSpelling = true)]
        internal static extern IntPtr ILClone(IntPtr pidl);

        /// <summary>
        /// Frees a PIDL that was allocated by a Shell function. Prefer <c>CoTaskMemFree</c> for most use cases.
        /// </summary>
        /// <param name="pidl">The PIDL to free.</param>
        [DllImport("shell32.dll", ExactSpelling = true)]
        internal static extern void ILFree(IntPtr pidl); //don't recommend you use this.  Use CoTaskMemFree instead

        /// <summary>
        /// Creates an absolute PIDL (pointer to an item identifier list) from a file system path.
        /// </summary>
        /// <param name="pszPath">
        /// The null-terminated Unicode path to a file system object (for example, <c>C:\Temp\File.txt</c>).
        /// </param>
        /// <returns>
        /// A pointer to the resulting absolute PIDL if successful; otherwise <see cref="IntPtr.Zero"/>.
        /// </returns>
        /// <remarks>
        /// The returned PIDL must be released with <c>ILFree</c>.
        /// </remarks>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr ILCreateFromPathW([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        #endregion

        #region        Notification Declarations

        /// <summary>
        /// Specifies a Shell item to watch for changes, identified by PIDL, and whether to watch recursively.
        /// Used as an entry in the array passed to <see cref="SHChangeNotifyRegister(IntPtr, SHCNRF, SHCNE, WM, int, SHChangeNotifyEntry[])"/>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHChangeNotifyEntry
        {
            public IntPtr pIdl;
            public bool Recursively;
        }

        /// <summary>
        /// Contains the two PIDLs associated with a Shell change notification message.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SHNOTIFYSTRUCT
        {
            public IntPtr dwItem1;
            public IntPtr dwItem2;
        }

        // Registers a window that receives notifications from the file system or shell
        [DllImport("shell32", EntryPoint = "#2", CharSet = CharSet.Auto)]
        public static extern int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, [MarshalAs(UnmanagedType.LPArray)] SHChangeNotifyEntry[] pfsne);

        // Unregisters the client's window process from receiving SHChangeNotify
        [DllImport("shell32", EntryPoint = "#4", CharSet = CharSet.Auto)]
        public static extern bool SHChangeNotifyDeregister(int hNotify);

        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent);

        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern int SHChangeNotification_Unlock(IntPtr hLock);

        /// <summary>
        /// Notifies the system of an event that an application has performed. Shell change notifications
        /// cause registered clients to receive update messages.
        /// </summary>
        /// <param name="wEventId">The event type (see <see cref="SHCNE"/>).</param>
        /// <param name="uFlags">Flags indicating the meaning of <paramref name="dwItem1"/> and <paramref name="dwItem2"/>.</param>
        /// <param name="dwItem1">First PIDL or pointer whose meaning depends on <paramref name="uFlags"/>.</param>
        /// <param name="dwItem2">Second PIDL or pointer whose meaning depends on <paramref name="uFlags"/>.</param>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        #endregion

        #region        SHGetDesktopFolder
        /// <summary>
        /// Retrieves the <see cref="IShellFolder"/> interface for the desktop folder,
        /// which is the root of the Shell's namespace.
        /// </summary>
        /// <param name="ppshf">Receives the <see cref="IShellFolder"/> interface for the desktop folder.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern int SHGetDesktopFolder(ref IShellFolder ppshf);

        /// <summary>
        /// Retrieves the PIDL of a known folder identified by its <see cref="Guid"/>.
        /// </summary>
        /// <param name="rfid">The known folder identifier (e.g., <c>FOLDERID_Documents</c>).</param>
        /// <param name="dwFlags">Flags controlling the retrieval (typically <c>0</c>).</param>
        /// <param name="hToken">An access token, or <see cref="IntPtr.Zero"/> for the current user.</param>
        /// <param name="ppidl">Receives the PIDL of the known folder. The caller must free this with <c>CoTaskMemFree</c>.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderIDList(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppidl);

        #endregion

        #region        SHGetFileInfo
        /// <summary>
        /// Retrieves information about an object in the file system, such as a file, folder, directory, or drive root.
        /// This overload accepts the path as a string.
        /// </summary>
        /// <param name="pszPath">The path to the file or folder to query.</param>
        /// <param name="dwFileAttributes">File attribute flags used when <c>SHGFI_USEFILEATTRIBUTES</c> is set.</param>
        /// <param name="sfi">Receives the file information.</param>
        /// <param name="cbsfi">The size, in bytes, of the <paramref name="sfi"/> structure.</param>
        /// <param name="uFlags">Flags specifying which information to retrieve (e.g., <c>SHGFI_ICON</c>).</param>
        /// <returns>A value whose meaning depends on <paramref name="uFlags"/>.</returns>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, int dwFileAttributes, ref SHFILEINFO sfi, int cbsfi, int uFlags);


        /// <summary>
        /// Retrieves information about a file object from the Windows Shell, such as its display name,
        /// type name, icon, attributes, or system image list index.
        /// </summary>
        /// <param name="pszPath">
        /// The locator of the file, folder, or file type to query.
        /// If <c>uFlags</c> includes <c>SHGFI.PIDL</c>, this can be a PIDL
        /// 
        /// If <c>uFlags</c> includes <c>SHGFI_USEFILEATTRIBUTES</c>, this can be a file extension
        /// (for example, <c>".txt"</c>) and does not need to reference an existing item.
        /// </param>
        /// <param name="dwFileAttributes">
        /// File attributes to use when <c>SHGFI_USEFILEATTRIBUTES</c> is specified (for example,
        /// <c>FILE_ATTRIBUTE_NORMAL</c> or <c>FILE_ATTRIBUTE_DIRECTORY</c>); otherwise typically <c>0</c>.
        /// </param>
        /// <param name="psfi">
        /// On success, receives the requested Shell file information in an <see cref="SHFILEINFOW"/> structure.
        /// </param>
        /// <param name="cbFileInfo">
        /// The size, in bytes, of the <see cref="SHFILEINFOW"/> structure pointed to by <paramref name="psfi"/>.
        /// Typically <c>(uint)Marshal.SizeOf&lt;SHFILEINFOW&gt;()</c>.
        /// </param>
        /// <param name="uFlags">
        /// A combination of <c>SHGFI_*</c> flags that specifies which information to retrieve
        /// (for example, <c>SHGFI_ICON</c>, <c>SHGFI_TYPENAME</c>, <c>SHGFI_SYSICONINDEX</c>).
        /// </param>
        /// <returns>
        ///     Returns a value whose meaning depends on <paramref name="uFlags"/>:
        ///     <list type="bullet">
        ///     <item><description>
        ///     If <c>SHGFI_SYSICONINDEX</c> is specified, returns a handle to the system image list.
        ///     </description></item>
        ///     <item><description>
        ///     Otherwise, returns a nonzero value on success; <c>0</c> on failure.
        ///     </description></item>
        ///     </list>
        /// </returns>
        /// <remarks>
        /// This function is the Unicode variant of <c>SHGetFileInfo</c>.
        /// If <c>SHGFI_ICON</c> is requested, <c>psfi.hIcon</c> must be released with <c>DestroyIcon</c>
        /// when no longer needed to avoid resource leaks.
        /// </remarks>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(IntPtr pszPath, int dwFileAttributes, ref SHFILEINFO psfi, int cbFileInfo, SHGFI uFlags);




        #endregion

        #region        ShGetImageListHandle
        /// <summary>
        /// Retrieves a handle to a system image list (small, large, extra-large, etc.).
        /// Not exported correctly in XP; see KB316931. Accessed by ordinal 727.
        /// </summary>
        /// <param name="iImageList">The image list to retrieve (e.g., <c>SHIL_SMALL</c> = 0, <c>SHIL_LARGE</c> = 1).</param>
        /// <param name="riid">Reference to the IID of the desired image list interface.</param>
        /// <param name="handle">Receives the image list handle.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll", EntryPoint = "#727")]
        public static extern int SHGetImageListHandle(int iImageList, ref Guid riid, ref IntPtr handle);
        #endregion

        #region        SHGetMalloc
        /// <summary>
        /// Retrieves a pointer to the Shell's <see cref="IMalloc"/> interface.
        /// Not typically needed in .NET applications; prefer <see cref="System.Runtime.InteropServices.Marshal"/> methods.
        /// </summary>
        /// <param name="pMalloc">Receives the <see cref="IMalloc"/> interface.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern int SHGetMalloc(ref IMalloc pMalloc);
        #endregion

        #region        SHGetNewLinkInfo
        /// <summary>Despite its name, the API returns a filename
        /// for a link to be copied/created in a Target directory,
        /// with a specific LinkTarget. It will create a unique name
        /// unless instructed otherwise (SHGLNI_NOUNIQUE).  And add
        /// the ".lnk" extension, unless instructed otherwise(SHGLNI.NOLNK)
        /// </summary>
        [DllImport("shell32", EntryPoint = "SHGetNewLinkInfoA", CharSet = CharSet.Ansi)]
        public static extern int SHGetNewLinkInfo(string pszLinkTo, string pszDir, [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, ref bool pfMustCopy, SHGNLI uFlags);

        /// <summary> Same function using a PIDL as the pszLinkTo.
        /// SHGNLI.PIDL must be set.
        /// </summary>
        [DllImport("shell32", EntryPoint = "SHGetNewLinkInfoA", CharSet = CharSet.Ansi)]
        public static extern int SHGetNewLinkInfo(IntPtr pszLinkTo, string pszDir, [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, ref bool pfMustCopy, SHGNLI uFlags);

        #endregion

        #region PathIDLists
        /// <summary>
        /// Converts a display name (file path, URL, or shell namespace path) to a PIDL.
        /// </summary>
        /// <param name="name">The display name to parse.</param>
        /// <param name="bindingContext">Optional bind context, or <see cref="IntPtr.Zero"/>.</param>
        /// <param name="pidl">Receives the resulting PIDL. The caller must free this with <c>CoTaskMemFree</c>.</param>
        /// <param name="sfgaoIn">Requested <see cref="SFGAO"/> attributes to query.</param>
        /// <param name="sfgaoOut">Receives the actual attributes of the object.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

        /// <summary>
        /// Converts a PIDL to a file system path.
        /// </summary>
        /// <param name="pidl">The PIDL to convert.</param>
        /// <param name="Path">Buffer to receive the path.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the PIDL does not refer to a file system object.</returns>
        [DllImport("shell32", CharSet = CharSet.Unicode)]
        public static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder Path);

        /// <summary>
        /// Converts a PIDL to a file system path, supporting long paths (beyond MAX_PATH).
        /// </summary>
        /// <param name="pidl">The PIDL to convert.</param>
        /// <param name="pszPath">Buffer to receive the path.</param>
        /// <param name="cchPath">Size of the buffer in characters.</param>
        /// <param name="uOpts">Flags (typically <c>0</c>).</param>
        /// <returns><c>true</c> on success; <c>false</c> if the PIDL does not refer to a file system object.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SHGetPathFromIDListEx(
            IntPtr pidl,
            [Out] StringBuilder pszPath,
            uint cchPath,
            uint uOpts);

        /// <summary>
        /// Creates an <see cref="IShellItem"/> from an existing PIDL, using the <c>IntPtr</c> overload.
        /// </summary>
        /// <param name="pidl">The absolute PIDL.</param>
        /// <param name="riid">The interface ID to retrieve (typically <see cref="IID_IShellItem"/>).</param>
        /// <param name="ppv">Receives the <see cref="IShellItem"/> interface.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, out IntPtr ppv);

        //[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        //static extern void SHCreateItemFromIDList(
        //    [In] IntPtr pidl,
        //    [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        //    [Out, MarshalAs(UnmanagedType.Interface, IidParameterIndex = 1)] out object ppv
        //);

        /// <summary>
        /// Creates an <see cref="IShellItem"/> from an existing PIDL, returning the strongly-typed interface.
        /// </summary>
        [DllImport("shell32.dll", PreserveSig = false)]
        public static extern int SHCreateItemFromIDList(
            IntPtr pidl,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
        
        /// <summary>
        /// Creates an IShellItem from a file path
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            [In] string pszPath,
            [In] IntPtr pbc,
            [In] ref Guid riid,
            [Out] out IntPtr ppv
        );

        /// <summary>
        /// Creates an <see cref="IShellItem"/> from a file system path or shell namespace parsing name.
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern IShellItem SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid);

        /// <summary>
        /// Creates an <see cref="IShellItem"/> for a child item given its parent folder and relative PIDL.
        /// This is the preferred shell-validated alternative to <c>ILCombine</c> for constructing
        /// absolute shell items from a parent/child pair.
        /// </summary>
        /// <param name="pidlParent">
        /// An absolute PIDL of the parent folder. Can be <see cref="IntPtr.Zero"/> if
        /// <paramref name="psfParent"/> is provided instead.
        /// </param>
        /// <param name="psfParent">
        /// The <c>IShellFolder</c> interface of the parent folder. Can be <c>null</c> if
        /// <paramref name="pidlParent"/> is provided instead. If both are provided,
        /// <paramref name="psfParent"/> takes precedence.
        /// </param>
        /// <param name="pidlChild">
        /// A relative PIDL identifying the child item within the parent folder.
        /// Typically obtained from <c>IShellFolder::EnumObjects</c> or <c>IShellFolder::ParseDisplayName</c>.
        /// </param>
        /// <param name="riid">
        /// The IID of the interface to retrieve. Typically <c>IID_IShellItem</c>
        /// (<c>43826D1E-E718-42EE-BC55-A1E261C37BFE</c>).
        /// </param>
        /// <param name="ppvItem">
        /// When this method returns, contains the requested interface pointer for the child shell item.
        /// The caller is responsible for releasing this object.
        /// </param>
        /// <returns>
        /// Returns <c>S_OK</c> (0) on success, or a COM error <c>HRESULT</c> on failure.
        /// Common failure codes include <c>E_INVALIDARG</c> if both parent parameters are <c>null</c>/zero,
        /// or <c>E_NOINTERFACE</c> if the requested interface is not supported.
        /// </returns>
        /// <remarks>
        /// Requires Windows Vista or later.
        /// At least one of <paramref name="pidlParent"/> or <paramref name="psfParent"/> must be provided.
        /// Unlike <c>ILCombine</c>, this function is namespace-aware and validates that the child
        /// item belongs to the given parent, making it the safer choice for constructing shell items.
        /// </remarks>
        [DllImport("shell32.dll")]
        public static extern int SHCreateItemWithParent(
            IntPtr pidlParent,
            [MarshalAs(UnmanagedType.IUnknown)] object psfParent,
            IntPtr pidlChild,
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvItem);

        /// <summary>
        /// Retrieves the absolute PIDL (<c>PIDLIST_ABSOLUTE</c>) for a shell object that
        /// implements <c>IPersistIDList</c> or <c>IPersistFolder2</c>, such as an
        /// <c>IShellItem</c> or <c>IShellFolder</c>.
        /// </summary>
        /// <param name="punk">
        /// The COM object from which to retrieve the PIDL. Must implement either
        /// <c>IPersistIDList</c> or <c>IPersistFolder2</c>. Commonly an <c>IShellItem</c>
        /// or <c>IShellFolder</c> instance.
        /// </param>
        /// <param name="ppidl">
        /// When this method returns, contains the absolute PIDL representing the full
        /// path of the shell object from the desktop root. The caller is responsible
        /// for freeing this PIDL using <c>ILFree</c> or <c>CoTaskMemFree</c>.
        /// Set to <see cref="IntPtr.Zero"/> on failure.
        /// </param>
        /// <returns>
        /// Returns <c>S_OK</c> (0) on success, or a COM error <c>HRESULT</c> on failure.
        /// Returns <c>E_NOINTERFACE</c> if <paramref name="punk"/> does not implement
        /// <c>IPersistIDList</c> or <c>IPersistFolder2</c>.
        /// </returns>
        /// <remarks>
        /// Requires Windows Vista or later.
        /// This is the preferred way to obtain an absolute PIDL from a COM shell object,
        /// as it works with any object that supports <c>IPersistIDList</c> — including
        /// <c>IShellItem</c>, <c>IShellFolder</c>, and custom namespace extension objects.
        /// The returned PIDL must always be freed by the caller to avoid memory leaks.
        /// </remarks>
        [DllImport("shell32.dll")]
        public static extern int SHGetIDListFromObject(
            [MarshalAs(UnmanagedType.IUnknown)] object punk,
            out IntPtr ppidl);

        /// <summary>
        /// Retrieves the display name of a Shell item identified by its PIDL.
        /// </summary>
        /// <param name="pidl">The PIDL of the item.</param>
        /// <param name="sigdnName">The name format to retrieve (see <see cref="SIGDN"/>).</param>
        /// <param name="ppszName">Receives the name string. The caller must free this with <c>CoTaskMemFree</c>.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int SHGetNameFromIDList(IntPtr pidl, SIGDN sigdnName, out IntPtr ppszName);

        /// <summary>
        /// Retrieves the display name of a Shell item identified by its PIDL (strongly-typed overload).
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHGetNameFromIDList(
            IntPtr pidl,
            SIGDN sigdnName,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszName
        );

        /// <summary>
        /// Converts a PIDL to a file system path using a character array buffer.
        /// </summary>
        /// <param name="pidl">The PIDL to convert.</param>
        /// <param name="pszPath">Character array buffer to receive the path.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the PIDL does not refer to a file system object.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SHGetPathFromIDListW(IntPtr pidl, [Out] char[] pszPath);

        public const uint SICHINT_CANONICAL = 0x10000000;
        public const uint SICHINT_TEST_FILESYSPATH_IF_NOT_EQUAL = 0x20000000;

                // SHGetDataFromIDList format values
        internal const int SHGDFIL_FINDDATA = 1; //pv should point to a WIN32_FIND_DATA
        internal const int SHGDFIL_NETRESOURCE = 2; //pv should point to a NETRESOURCE
        internal const int SHGDFIL_DESCRIPTIONID = 3; //pv should point to a SHDESCRIPTIONID

        /// <summary>
        /// Retrieves item data stored in the Shell's internal format for a given PIDL.
        /// </summary>
        /// <param name="psf">The parent <see cref="IShellFolder"/> that owns the PIDL.</param>
        /// <param name="pidl">The relative child PIDL.</param>
        /// <param name="nFormat">The data format to retrieve (e.g., <c>SHGDFIL_FINDDATA</c>).</param>
        /// <param name="pv">Receives the requested data.</param>
        /// <param name="cb">The size of the output structure in bytes.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int SHGetDataFromIDListW(
            [MarshalAs(UnmanagedType.Interface)] IShellFolder psf,
            IntPtr pidl,          // relative child PIDL
            int nFormat,          // e.g. SHGDFIL_FINDDATA
            out WIN32_FIND_DATAW pv,
            int cb);               // Marshal.SizeOf<WIN32_FIND_DATAW>()

        #endregion

        #region        SHGetRealIDL
        // SHGetRealIDL converts a relative PIDL to a full PIDL
        // Note that Win2K and before do not export SHGetRealIDL, though support it at Ordinal 98
        [DllImport("shell32", EntryPoint = "#98", CharSet = CharSet.Auto)]
        public static extern int SHGetRealIDL(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal);


        #endregion

        #region        SHGetSpecialFolderLocation

        [DllImport("shell32")]
        public static extern int SHGetSpecialFolderLocation(int hWndOwner, int csidl, ref IntPtr ppidl);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);


        #endregion

        #endregion

        #region        shlwapi Dll Declarations

        /// Accepts a STRRET structure returned by IShellFolder::GetDisplayNameOf that contains or points to a 
        /// string, and then returns that string as a BSTR.
        /// <param>
        /// Pointer to a STRRET structure.
        /// Pointer to an ITEMIDLIST uniquely identifying a file object or subfolder relative
        /// Pointer to a variable of type BSTR that contains the converted string.
        /// </param>
        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        public static extern int StrRetToBSTR(ref STRRET pstr, IntPtr pidl, [MarshalAs(UnmanagedType.BStr)] ref string pbstr);

        /// <summary>
        /// Takes a STRRET structure returned by IShellFolder::GetDisplayNameOf, 
        /// converts it to a string, and 
        /// places the result in a buffer. 
        /// <param>
        /// Pointer to a STRRET structure.
        /// Pointer to an ITEMIDLIST uniquely identifying a file object or subfolder relative
        /// Pointer to a Buffer to hold the display name. It will be returned as a null-terminated
        /// string. If cchBuf is too small, 
        /// the name will be truncated to fit. 
        /// Size of pszBuf, in characters. 
        /// </param>
        /// </summary>
        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        public static extern int StrRetToBuf(IntPtr pstr, IntPtr pidl, StringBuilder pszBuf, [MarshalAs(UnmanagedType.U4)] int cchBuf);

        [Flags]
        public enum ASSOCF : uint
        {
            NONE = 0
        }

        public enum ASSOCSTR
        {
            COMMAND = 1,
            EXECUTABLE,
            FRIENDLYDOCNAME,
            FRIENDLYAPPNAME,
            NOOPEN,
            SHELLNEWVALUE,
            DDECOMMAND,
            DDEIFEXEC,
            DDEAPPLICATION,
            DDETOPIC,
            INFOTIP,
            QUICKTIP,
            TILEINFO,
            CONTENTTYPE,
            DEFAULTICON,
            SHELLEXTENSION,
            DROPTARGET,
            DELEGATEEXECUTE,
            SUPPORTED_URI_PROTOCOLS,
            PROGID,
            APPID,
            APPPUBLISHER,
            APPICONREFERENCE,
            MAX
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int AssocQueryString(
            ASSOCF flags,
            ASSOCSTR str,
            string pszAssoc,
            string pszExtra,
            StringBuilder pszOut,
            ref uint pcchOut);

        /// <summary>
        /// Compares two PIDL (Pointer to an Item ID List) values.
        /// </summary>
        /// <param name="lpsf">
        /// A pointer to an IShellFolder interface. If NULL, the Desktop folder is used.
        /// </param>
        /// <param name="pidl1">The first PIDL to compare.</param>
        /// <param name="pidl2">The second PIDL to compare.</param>
        /// <param name="flags">
        /// Comparison flags. Use SHCIDS_CANONICALONLY (0x10000000) for logical identity checks.
        /// </param>
        /// <returns>
        /// Returns 0 if the PIDLs are equal.
        /// Returns a negative value if pidl1 comes before pidl2.
        /// Returns a positive value if pidl1 comes after pidl2.
        /// </returns>
        [DllImport("shlwapi.dll", EntryPoint = "#556", CharSet = CharSet.Unicode)]
        public static extern int SHCompareIDList(
            IntPtr lpsf,        // IShellFolder* — pass IntPtr.Zero to use the Desktop folder
            IntPtr pidl1,       // PCIDLIST_ABSOLUTE
            IntPtr pidl2,       // PCIDLIST_ABSOLUTE
            uint flags          // SHCIDS_* flags
        );
        // Common flags for the 'flags' parameter
        public const uint SHCIDS_CANONICALONLY = 0x10000000;
        public const uint SHCIDS_ALLFIELDS = 0x80000000;


        #endregion

        #region        user32 Dll Declarations

        #region            SendMessage
        public const int SB_HORZ = 0;
        public const int SB_VERT = 1;
        public const uint SIF_ALL = 0x17;

        [StructLayout(LayoutKind.Sequential)]
        public struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }
        /// <summary>
        /// Contains information about a hit test in a ListView control, including the point, flags, and item/group index.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct LVHITTESTINFO
        {
            public POINT pt;
            public uint flags;
            public int iItem;
            public int iSubItem;
            public int iGroup; // safe for modern comctl; ignored if unused
        }

        /// <summary>
        /// Sends the specified message to a window or windows and waits for the message to be processed.
        /// Multiple overloads are provided for different parameter type combinations.
        /// </summary>
        /// <param name="hWnd">Handle of the window to receive the message. Use <see cref="HWND_BROADCAST"/> for all top-level windows.</param>
        /// <param name="Msg">The message to send.</param>
        /// <param name="wParam">Additional message-specific information.</param>
        /// <param name="lParam">Additional message-specific information.</param>
        /// <returns>The result of the message processing; meaning depends on the message.</returns>
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint wMsg, IntPtr wParam, IntPtr lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref RECT lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVHITTESTINFO lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

        /// <summary>
        /// Retrieves the current position of a scrollbar thumb in the specified window.
        /// </summary>
        /// <param name="hWnd">Handle to the window with the scrollbar.</param>
        /// <param name="nBar">The scrollbar to query (<see cref="SB_HORZ"/> or <see cref="SB_VERT"/>).</param>
        /// <returns>The current position of the scrollbar thumb.</returns>
        [DllImport("user32.dll")]
        public static extern int GetScrollPos(IntPtr hWnd, int nBar);

        /// <summary>
        /// Retrieves the range, page size, and current position of a scrollbar.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO lpScrollInfo);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, WM wMsg, int wParam, IntPtr lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SendMessage(IntPtr hWnd, uint wMsg, int wParam, ref LVBKIMAGE lParam);

        /// <inheritdoc cref="SendMessage(IntPtr, uint, int, int)" />
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SendMessage(IntPtr hWnd, uint wMsg, int wParam, ref LVITEM lParam);

        /// <summary>
        /// Places a message in the message queue of the specified window and returns immediately.
        /// </summary>
        /// <param name="hWnd">Handle of the window whose message queue is to receive the message.</param>
        /// <param name="Msg">The message to post.</param>
        /// <param name="wParam">Additional message-specific information.</param>
        /// <param name="lParam">Additional message-specific information.</param>
        /// <returns><c>true</c> if the message was successfully posted; otherwise <c>false</c>.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Destroys an icon and frees any memory the icon occupied.
        /// </summary>
        /// <param name="hIcon">Handle to the icon to be destroyed.</param>
        /// <returns><c>true</c> if the icon was successfully destroyed; otherwise <c>false</c>.</returns>
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(
            IntPtr hWnd,
            out RECT rect);


        #region Menu related

        [DllImport("user32", CharSet = CharSet.Auto)]
        //public static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);
        //public static extern bool AppendMenu(IntPtr hMenu, UInt32 uFlags, UIntPtr uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);
        public static extern bool AppendMenu(IntPtr hMenu, UInt32 uFlags, UInt32 uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);

        /// <summary>
        /// Creates a new popup menu.
        /// </summary>
        /// <returns>Handle to the newly created popup menu, or <see cref="IntPtr.Zero"/> on failure.</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        /// <summary>
        /// Returns the number of items in the specified menu.
        /// </summary>
        /// <param name="hMenu">Handle to the menu.</param>
        /// <returns>The number of items, or <c>-1</c> on failure.</returns>
        [DllImport("user32.dll")]
        public static extern int GetMenuItemCount(int hMenu);

        /// <summary>
        /// Retrieves the handle to the submenu at the specified position in a menu.
        /// </summary>
        /// <param name="hMenu">Handle to the menu.</param>
        /// <param name="nPos">Zero-based position of the submenu.</param>
        /// <returns>Handle to the submenu, or <see cref="IntPtr.Zero"/> if none exists.</returns>
        [DllImport("user32")]
        public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        /// <summary>
        /// Inserts a new menu item at the specified position in a menu.
        /// </summary>
        /// <param name="hMenu">Handle to the menu.</param>
        /// <param name="uItem">Identifier or position, depending on <paramref name="fByPosition"/>.</param>
        /// <param name="fByPosition">If <c>true</c>, <paramref name="uItem"/> is a zero-based position; otherwise it is a command identifier.</param>
        /// <param name="lpmii">A <see cref="MENUITEMINFO"/> structure specifying the menu item.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern bool InsertMenuItem(IntPtr hMenu, int uItem, bool fByPosition, ref MENUITEMINFO lpmii);

        /// <summary>
        /// Displays a shortcut menu at the specified location and tracks the selection.
        /// </summary>
        /// <param name="hMenu">Handle to the shortcut menu.</param>
        /// <param name="uFlags">Flags controlling function behavior.</param>
        /// <param name="x">Horizontal position in screen coordinates.</param>
        /// <param name="y">Vertical position in screen coordinates.</param>
        /// <param name="hWnd">Handle to the window that owns the popup menu.</param>
        /// <param name="lptpm">Pointer to a <c>TPMPARAMS</c> structure, or <see cref="IntPtr.Zero"/>.</param>
        /// <returns>The menu item identifier of the item the user selected, or <c>0</c> if cancelled.</returns>
        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, int uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        /// <summary>
        /// Destroys the specified menu and frees any memory that the menu occupies.
        /// </summary>
        /// <param name="hMenu">Handle to the menu to destroy.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyMenu(IntPtr hMenu);
        #endregion

        /// <summary>
        /// Registers a new clipboard format or retrieves the identifier of an existing format.
        /// </summary>
        /// <param name="lpszFormat">The name of the clipboard format to register or look up.</param>
        /// <returns>The clipboard format identifier, or <c>0</c> on failure.</returns>
        [DllImport("User32", CharSet = CharSet.Auto)]
        public static extern int RegisterClipboardFormat(string lpszFormat);
        
        #endregion

        #endregion

        #region        comctl32 Dll Declarations

        /// <summary>
        /// Retrieves the dimensions of images in an image list.
        /// </summary>
        /// <param name="himl">Handle to the image list.</param>
        /// <param name="cx">Receives the width, in pixels, of each image.</param>
        /// <param name="cy">Receives the height, in pixels, of each image.</param>
        /// <returns>Nonzero on success; zero on failure.</returns>
        [DllImport("comctl32")]
        public static extern int ImageList_GetIconSize(IntPtr himl, ref int cx, ref int cy);

        /// <summary>
        /// Replaces an image in an image list with an icon.
        /// </summary>
        /// <param name="hImageList">Handle to the image list.</param>
        /// <param name="IconIndex">Index of the image to replace.</param>
        /// <param name="hIcon">Handle to the icon to set.</param>
        /// <returns>The index of the replaced image, or <c>-1</c> on failure.</returns>
        [DllImport("comctl32", CharSet = CharSet.Auto)]
        public static extern int ImageList_ReplaceIcon(IntPtr hImageList, int IconIndex, IntPtr hIcon);

        /// <summary>
        /// Returns the number of images in an image list.
        /// </summary>
        /// <param name="hImageList">Handle to the image list.</param>
        /// <returns>The number of images, or <c>0</c> if the list is empty.</returns>
        [DllImport("comctl32", CharSet = CharSet.Auto)]
        public static extern int ImageList_GetImageCount(IntPtr hImageList);

        /// <summary>
        /// Creates an icon from an image in an image list.
        /// </summary>
        /// <param name="himl">Handle to the image list.</param>
        /// <param name="i">Index of the image.</param>
        /// <param name="flags">Drawing flags (see <see cref="ILD"/>).</param>
        /// <returns>Handle to the newly created icon, or <see cref="IntPtr.Zero"/> on failure. The caller must destroy this icon.</returns>
        [DllImport("comctl32")]
        public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, ILD flags);

        /// <summary>
        /// Draws an image list image onto a device context at the specified position.
        /// </summary>
        /// <param name="hIml">Handle to the image list.</param>
        /// <param name="indx">Index of the image to draw.</param>
        /// <param name="hdcDst">Handle to the destination device context.</param>
        /// <param name="x">X-coordinate in the device context.</param>
        /// <param name="y">Y-coordinate in the device context.</param>
        /// <param name="fStyle">Drawing style flags (see <see cref="ILD"/>).</param>
        /// <returns>Nonzero on success; zero on failure.</returns>
        [DllImport("comctl32")]
        public static extern int ImageList_Draw(IntPtr hIml, int indx, IntPtr hdcDst, int x, int y, int fStyle);

        /// <summary>
        /// Draws an image list image onto a device context with extended options for background and foreground colors.
        /// </summary>
        /// <param name="hIml">Handle to the image list.</param>
        /// <param name="i">Index of the image to draw.</param>
        /// <param name="hdcDst">Handle to the destination device context.</param>
        /// <param name="x">X-coordinate in the device context.</param>
        /// <param name="y">Y-coordinate in the device context.</param>
        /// <param name="dx">Width of the image to draw.</param>
        /// <param name="dy">Height of the image to draw.</param>
        /// <param name="rgbBk">Background color, or <c>CLR_NONE</c> for transparent.</param>
        /// <param name="rgbFg">Foreground (blend) color, or <c>CLR_NONE</c>.</param>
        /// <param name="fStyle">Drawing style flags (see <see cref="ILD"/>).</param>
        /// <returns>Nonzero on success; zero on failure.</returns>
        [DllImport("comctl32")]
        public static extern int ImageList_DrawEx(IntPtr hIml, int i, IntPtr hdcDst, int x, int y, int dx, int dy, int rgbBk, int rgbFg, int fStyle);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ImageList_SetImageCount(
            IntPtr himl,
            uint uNewCount);

        #endregion

        #region        Ole32 Dll Declarations
        
        [DllImport("ole32.dll")]
        public static extern int OleInitialize(IntPtr pvReserved);

        [DllImport("ole32.dll")]
        public static extern void OleUninitialize();

        /// <summary>
        /// Registers the specified application window as a potential target for OLE drag-and-drop operations.
        /// </summary>
        /// <param name="hWnd">Handle to the window to register as a drop target.</param>
        /// <param name="IdropTgt">The <see cref="IDropTarget"/> implementation that receives drop notifications.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RegisterDragDrop(IntPtr hWnd, IDropTarget IdropTgt);

        /// <summary>
        /// Revokes the registration of the specified application window as an OLE drag-and-drop drop target.
        /// </summary>
        /// <param name="hWnd">Handle to the window previously registered with <see cref="RegisterDragDrop"/>.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RevokeDragDrop(IntPtr hWnd);

        /// <summary>
        /// Frees the specified storage medium and its associated resources.
        /// </summary>
        /// <param name="pmedium">The <see cref="STGMEDIUM"/> to release.</param>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        /// <summary>
        /// Carries out an OLE drag-and-drop operation, registering the data object and drop source
        /// and entering a modal loop until the operation completes.
        /// </summary>
        /// <param name="pDataObject">The data object being dragged.</param>
        /// <param name="pDropSource">The drop source that provides visual feedback and drop semantics.</param>
        /// <param name="dwOKEffect">Allowed effects (combination of <see cref="DragDropEffects"/>).</param>
        /// <param name="pdwEffect">Receives the effect that was performed.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int DoDragDrop(IntPtr pDataObject, [MarshalAs(UnmanagedType.Interface)] IDropSource pDropSource, DragDropEffects dwOKEffect, out DragDropEffects pdwEffect);

        /// <summary>
        /// Creates a COM object instance identified by its CLSID.
        /// </summary>
        /// <param name="rclsid">The CLSID of the object to create.</param>
        /// <param name="pUnkOuter">Aggregate controlling <c>IUnknown</c>, or <see cref="IntPtr.Zero"/>.</param>
        /// <param name="dwClsContext">Context in which the code will run (see <see cref="CLSCTX"/>).</param>
        /// <param name="riid">The IID of the interface to retrieve.</param>
        /// <param name="ppv">Receives the requested interface pointer.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, CLSCTX dwClsContext, ref Guid riid, out IntPtr ppv);

        /// <summary>
        /// Retrieves a data object that provides access to the contents of the clipboard.
        /// </summary>
        /// <param name="ppDataObj">Receives the clipboard data object implementing <c>IDataObject</c>.</param>
        /// <returns>An <c>HRESULT</c> indicating success or failure.</returns>
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int OleGetClipboard(out IntPtr ppDataObj);

        #endregion

        #region        kernel32 Declarations
        /// <summary>
        /// Locks a Global memory Handle. Used for referencing stg.hGlobal in some CIDA related cases
        /// of ExploreControls. Returns a pointer to the actual data block, dealing with intra and inter
        /// application Drag ops.
        /// </summary>
        /// <param name="handle">A Global memory handle.</param>
        /// <returns>Pointer to actual data.</returns>
        /// <remarks>Needed when actually implementing IDropTarget type processing.</remarks>
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr handle);
        /// <summary>
        /// Releases a handle by decrementing a reference counter kept with it.
        /// </summary>
        /// <param name="handle">A previously GlobalLock locked Global memory handle.</param>
        /// <returns>True if locks remain, False if none.</returns>
        /// <remarks>Just unlocks a previous lock.</remarks>
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr handle);

        #endregion

        #region        gdi32 Dll Declarations

        /// <summary>
        /// Deletes a logical pen, brush, font, bitmap, region, or palette, freeing all system resources associated with the object.
        /// </summary>
        /// <param name="hObject">Handle to the GDI object to delete.</param>
        /// <returns>Nonzero on success; zero if the handle is invalid or the object is currently selected.</returns>
        [DllImport("gdi32", CharSet = CharSet.Auto)]
        public static extern int DeleteObject(IntPtr hObject);

        #endregion

        #region        Context Menu Related

        #region  Structures 

        /// <summary>
        /// Contains information about a menu item such as type, state, identifier, submenu handle, and text.
        /// Used with <c>InsertMenuItem</c>, <c>GetMenuItemInfo</c>, and related functions.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MENUITEMINFO
        {

            public MENUITEMINFO(string text)
            {
                cbSize = Marshal.SizeOf(this);
                dwTypeData = text;
                cch = text.Length;
                fMask = 0;
                fType = 0;
                fState = 0;
                wID = 0;
                hSubMenu = IntPtr.Zero;
                hbmpChecked = IntPtr.Zero;
                hbmpUnchecked = IntPtr.Zero;
                dwItemData = IntPtr.Zero;
                hbmpItem = IntPtr.Zero;
            }

            public int cbSize;
            public int fMask;
            public int fType;
            public int fState;
            public int wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string dwTypeData;
            public int cch;
            public IntPtr hbmpItem;
        }

        /// <summary>
        /// Extended version of <c>CMINVOKECOMMANDINFO</c> used by context menu handlers for ANSI and Unicode verb strings.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CMInvokeCommandInfoEx
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpDirectory;
            public int nShow;
            public int dwHotKey;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpTitle;
            public IntPtr lpVerbW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpParametersW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpDirectoryW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpTitleW;
            public Point ptInvoke;
        }

        #endregion

        #endregion

        #region        Drag/Drop Stuctures

        #region            FORMATETC Structure
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct FORMATETC
        {
            public CF cfFormat;
            public IntPtr ptd;
            public DVASPECT dwAspect;
            public int lindex;
            public TYMED Tymd; // ShellDll.ShellAPI.TYMED
        }
        #endregion

        #region            STGMEDIUM Structure
        [StructLayout(LayoutKind.Sequential)]
        public struct STGMEDIUM
        {
            public int tymed;
            public IntPtr hGlobal;
            public IntPtr pUnkForRelease;
        }
        #endregion

        #region            DROPFILES Structure
        [StructLayout(LayoutKind.Sequential)]
        public struct DROPFILES
        {
            public int pFiles;
            public POINT pt;
            public bool fNC;
            public bool fWide;
        }
        #endregion

        #endregion

        #region    Public Shared Methods

        #region        Get Special Folder Paths
        public static string GetSpecialFolderPath(IntPtr hWnd, int csidl)
        {
            IntPtr res;
            IntPtr ppidl;
            ppidl = GetSpecialFolderLocation(hWnd, csidl);
            var shfi = new SHFILEINFO();
            var uFlags = SHGFI.PIDL | SHGFI.DISPLAYNAME | SHGFI.TYPENAME;
            // uFlags = uFlags Or SHGFI.SYSICONINDEX
            int dwAttr = 0;
            res = SHGetFileInfo(ppidl, dwAttr, ref shfi, SHFILEINFO_size, uFlags);
            Marshal.FreeCoTaskMem(ppidl);
            return shfi.szDisplayName + "  (" + shfi.szTypeName + ")";
        }

        /// <summary>
        /// Returns an IntPtr referencing the PIDL of the requested Special Folder.
        /// </summary>
        /// <param name="hWnd">Unused</param>
        /// <param name="csidl">The integer equivalent of the CSIDL Enum Value for the desired Special Folder.</param>
        /// <returns>An IntPtr referencing the PIDL of the requested Special Folder.</returns>
        /// <remarks></remarks>
        public static IntPtr GetSpecialFolderLocation(IntPtr hWnd, int csidl)
        {
            IntPtr rVal = IntPtr.Zero;
            int res;
            res = SHGetSpecialFolderLocation(0, csidl, ref rVal);
            return rVal;
        }

        public static string GetSpecialShellPath(Guid location)
        {
            IntPtr pPath;
            int hr = SHGetKnownFolderPath(location, 0, IntPtr.Zero, out pPath);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            string path = string.Empty;
            try
            {
                path = Marshal.PtrToStringUni(pPath)!;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pPath);
            }
            return path;
        }

        #endregion

        #region        IsXpOrAbove and Is2KOrAbove
        /// <summary>
        /// Determines is the current OS is Windows XP or newer.
        /// </summary>
        /// <returns>True if current OS is Windows XP or newer. Returns False otherwise.</returns>
        /// <remarks></remarks>
        public static bool IsXpOrAbove()
        {
            bool rVal = false;
            if (Environment.OSVersion.Version.Major > 5)
            {
                rVal = true;
            }
            else if (Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1)
            {
                rVal = true;
            }
            // if none of the above tests succeed, then return false
            return rVal;
        }

        /// <summary>
        /// Determines is the current OS is Windows 2000 or newer.
        /// </summary>
        /// <returns>True if current OS is Windows XP or newer. Returns False otherwise.</returns>
        /// <remarks></remarks>
        public static bool Is2KOrAbove()
        {
            if (Environment.OSVersion.Version.Major >= 5)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Determines is the current OS is Windows Vista or newer.
        /// </summary>
        /// <returns>True if current OS is Windows Vista or newer. Returns False otherwise.</returns>
        /// <remarks></remarks>
        public static bool IsVistaOrAbove()
        {
            if (Environment.OSVersion.Version.Major >= 6)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion

        #endregion

        #region Interfaces 


        #endregion
    }
}
