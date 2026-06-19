
namespace WindowsApiLib.Shell
{
    // This file has been split from the ShellDll.vb file simply to reduce its size. Otherwise it is
    // just an extension of ShellAPI. It contains various Enums used in referencing the Windows APIs.
    public partial class ShellAPI
    {
        #region    Windows Messages
        /// <summary>
        /// Windows Message Numbers
        /// </summary>
        /// <remarks></remarks>
        [Flags()]
        public enum WM : uint
        {
            ACTIVATE = 6U,
            ACTIVATEAPP = 28U,
            AFXFIRST = 864U,
            AFXLAST = 895U,
            APP = 32768U,
            ASKCBFORMATNAME = 780U,
            CANCELJOURNAL = 75U,
            CANCELMODE = 31U,
            CAPTURECHANGED = 533U,
            CHANGECBCHAIN = 781U,
            CHAR = 258U,
            CHARTOITEM = 47U,
            CHILDACTIVATE = 34U,
            CLEAR = 771U,
            CLOSE = 16U,
            COMMAND = 273U,
            COMPACTING = 65U,
            COMPAREITEM = 57U,
            CONTEXTMENU = 123U,
            COPY = 769U,
            COPYDATA = 74U,
            CREATE = 1U,
            CTLCOLORBTN = 309U,
            CTLCOLORDLG = 310U,
            CTLCOLOREDIT = 307U,
            CTLCOLORLISTBOX = 308U,
            CTLCOLORMSGBOX = 306U,
            CTLCOLORSCROLLBAR = 311U,
            CTLCOLORSTATIC = 312U,
            CUT = 768U,
            DEADCHAR = 259U,
            DELETEITEM = 45U,
            DESTROY = 2U,
            DESTROYCLIPBOARD = 775U,
            DEVICECHANGE = 537U,
            DEVMODECHANGE = 27U,
            DISPLAYCHANGE = 126U,
            DRAWCLIPBOARD = 776U,
            DRAWITEM = 43U,
            DROPFILES = 563U,
            ENABLE = 10U,
            ENDSESSION = 22U,
            ENTERIDLE = 289U,
            ENTERMENULOOP = 529U,
            ENTERSIZEMOVE = 561U,
            ERASEBKGND = 20U,
            EXITMENULOOP = 530U,
            EXITSIZEMOVE = 562U,
            FONTCHANGE = 29U,
            GETDLGCODE = 135U,
            GETFONT = 49U,
            GETHOTKEY = 51U,
            GETICON = 127U,
            GETMINMAXINFO = 36U,
            GETOBJECT = 61U,
            GETSYSMENU = 787U,
            GETTEXT = 13U,
            GETTEXTLENGTH = 14U,
            HANDHELDFIRST = 856U,
            HANDHELDLAST = 863U,
            HELP = 83U,
            HOTKEY = 786U,
            HSCROLL = 276U,
            HSCROLLCLIPBOARD = 782U,
            ICONERASEBKGND = 39U,
            IME_CHAR = 646U,
            IME_COMPOSITION = 271U,
            IME_COMPOSITIONFULL = 644U,
            IME_CONTROL = 643U,
            IME_ENDCOMPOSITION = 270U,
            IME_KEYDOWN = 656U,
            IME_KEYLAST = 271U,
            IME_KEYUP = 657U,
            IME_NOTIFY = 642U,
            IME_REQUEST = 648U,
            IME_SELECT = 645U,
            IME_SETCONTEXT = 641U,
            IME_STARTCOMPOSITION = 269U,
            INITDIALOG = 272U,
            INITMENU = 278U,
            INITMENUPOPUP = 279U,
            INPUTLANGCHANGE = 81U,
            INPUTLANGCHANGEREQUEST = 80U,
            KEYDOWN = 256U,
            KEYFIRST = 256U,
            KEYLAST = 264U,
            KEYUP = 257U,
            KILLFOCUS = 8U,
            LBUTTONDBLCLK = 515U,
            LBUTTONDOWN = 513U,
            LBUTTONUP = 514U,
            LVM_GETEDITCONTROL = 4120U,
            LVM_SETIMAGELIST = 4099U,
            MBUTTONDBLCLK = 521U,
            MBUTTONDOWN = 519U,
            MBUTTONUP = 520U,
            MDIACTIVATE = 546U,
            MDICASCADE = 551U,
            MDICREATE = 544U,
            MDIDESTROY = 545U,
            MDIGETACTIVE = 553U,
            MDIICONARRANGE = 552U,
            MDIMAXIMIZE = 549U,
            MDINEXT = 548U,
            MDIREFRESHMENU = 564U,
            MDIRESTORE = 547U,
            MDISETMENU = 560U,
            MDITILE = 550U,
            MEASUREITEM = 44U,
            MENUCHAR = 288U,
            MENUCOMMAND = 294U,
            MENUDRAG = 291U,
            MENUGETOBJECT = 292U,
            MENURBUTTONUP = 290U,
            MENUSELECT = 287U,
            MOUSEACTIVATE = 33U,
            MOUSEFIRST = 512U,
            MOUSEHOVER = 673U,
            MOUSELAST = 522U,
            MOUSELEAVE = 675U,
            MOUSEMOVE = 512U,
            MOUSEWHEEL = 522U,
            MOVE = 3U,
            MOVING = 534U,
            NCACTIVATE = 134U,
            NCCALCSIZE = 131U,
            NCCREATE = 129U,
            NCDESTROY = 130U,
            NCHITTEST = 132U,
            NCLBUTTONDBLCLK = 163U,
            NCLBUTTONDOWN = 161U,
            NCLBUTTONUP = 162U,
            NCMBUTTONDBLCLK = 169U,
            NCMBUTTONDOWN = 167U,
            NCMBUTTONUP = 168U,
            NCMOUSEHOVER = 672U,
            NCMOUSELEAVE = 674U,
            NCMOUSEMOVE = 160U,
            NCPAINT = 133U,
            NCRBUTTONDBLCLK = 166U,
            NCRBUTTONDOWN = 164U,
            NCRBUTTONUP = 165U,
            NEXTDLGCTL = 40U,
            NEXTMENU = 531U,
            NOTIFY = 78U,
            NOTIFYFORMAT = 85U,
            NULL = 0U,
            PAINT = 15U,
            PAINTCLIPBOARD = 777U,
            PAINTICON = 38U,
            PALETTECHANGED = 785U,
            PALETTEISCHANGING = 784U,
            PARENTNOTIFY = 528U,
            PASTE = 770U,
            PENWINFIRST = 896U,
            PENWINLAST = 911U,
            POWER = 72U,
            PRINT = 791U,
            PRINTCLIENT = 792U,
            QUERYDRAGICON = 55U,
            QUERYENDSESSION = 17U,
            QUERYNEWPALETTE = 783U,
            QUERYOPEN = 19U,
            QUEUESYNC = 35U,
            QUIT = 18U,
            RBUTTONDBLCLK = 518U,
            RBUTTONDOWN = 516U,
            RBUTTONUP = 517U,
            RENDERALLFORMATS = 774U,
            RENDERFORMAT = 773U,
            SETCURSOR = 32U,
            SETFOCUS = 7U,
            SETFONT = 48U,
            SETHOTKEY = 50U,
            SETICON = 128U,
            SETMARGINS = 211U,
            SETREDRAW = 11U,
            SETTEXT = 12U,
            SETTINGCHANGE = 26U,
            SH_NOTIFY = 1025U,
            SHOWWINDOW = 24U,
            SIZE = 5U,
            SIZECLIPBOARD = 779U,
            SIZING = 532U,
            SPOOLERSTATUS = 42U,
            STYLECHANGED = 125U,
            STYLECHANGING = 124U,
            SYNCPAINT = 136U,
            SYSCHAR = 262U,
            SYSCOLORCHANGE = 21U,
            SYSCOMMAND = 274U,
            SYSDEADCHAR = 263U,
            SYSKEYDOWN = 260U,
            SYSKEYUP = 261U,
            TCARD = 82U,
            TIMECHANGE = 30U,
            TIMER = 275U,
            TVM_GETEDITCONTROL = 4367U,
            TVM_SETIMAGELIST = 4361U,
            UNDO = 772U,
            UNINITMENUPOPUP = 293U,
            USER = 1024U,
            USERCHANGED = 84U,
            VKEYTOITEM = 46U,
            VSCROLL = 277U,
            VSCROLLCLIPBOARD = 778U,
            WINDOWPOSCHANGED = 71U,
            WINDOWPOSCHANGING = 70U,
            WININICHANGE = 26U
        }

