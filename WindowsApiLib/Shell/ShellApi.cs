using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

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

        public const int DRAGDROP_S_DROP = 0x40100;
        public const int DRAGDROP_S_CANCEL = 0x40101;
        public const int DRAGDROP_S_USEDEFAULTCURSORS = 0x40102;

        public static int cbFileInfo = Marshal.SizeOf(typeof(SHFILEINFO));
        public static int cbMenuItemInfo = Marshal.SizeOf(typeof(MENUITEMINFO));
        // Public Const cbTpmParams As Integer = Marshal.SizeOf(GetType(TPMPARAMS))
        public static int cbInvokeCommand = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx));

        // ListView Message Constants
        public const int LVM_FIRST = 0x1000;
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
        public static readonly Guid IID_IMalloc = new Guid("{00000002-0000-0000-C000-000000000046}");
        public static readonly Guid IID_IShellFolder = new Guid("{000214E6-0000-0000-C000-000000000046}");
        public static readonly Guid IID_IFolderFilterSite = new Guid("{C0A651F5-B48B-11d2-B5ED-006097C686F6}");
        public static readonly Guid IID_IFolderFilter = new Guid("{9CC22886-DC8E-11d2-B1D0-00C04F8EEB3E}");
        public static readonly Guid DesktopGUID = new Guid("{00021400-0000-0000-C000-000000000046}");

        public static readonly Guid IID_IDropTarget = new Guid("{00000122-0000-0000-C000-000000000046}");
        public static readonly Guid IID_IDataObject = new Guid("{0000010e-0000-0000-C000-000000000046}");

        public static readonly Guid IID_IContextMenu = new Guid("{000214e4-0000-0000-c000-000000000046}");
        public static readonly Guid IID_IContextMenu2 = new Guid("{000214f4-0000-0000-c000-000000000046}");
        public static readonly Guid IID_IContextMenu3 = new Guid("{bcfce0a0-ec17-11d0-8d10-00a0c90f2719}");

        public static readonly Guid IID_IExtractImage = new Guid("{BB2E617C-0920-11d1-9A0B-00C04FC2D6C1}");

        public static readonly Guid IID_IQueryInfo = new Guid("{00021500-0000-0000-C000-000000000046}");
        public static readonly Guid IID_IPersistFile = new Guid("{0000010b-0000-0000-C000-000000000046}");

        public static readonly Guid CLSID_DragDropHelper = new Guid("{4657278A-411B-11d2-839A-00C04FD918D0}");
        public static readonly Guid CLSID_NewMenu = new Guid("{D969A300-E7FF-11d0-A93B-00A0C90F2719}");
        public static readonly Guid IID_IDragSourceHelper = new Guid("{DE5BF786-477A-11d2-839D-00C04FD918D0}");
        public static readonly Guid IID_IDropTargetHelper = new Guid("{4657278B-411B-11d2-839A-00C04FD918D0}");

        public static readonly Guid IID_IShellExtInit = new Guid("{000214e8-0000-0000-c000-000000000046}");
        public static readonly Guid IID_IStream = new Guid("{0000000c-0000-0000-c000-000000000046}");
        public static readonly Guid IID_IStorage = new Guid("{0000000B-0000-0000-C000-000000000046}");

        public static readonly Guid CLSID_ShellLink = new Guid("{00021401-0000-0000-C000-000000000046}");
        public static readonly Guid CLSID_InternetShortcut = new Guid("{FBF23B40-E3F0-101B-8488-00AA003E56F8}");

        public static readonly Guid IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        public static readonly Guid IID_IShellItemImageFactory = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");

        #endregion

        #region    Shell Structures

        #region        SHFILEINFO
        // ///<summary>
        // SHFILEINFO structure for VB.Net
        // ///</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public SFGAO dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_NAME)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        #endregion

        #region        STRRET Structures
        // both of these formats work in main thread, neither in worker thread
        // <StructLayout(LayoutKind.Sequential)> _
        // Public Structure STRRET
        // Public uType As Integer
        // Public pOle As IntPtr
        // End Structure
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
            
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_NAME)]
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

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WinSDK.MAX_NAME)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }
        #endregion

        // Contains the information needed to create a drag image
        [StructLayout(LayoutKind.Sequential)]
        public struct SHDRAGIMAGE
        {
            public Size sizeDragImage;
            public POINT ptOffset;
            public IntPtr hbmpDragImage;
            public Color crColorKey;
        }

        // Represents the number of 100-nanosecond intervals since January 1, 1601
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public int dwLowDateTime;
            public int dwHighDateTime;
        }


        // Contains statistical data about an open storage, stream, or byte-array object
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
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern int DragQueryFile(IntPtr hDrop, int iFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFile, int cch);




        #endregion

        #region        IL functions
        [DllImport("shell32", EntryPoint = "#21", CharSet = CharSet.Auto)]
        public static extern bool ILIsEqual(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32", EntryPoint = "#23", CharSet = CharSet.Auto)]
        public static extern bool ILIsParent(IntPtr pidlParent, IntPtr pidlBelow, bool fImmediate);


        [DllImport("shell32", EntryPoint = "#25", CharSet = CharSet.Auto)]
        public static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32", EntryPoint = "#16", CharSet = CharSet.Auto)]
        public static extern IntPtr ILFindLastID(IntPtr pidl);
        [DllImport("shell32", EntryPoint = "#17", CharSet = CharSet.Auto)]
        public static extern bool ILRemoveLastID([In()] ref IntPtr pidl);
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr ILGetNext(IntPtr pidl);

        #endregion

        #region        Notification Declarations

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHChangeNotifyEntry
        {
            public IntPtr pIdl;
            public bool Recursively;
        }

        // Contains two PIDLs concerning the notify message
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

        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);



        #endregion

        #region        SHGetDesktopFolder
        // <summary>
        // Retrieves the IShellFolder interface for the desktop folder, 
        // which is the root of the Shell's namespace. 
        // <param>
        // ppshf -- Recieves the IShellFolder interface for the desktop folder
        // </param>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern int SHGetDesktopFolder(ref IShellFolder ppshf);
        #endregion

        #region        SHGetFileInfo
        // SHGetFileInfo
        // Retrieves information about an object in the file system,
        // such as a file, a folder, a directory, or a drive root.

        // <summary>
        // SHGetFileInfo  - for a given Path as a string
        // </summary>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, int dwFileAttributes, ref SHFILEINFO sfi, int cbsfi, int uFlags);




        // <summary>
        // SHGetFileInfo  - for a given ItemIDList as IntPtr
        // </summary>
        [DllImport("shell32", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(IntPtr ppidl, int dwFileAttributes, ref SHFILEINFO sfi, int cbsfi, SHGFI uFlags);




        #endregion

        #region        ShGetImageListHandle
        // UPDATE: Add SHGetImageListHandle
        /// <summary>
        /// SHGetImageList is not exported correctly in XP.  See KB316931
        /// http://support.microsoft.com/default.aspx?scid=kb;EN-US;Q316931
        /// Apparently (and hopefully) ordinal 727 isn't going to change.
        /// </summary>
        [DllImport("shell32.dll", EntryPoint = "#727")]

        public static extern int SHGetImageListHandle(int iImageList, ref Guid riid, ref IntPtr handle);
        #endregion

        #region        SHGetMalloc
        // <summary>
        // Get an Imalloc Interface
        // Not required for .Net apps, use Marshal class
        // </summary>
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
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

        [DllImport("shell32", CharSet = CharSet.Unicode)]
        public static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder Path);

        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHCreateItemFromIDList(IntPtr pidl, [In] ref Guid riid, out IntPtr ppv);

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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int SHGetNameFromIDList(IntPtr pidl, SIGDN sigdnName, out IntPtr ppszName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SHGetPathFromIDListW(IntPtr pidl, [Out] char[] pszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        #endregion

        #region        SHGetRealIDL
        // SHGetRealIDL converts a simple PIDL to a full PIDL
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

        #region            STRRETtoSomeString
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

        #endregion

        #endregion

        #region        user32 Dll Declarations

        #region            SendMessage
        // <summary>
        // Sends a message to some Window
        // </summary>
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, WM wMsg, int wParam, IntPtr lParam);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SendMessage(IntPtr hWnd, uint wMsg, int wParam, ref LVBKIMAGE lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SendMessage(IntPtr hWnd, uint wMsg, int wParam, ref LVITEM lParam);

        #endregion

        #region            DestroyIcon
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
        #endregion

        #region Menu related

        [DllImport("user32", CharSet = CharSet.Auto)]
        //public static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);
        //public static extern bool AppendMenu(IntPtr hMenu, UInt32 uFlags, UIntPtr uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);
        public static extern bool AppendMenu(IntPtr hMenu, UInt32 uFlags, UInt32 uIDNewItem, [MarshalAs(UnmanagedType.LPTStr)] string lpNewItem);

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        public static extern int GetMenuItemCount(int hMenu);

        [DllImport("user32")]
        public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern bool InsertMenuItem(IntPtr hMenu, int uItem, bool fByPosition, ref MENUITEMINFO lpmii);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, int uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyMenu(IntPtr hMenu);
        #endregion

        #region            RegisterClipboardFormat

        [DllImport("User32", CharSet = CharSet.Auto)]
        public static extern int RegisterClipboardFormat(string lpszFormat);

        #endregion

        #endregion

        #region        comctl32 Dll Declarations

        #region        ImageList_GetIconSize
        // <summary>
        // Gets an IconSize from a ImagelistHandle
        // </summary>
        [DllImport("comctl32")]
        public static extern int ImageList_GetIconSize(IntPtr himl, ref int cx, ref int cy);


        #endregion

        #region        ImageList_ReplaceIcon
        [DllImport("comctl32", CharSet = CharSet.Auto)]
        public static extern int ImageList_ReplaceIcon(IntPtr hImageList, int IconIndex, IntPtr hIcon);



        [DllImport("comctl32", CharSet = CharSet.Auto)]
        public static extern int ImageList_GetImageCount(IntPtr hImageList);
        #endregion

        #region        ImageList_GetIcon
        [DllImport("comctl32")]
        public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, ILD flags);


        #endregion

        #region        ImageList_Draw
        [DllImport("comctl32")]
        public static extern int ImageList_Draw(IntPtr hIml, int indx, IntPtr hdcDst, int x, int y, int fStyle);





        #endregion

        #region        ImageList_DrawEx
        // Used for hidden folders in ExpCombo
        [DllImport("comctl32")]
        public static extern int ImageList_DrawEx(IntPtr hIml, int i, IntPtr hdcDst, int x, int y, int dx, int dy, int rgbBk, int rgbFg, int fStyle);

        #endregion

        #endregion

        #region        Ole32 Dll Declarations

        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RegisterDragDrop(IntPtr hWnd, IDropTarget IdropTgt);

        // Revokes the registration of the specified application window as a potential target for 
        // OLE drag-and-drop operations
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RevokeDragDrop(IntPtr hWnd);

        // This function frees the specified storage medium
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        // Carries out an OLE drag and drop operation
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int DoDragDrop(IntPtr pDataObject, [MarshalAs(UnmanagedType.Interface)] IDropSource pDropSource, DragDropEffects dwOKEffect, out DragDropEffects pdwEffect);

        // Retrieves a drag/drop helper interface for drawing the drag/drop images
        [DllImport("ole32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, CLSCTX dwClsContext, ref Guid riid, out IntPtr ppv);

        // Retrieves a data object that you can use to access the contents of the clipboard
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

        [DllImport("gdi32", CharSet = CharSet.Auto)]
        public static extern int DeleteObject(IntPtr hObject);

        #endregion

        #region        Context Menu Related

        #region  Structures 

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
            res = SHGetFileInfo(ppidl, dwAttr, ref shfi, cbFileInfo, uFlags);
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