        #endregion

        #region    Shell Enumerations

        #region    CSIDL
        public enum CSIDL : int
        {
            DESKTOP = 0x0,
            INTERNET = 0x1,
            PROGRAMS = 0x2,
            CONTROLS = 0x3,
            PRINTERS = 0x4,
            PERSONAL = 0x5,
            FAVORITES = 0x6,
            STARTUP = 0x7,
            RECENT = 0x8,
            SENDTO = 0x9,
            BITBUCKET = 0xA,
            STARTMENU = 0xB,
            MYDOCUMENTS = 0xC,
            MYMUSIC = 0xD,
            MYVIDEO = 0xE,
            DESKTOPDIRECTORY = 0x10,
            DRIVES = 0x11,
            NETWORK = 0x12,
            NETHOOD = 0x13,
            FONTS = 0x14,
            TEMPLATES = 0x15,
            COMMON_STARTMENU = 0x16,
            COMMON_PROGRAMS = 0x17,
            COMMON_STARTUP = 0x18,
            COMMON_DESKTOPDIRECTORY = 0x19,
            APPDATA = 0x1A,
            PRINTHOOD = 0x1B,
            LOCAL_APPDATA = 0x1C,
            ALTSTARTUP = 0x1D,
            COMMON_ALTSTARTUP = 0x1E,
            COMMON_FAVORITES = 0x1F,
            INTERNET_CACHE = 0x20,
            COOKIES = 0x21,
            HISTORY = 0x22,
            COMMON_APPDATA = 0x23,
            WINDOWS = 0x24,
            SYSTEM = 0x25,
            PROGRAM_FILES = 0x26,
            MYPICTURES = 0x27,
            PROFILE = 0x28,
            SYSTEMX86 = 0x29,
            PROGRAM_FILESX86 = 0x2A,
            PROGRAM_FILES_COMMON = 0x2B,
            PROGRAM_FILES_COMMONX86 = 0x2C,
            COMMON_TEMPLATES = 0x2D,
            COMMON_DOCUMENTS = 0x2E,
            COMMON_ADMINTOOLS = 0x2F,
            ADMINTOOLS = 0x30,
            CONNECTIONS = 0x31,
            COMMON_MUSIC = 0x35,
            COMMON_PICTURES = 0x36,
            COMMON_VIDEO = 0x37,
            RESOURCES = 0x38,
            RESOURCES_LOCALIZED = 0x39,
            COMMON_OEM_LINKS = 0x3A,
            CDBURN_AREA = 0x3B,
            COMPUTERSNEARME = 0x3D,
            C_DRIVE = 0x70000000, //this isn't one of the standard csidl locations.  I added it to facilitate unit testing.
            FLAG_PER_USER_INIT = 0x800,
            FLAG_NO_ALIAS = 0x1000,
            FLAG_DONT_VERIFY = 0x4000,
            FLAG_CREATE = 0x8000,
            FLAG_MASK = 0xFF00
        }
        #endregion

        #region    E_STRRET

        [Flags()]
        private enum E_STRRET
        {
            WSTR = 0x0,          // Use STRRET.pOleStr
            OFFSET = 0x1,        // Use STRRET.uOffset to Ansi
            C_STR = 0x2         // Use STRRET.cStr
        }
        #endregion

        #region    SHCONTF
        [Flags()]
        public enum SHCONTF
        {
            EMPTY = 0,                      // used to zero a SHCONTF variable
            FOLDERS = 0x20,                 // only want folders enumerated (FOLDER)
            NONFOLDERS = 0x40,              // include non folders
            INCLUDEHIDDEN = 0x80,           // show items normally hidden
            INIT_ON_FIRST_NEXT = 0x100,     // allow EnumObject() to return before validating enum
            NETPRINTERSRCH = 0x200,         // hint that client is looking for printers
            SHAREABLE = 0x400,              // hint that client is looking sharable resources (remote shares)
            STORAGE = 0x800                // include all items with accessible storage and their ancestors
        }
        #endregion

        #region    SHCIDS
        [Flags()]
        public enum SHCIDS
        {
            ALLFIELDS = unchecked((int)0x80000000),
            CANONICALONLY = 0x10000000,
            BITMASK = unchecked((int)0xFFFF0000),
            COLUMNMASK = 0xFFFF
        }
        #endregion

        #region    SFGAO
        [Flags()]
        public enum SFGAO : uint
        {
            CANCOPY = 0x1,                    // Objects can be copied    
            CANMOVE = 0x2,                    // Objects can be moved     
            CANLINK = 0x4,                    // Objects can be linked    
            STORAGE = 0x8,                    // supports BindToObject(IID_IStorage)
            CANRENAME = 0x10,                 // Objects can be renamed
            CANDELETE = 0x20,                 // Objects can be deleted
            HASPROPSHEET = 0x40,              // Objects have property sheets
            DROPTARGET = 0x100,               // Objects are drop target
            CAPABILITYMASK = 0x177,           // This flag is a mask for the capability flags.
            ENCRYPTED = 0x2000,               // object is encrypted (use alt color)
            ISSLOW = 0x4000,                  // 'slow' object
            GHOSTED = 0x8000,                 // ghosted icon
            LINK = 0x10000,                   // Shortcut (link)
            SHARE = 0x20000,                  // shared
            READONLY = 0x40000,               // read-only
            HIDDEN = 0x80000,                 // hidden object
            DISPLAYATTRMASK = 0xFC000,        // This flag is a mask for the display attributes.
            FILESYSANCESTOR = 0x10000000,     // may contain children with FILESYSTEM
            FOLDER = 0x20000000,              // support BindToObject(IID_IShellFolder)
            FILESYSTEM = 0x40000000,          // is a win32 file system object (file/folder/root)
            HASSUBFOLDER = 0x80000000,        // may contain children with FOLDER
            CONTENTSMASK = 0x80000000,        // This flag is a mask for the contents attributes.
            VALIDATE = 0x1000000,             // invalidate cached information
            REMOVABLE = 0x2000000,            // is this removeable media?
            COMPRESSED = 0x4000000,           // Object is compressed (use alt color)
            BROWSABLE = 0x8000000,            // supports IShellFolder but only implements CreateViewObject() (non-folder view)
            NONENUMERATED = 0x100000,         // is a non-enumerated object
            NEWCONTENT = 0x200000,            // should show bold in explorer tree
            CANMONIKER = 0x400000,            // defunct
            HASSTORAGE = 0x400000,            // defunct
            STREAM = 0x400000,                // supports BindToObject(IID_IStream)
            STORAGEANCESTOR = 0x800000,       // may contain children with STORAGE or STREAM
            STORAGECAPMASK = 0x70C50008,      // for determining storage capabilities ie for open/save semantics
            PKEYSFGAOMASK = 0x81044000        // Mask of attributes obtainable via PKEYs
        }

        [Flags]
        public enum SFGAOF : uint
        {
            CANCOPY = 0x00000001,
            CANMOVE = 0x00000002,
            CANLINK = 0x00000004,
            STORAGE = 0x00000008,
            CANRENAME = 0x00000010,
            CANDELETE = 0x00000020,
            HASPROPSHEET = 0x00000040,
            DROPTARGET = 0x00000100,

            SYSTEM = 0x00001000,
            ENCRYPTED = 0x00002000,
            ISSLOW = 0x00004000,
            GHOSTED = 0x00008000,
            LINK = 0x00010000,
            SHARE = 0x00020000,
            READONLY = 0x00040000,
            HIDDEN = 0x00080000,

            NONENUMERATED = 0x00100000,
            NEWCONTENT = 0x00200000,
            CANMONIKER = 0x00400000, // older name
            STREAM = 0x00400000, // newer alias in some headers/wrappers
            STORAGEANCESTOR = 0x00800000,

            VALIDATE = 0x01000000,
            REMOVABLE = 0x02000000,
            COMPRESSED = 0x04000000,
            BROWSABLE = 0x08000000,

            FILESYSANCESTOR = 0x10000000,
            FOLDER = 0x20000000,
            FILESYSTEM = 0x40000000,
            HASSUBFOLDER = 0x80000000
        }

        #endregion

        #region    SHGFI
        [Flags()]
        public enum SHGFI
        {
            ICON = 0x100,                // get icon 
            DISPLAYNAME = 0x200,         // get display name 
            TYPENAME = 0x400,            // get type name 
            ATTRIBUTES = 0x800,          // get attributes 
            ICONLOCATION = 0x1000,       // get icon location 
            EXETYPE = 0x2000,            // return exe type 
            SYSICONINDEX = 0x4000,       // get system icon index 
            LINKOVERLAY = 0x8000,        // put a link overlay on icon 
            SELECTED = 0x10000,          // show icon in selected state 
            ATTR_SPECIFIED = 0x20000,    // get only specified attributes 
            LARGEICON = 0x0,             // get large icon 
            SMALLICON = 0x1,             // get small icon 
            OPENICON = 0x2,              // get open icon 
            SHELLICONSIZE = 0x4,         // get shell size icon 
            PIDL = 0x8,                  // pszPath is a pidl 
            USEFILEATTRIBUTES = 0x10,    // use passed dwFileAttribute 
            ADDOVERLAYS = 0x20,          // apply the appropriate overlays
            OVERLAYINDEX = 0x40          // Get the index of the overlay
        }
        #endregion

        #region    SHGDN
        [Flags()]
        public enum SHGDN
        {
            NORMAL = 0,
            INFOLDER = 1,
            FORADDRESSBAR = 16384,
            FORPARSING = 32768
        }
        #endregion

        #region    ILD --- Flags controlling how the Image List item is drawn
        // /// <summary>
        // /// Flags controlling how the Image List item is 
        // /// drawn
        // /// </summary>
        // [Flags]	
        // Public Enum ImageListDrawItemConstants : int
        // {
        // /// <summary>
        // /// Draw item normally.
        // /// </summary>
        // ILD_NORMAL = 0x0,
        // /// <summary>
        // /// Draw item transparently.
        // /// </summary>
        // ILD_TRANSPARENT = 0x1,
        // /// <summary>
        // /// Draw item blended with 25% of the specified foreground colour
        // /// or the Highlight colour if no foreground colour specified.
        // /// </summary>
        // ILD_BLEND25 = 0x2,
        // /// <summary>
        // /// Draw item blended with 50% of the specified foreground colour
        // /// or the Highlight colour if no foreground colour specified.
        // /// </summary>
        // ILD_SELECTED = 0x4,
        // /// <summary>
        // /// Draw the icon's mask
        // /// </summary>
        // ILD_MASK = 0x10,
        // /// <summary>
        // /// Draw the icon image without using the mask
        // /// </summary>
        // ILD_IMAGE = 0x20,
        // /// <summary>
        // /// Draw the icon using the ROP specified.
        // /// </summary>
        // ILD_ROP = 0x40,
        // /// <summary>
        // /// Preserves the alpha channel in dest. XP only.
        // /// </summary>
        // ILD_PRESERVEALPHA = 0x1000,
        // /// <summary>
        // /// Scale the image to cx, cy instead of clipping it.  XP only.
        // /// </summary>
        // ILD_SCALE = 0x2000,
        // /// <summary>
        // /// Scale the image to the current DPI of the display. XP only.
        // /// </summary>
        // ILD_DPISCALE = 0x4000
        // /// <summary>
        // /// Flags controlling how the Image List item is 
        // /// drawn
        // /// </summary>
        [Flags()]
        public enum ILD
        {
            NORMAL = 0x0,
            TRANSPARENT = 0x1,
            BLEND25 = 0x2,
            SELECTED = 0x4,
            MASK = 0x10,
            IMAGE = 0x20,
            ROP = 0x40,
            PRESERVEALPHA = 0x1000,
            SCALE = 0x2000,
            DPISCALE = 0x4000
        }
        #endregion

        #region    ILS --- XP ImageList Draw State options
        // /// <summary>
        // /// Enumeration containing XP ImageList Draw State options
        // /// </summary>
        public enum ILS
        {
            NORMAL = 0x0,      // The image state is not modified.
            GLOW = 0x1,        // The color for the glow effect is passed to the IImageList::Draw method in the crEffect member of IMAGELISTDRAWPARAMS. 
            SHADOW = 0x2,      // The color for the drop shadow effect is passed to the IImageList::Draw method in the crEffect member of IMAGELISTDRAWPARAMS. 
            SATURATE = 0x4,    // The amount to increase is indicated by the frame member in the IMAGELISTDRAWPARAMS method. 
            ALPHA = 0x8       // The value of the alpha channel is indicated by the frame member in the IMAGELISTDRAWPARAMS method. The alpha channel can be from 0 to 255 with 0 being completely transparent and 255 being completely opaque. 
        }
        #endregion

        #region    SLR --- IShellLink.Resolve Flags
        [Flags()]
        public enum SLR
        {
            NO_UI = 0x1,
            ANY_MATCH = 0x2,
            UPDATE = 0x4,
            NOUPDATE = 0x8,
            NOSEARCH = 0x10,
            NOTRACK = 0x20,
            NOLINKINFO = 0x40,
            INVOKE_MSI = 0x80,
            NO_UI_WITH_MSG_PUMP = 0x101
        }
        #endregion

        #region    SLGP --- IShellLink.GetPath Flags
        [Flags()]
        public enum SLGP
        {
            SHORTPATH = 0x1,
            UNCPRIORITY = 0x2,
            RAWPATH = 0x4
        }
        #endregion

        #region    SHGNLI -- SHGetNewLinkInfo flags
        [Flags()]
        public enum SHGNLI
        {
            PIDL = 1,        // pszLinkTo is a pidl
            PREFIXNAME = 2,  // Make name "Shortcut to xxx"
            NOUNIQUE = 4,    // don't do the unique name generation
            NOLNK = 8       // don't add ".lnk" extension (Win2k or higher,IE5 or higher)
        }

        #endregion

        // Indicate whether the method should try to return a name in the pwcsName member of the STATSTG structure
        [Flags()]
        public enum STATFLAG
        {
            DEFAULT = 0,
            NONAME = 1,
            NOOPEN = 2
        }

        // Indicate the type of locking requested for the specified range of bytes
        [Flags()]
        public enum LOCKTYPE
        {
            WRITE = 1,
            EXCLUSIVE = 2,
            ONLYONCE = 4
        }

        // Used in the type member of the STATSTG structure to indicate the type of the storage element
        public enum STGTY
        {
            STORAGE = 1,
            STREAM = 2,
            LOCKBYTES = 3,
            PROPERTY = 4
        }

        // Indicate conditions for creating and deleting the object and access modes for the object
        [Flags()]
        public enum STGM
        {
            DIRECT = 0x0,
            TRANSACTED = 0x10000,
            SIMPLE = 0x8000000,
            READ = 0x0,
            WRITE = 0x1,
            READWRITE = 0x2,
            SHARE_DENY_NONE = 0x40,
            SHARE_DENY_READ = 0x30,
            SHARE_DENY_WRITE = 0x20,
            SHARE_EXCLUSIVE = 0x10,
            PRIORITY = 0x40000,
            DELETEONRELEASE = 0x4000000,
            NOSCRATCH = 0x100000,
            CREATE = 0x1000,
            CONVERT = 0x20000,
            FAILIFTHERE = 0x0,
            NOSNAPSHOT = 0x200000,
            DIRECT_SWMR = 0x400000
        }

        // Indicate whether a storage element is to be moved or copied
        public enum STGMOVE
        {
            MOVE = 0,
            COPY = 1,
            SHALLOWCOPY = 2
        }

        // Specify the conditions for performing the commit operation in the IStorage::Commit and IStream::Commit methods
        [Flags()]
        public enum STGC
        {
            DEFAULT = 0,
            OVERWRITE = 1,
            ONLYIFCURRENT = 2,
            DANGEROUSLYCOMMITMERELYTODISKCACHE = 4,
            CONSOLIDATE = 8
        }

        // Directing the handling of the item from which you're retrieving the info tip text
        [Flags()]
        public enum QITIPF
        {
            DEFAULT = 0x0,
            USENAME = 0x1,
            LINKNOTARGET = 0x2,
            LINKUSETARGET = 0x4,
            USESLOWTIP = 0x8
        }

        #endregion

        #region    Context Menu Related Enums 

        [Flags()]
        public enum CLSCTX : uint
        {
            // Fields
            ALL = 23U,
            DISABLE_AAA = 32768U,
            ENABLE_AAA = 65536U,
            ENABLE_CODE_DOWNLOAD = 8192U,
            FROM_DEFAULT_CONTEXT = 131072U,
            INPROC = 3U,
            INPROC_HANDLER = 2U,
            INPROC_HANDLER16 = 32U,
            INPROC_SERVER = 1U,
            INPROC_SERVER16 = 8U,
            LOCAL_SERVER = 4U,
            NO_CODE_DOWNLOAD = 1024U,
            NO_CUSTOM_MARSHAL = 4096U,
            NO_FAILURE_LOG = 16384U,
            REMOTE_SERVER = 16U,
            RESERVED1 = 64U,
            RESERVED2 = 128U,
            RESERVED3 = 256U,
            RESERVED4 = 512U,
            RESERVED5 = 2048U,
            SERVER = 21U
        }

        public enum CMD
        {
            TILES = 100001,
            LARGEICON = 100002,
            LIST = 100003,
            DETAILS = 100004,
            THUMBNAILS = 100005,
            REFRESH = 100006,
            PASTE = 100007,
            PASTELINK = 100008,
            PROPERTIES = 100009,
            ARRANGEICONS = 100010,

            OPEN = 100011,
            OPEN_LOCATION = 100012,
            GOTO_LOCATION = 100013,
            COPY = 100014,
            CUT = 100015,
            DELETE = 100016,
            SELECT_ALL = 100017,
            COPY_NAME = 100018,
            COPY_FULL_PATH = 100019,
            EXPORT2EXCEL = 100020,
            LARGE_THUMBNAILS = 100021,
            EXTRA_LARGE_THUMBNAILS = 100022,

            SORT_BY_BASE = 101000
        }

        public enum MK
        {
            LBUTTON = 0x1,
            RBUTTON = 0x2,
            SHIFT = 0x4,
            CONTROL = 0x8,
            MBUTTON = 0x10,
            ALT = 0x20
        }

        public enum CMIC
        {
            HOTKEY = 0x20,
            ICON = 0x10,
            FLAG_NO_UI = 0x400,
            UNICODE = 0x4000,
            NO_CONSOLE = 0x8000,
            ASYNCOK = 0x100000,
            NOZONECHECKS = 0x800000,
            SHIFT_DOWN = 0x10000000,
            CONTROL_DOWN = 0x40000000,
            FLAG_LOG_USAGE = 0x4000000,
            PTINVOKE = 0x20000000
        }

        public enum SW
        {
            HIDE = 0,
            SHOWNORMAL = 1,
            SHOW = 5,
            SHOWDEFAULT = 10,
            SHOWMAXIMIZED = 3,
            SHOWMINIMIZED = 2,
            SHOWMINNOACTIVE = 7,
            SHOWNOACTIVATE = 4
        }


        [Flags()]
        public enum TPM
        {
            CENTERALIGN = 0x4,
            LEFTALIGN = 0x0,
            RIGHTALIGN = 0x8,
            BOTTOMALIGN = 0x20,
            TOPALIGN = 0x0,
            VCENTERALIGN = 0x10,
            NONOTIFY = 0x80,
            RETURNCMD = 0x100,
            LEFTBUTTON = 0x0,
            RIGHTBUTTON = 0x2
        }


        [Flags()]
        public enum CMF
        {
            NORMAL = 0x0,
            DEFAULTONLY = 0x1,
            VERBSONLY = 0x2,
            EXPLORE = 0x4,
            NOVERBS = 0x8,
            CANRENAME = 0x10,
            NODEFAULT = 0x20,
            INCLUDESTATIC = 0x40,
            ITEMMENU = 0x80,
            EXTENDEDVERBS = 0x100,
            DISABLEDVERBS = 0x200,
            ASYNCVERBSTATE = 0x400,
            OPTIMIZEFORASYNC = 0x800,
            SYNCCASCADEMENU = 0x1000,
            DONOTPICKDEFAULT = 0x2000,
            RESERVED = unchecked((int)0xFFFF0000)
        }


        [Flags()]
        public enum GCS
        {
            VERBA = 0,
            HELPTEXTA = 1,
            VALIDATEA = 2,
            VERBW = 4,
            HELPTEXTW = 5,
            VALIDATEW = 6
        }


        [Flags()]
        public enum MFT
        {
            GRAYED = 0x3,
            DISABLED = 0x3,
            CHECKED = 0x8,
            SEPARATOR = 0x800,
            RADIOCHECK = 0x200,
            BITMAP = 0x4,
            OWNERDRAW = 0x100,
            MENUBARBREAK = 0x20,
            MENUBREAK = 0x40,
            RIGHTORDER = 0x2000,
            BYCOMMAND = 0x0,
            BYPOSITION = 0x400,
            POPUP = 0x10
        }


        [Flags()]
        public enum MIIM
        {
            BITMAP = 0x80,
            CHECKMARKS = 0x8,
            DATA = 0x20,
            FTYPE = 0x100,
            ID = 0x2,
            STATE = 0x1,
            STRING = 0x40,
            SUBMENU = 0x4,
            TYPE = 0x10
        }

        [Flags()]
        public enum SHCNE : uint
        {
            RENAMEITEM = 0x1,
            CREATE = 0x2,
            DELETE = 0x4,
            MKDIR = 0x8,
            RMDIR = 0x10,
            MEDIAINSERTED = 0x20,
            MEDIAREMOVED = 0x40,
            DRIVEREMOVED = 0x80,
            DRIVEADD = 0x100,
            NETSHARE = 0x200,
            NETUNSHARE = 0x400,
            ATTRIBUTES = 0x800,
            UPDATEDIR = 0x1000,
            UPDATEITEM = 0x2000,
            SERVERDISCONNECT = 0x4000,
            UPDATEIMAGE = 0x8000,
            DRIVEADDGUI = 0x10000,
            RENAMEFOLDER = 0x20000,
            FREESPACE = 0x40000,
            EXTENDED_EVENT = 0x4000000,
            ASSOCCHANGED = 0x8000000,
            INTERRUPT = 0x80000000,
            DISKEVENTS = 0x2381F,
            GLOBALEVENTS = 0xC0581E0,
            ALLEVENTS = 0x7FFFFFFF,
        }


        [Flags()]
        public enum SHCNF
        {
            IDLIST = 0x0,
            FLUSH = 0x1000
        }

        [Flags()]
        public enum SHCNRF : uint
        {
            InterruptLevel = 0x1,
            ShellLevel = 0x2,
            RecursiveInterrupt = 0x1000,
            NewDelivery = 0x8000
        }

        #endregion

        #region    Drag/Drop Related Enums

        #region            CLIPFORMAT Enum
        public enum CF
        {
            TEXT = 1,
            BITMAP = 2,
            METAFILEPICT = 3,
            SYLK = 4,
            DIF = 5,
            TIFF = 6,
            OEMTEXT = 7,
            DIB = 8,
            PALETTE = 9,
            PENDATA = 10,
            RIFF = 11,
            WAVE = 12,
            UNICODETEXT = 13,
            ENHMETAFILE = 14,
            HDROP = 15,
            LOCALE = 16,
            MAX = 17,
            OWNERDISPLAY = 0x80,
            DSPTEXT = 0x81,
            DSPBITMAP = 0x82,
            DSPMETAFILEPICT = 0x83,
            DSPENHMETAFILE = 0x8E,
            PRIVATEFIRST = 0x200,
            PRIVATELAST = 0x2FF,
            GDIOBJFIRST = 0x300,
            GDIOBJLAST = 0x3FF
        }
        #endregion

        #region            ADVF Enum
        [Flags()]
        public enum ADVF
        {
            NODATA = 1,
            PRIMEFIRST = 2,
            ONLYONCE = 4,
            DATAONSTOP = 64,
            CACHE_NOHANDLER = 8,
            CACHE_FORCEBUILTIN = 16,
            CACHE_ONSAVE = 32
        }
        #endregion

        #region            DVASPECT Enum
        [Flags()]
        public enum DVASPECT
        {
            CONTENT = 1,
            THUMBNAIL = 2,
            ICON = 4,
            DOCPRINT = 8
        }
        #endregion

        #region            TYMED Enum
        [Flags()]
        public enum TYMED
        {
            HGLOBAL = 1,
            FILE = 2,
            ISTREAM = 4,
            ISTORAGE = 8,
            GDI = 16,
            MFPICT = 32,
            ENHMF = 64,
            NULL = 0
        }
        #endregion

        #endregion

        #region    Thumbnail Related Enums
        [Flags()]
        public enum IEIFLAG
        {
            ASYNC = 0x1,     // ask the extractor if it supports ASYNC extract (free threaded)
            CACHE = 0x2,      // returned from the extractor if it does NOT cache the thumbnail
            ASPECT = 0x4,      // passed to the extractor to beg it to render to the aspect ratio of the supplied rect
            OFFLINE = 0x8,     // if the extractor shouldn't hit the net to get any content needed for the rendering
            GLEAM = 0x10,     // does the image have a gleam ? this will be returned if it does
            SCREEN = 0x20,      // render as if for the screen  (this is exlusive with IEIFLAG_ASPECT )
            ORIGSIZE = 0x40,      // render to the approx size passed, but crop if neccessary
            NOSTAMP = 0x80,      // returned from the extractor if it does NOT want an icon stamp on the thumbnail
            NOBORDER = 0x100,      // returned from the extractor if it does NOT want an a border around the thumbnail
            QUALITY = 0x200      // passed to the Extract method to indicate that a slower, higher quality image is desired, re-compute the thumbnail
        }

        /// <summary>
        /// Flags for IShellItemImageFactory::GetImage
        /// </summary>
        [Flags]
        public enum SIIGBF : uint
        {
            RESIZETOFIT = 0x00000000, // Shrink as needed, preserve aspect ratio
            BIGGERSIZEOK = 0x00000001, // Caller may stretch returned image
            MEMORYONLY = 0x00000002, // Return only if already in memory
            ICONONLY = 0x00000004, // Icon only, never thumbnail
            THUMBNAILONLY = 0x00000008, // Thumbnail only, never icon
            INCACHEONLY = 0x00000010, // Only cached results
            CROPTOSQUARE = 0x00000020, // Windows 8+: crop to square
            WIDETHUMBNAILS = 0x00000040, // Windows 8+: crop/stretch to 0.7 aspect ratio
            ICONBACKGROUND = 0x00000080, // Windows 8+: paint icon background color
            SCALEUP = 0x00000100  // Windows 8+: scale up to requested size
        }

        #endregion

    }

    /// <summary>
    /// Pidl basing options
    /// </summary>
    [Flags]
    public enum SIGDN : uint
    {
        NORMALDISPLAY = 0x00000000,
        PARENTRELATIVEPARSING = 0x80018001,
        DESKTOPABSOLUTEPARSING = 0x80028000,
        PARENTRELATIVEEDITING = 0x80031001,
        DESKTOPABSOLUTEEDITING = 0x8004C000,
        FILESYSPATH = 0x80058000,
        URL = 0x80068000,
        PARENTRELATIVEFORADDRESSBAR = 0x8007C001,
        PARENTRELATIVE = 0x80080001,
        PARENTRELATIVEFORUI = 0x80094001
    }



}