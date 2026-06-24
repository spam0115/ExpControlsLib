using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{

    /// <summary>
    /// CShellItem is the <b>Primary Class</b> in WindowsApiLib. It is a superset of the .Net Classes FileInfo/DirectoryInfo. CShellItem and its' supporting
    /// Classes provide all the functionality of the .Net Classes as well as Change Notification, correct Icons for all Items, support for 
    /// non-FileSystem Items, and obtains most information about Items more rapidly than the .Net Classes.
    /// </summary>
    /// <remarks>Creates and maintains an internal cache of Directories and Files that the calling application has expressed an interest in.
    ///          The calling application is responsible for explicitly discarding elements from the cache when it no longer has an interest
    ///          in them. Normal usage is to retain all Directory entries but explicitly discard the file entries of Directories that are
    ///          no longer of interest. 
    /// </remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CShellItem : IDisposable, IComparable
    {

        #region    Shared Private Fields

        // This class has occasion to refer to the TypeName as reported by
        // SHGetFileInfo. It needs to compare this to the string
        // (in English) "System Folder"
        // on non-English systems, we do not know, in the general case,
        // what the equivalent string is to compare against
        // The following variable is set by Sub New() to the string that
        // corresponds to "System Folder" on the current machine
        // Sub New() depends on the existance of My Computer(CSIDL.DRIVES),
        // to determine what the equivalent string is
        //private static string m_strSystemFolder;

        // My Computer is also commonly used (though not internally),
        // so save & expose its name on the current machine
        //private static string m_strMyComputer;

        // To get My Documents sorted first, we need to know the Locale 
        // specific name of that folder.
        //private static string m_strMyDocuments;

        //// The DesktopBase is set up via Sub New() (one time only) and
        //// disposed of only when DesktopBase is finally disposed of
        //private static CShellItem DesktopBase;

        // DragDrop, possibly among others, needs to know the Path of
        // the DeskTopDirectory in addition to the Desktop itself
        // Also need the actual CShellItem for the DeskTopDirectory, so get it
        //private static CShellItem m_DeskTopDirectory;

        /// <summary>
        /// The CShellItem of the Recycle Bin. Set in New() (the Desktop creator)
        /// Used to prevent UPDATEDIR on this Item from processing.
        /// Als used to prevent normal UPDATEDIR on Desktop from processing the
        /// Recycle Bin which would cause an effectively endless loop.
        /// </summary>
        //private static CShellItem m_Recycle;            // 6/21/2012

        // Keep the local System Name for IsRemote testing
        // private static string SystemName;                              // 4/14/2012

        /// Keep list of Drives and their DriveType for IsRemote testing
        private static readonly Dictionary<string, bool> DriveDict = new Dictionary<string, bool>();   // 4/16/2012

        /// <summary>
        /// LockObj is used for locking critical updating blocks of code
        /// that reference the Shared Directory Tree of CShItems.  In the
        /// normal case, it will not actually lock the block of code since
        /// Most (all?) updating is done in the main thread. HOWEVER, empirical evidence
        /// suggests that if multiple code paths of the SAME thread are in play 
        /// as a byproduct of overriding WndProc for Notification messages, the
        /// SyncLock LockObj statement will allow pending messages to be processed.
        /// This effectively causes messages to be processed in reverse order of receipt.
        /// This is a bit funky, but is at least made predictible by the SyncLock.
        /// </summary>
        /// <remarks></remarks>
        private static readonly object _itemTreeLock = new object(); //todo: stop using this and use locks on the instances of m_directories and m_files instead

        #endregion

        #region    Instance Private Fields
        // m_Folder and m_Pidl must be released/freed at Dispose time
        internal IntPtr m_Pidl;            // The Absolute PIDL for this item (not retained for files)
        //internal IShellFolder? m_IShellFolder = null;    // if item is a folder, contains the Folder interface for this instance.  Had to remove this because it has sta thread affinity and would throw exceptions when used in a multithreaded sta environment
        internal CShellItem? m_Parent = null;
        internal string m_DisplayName = "";
        internal string? m_FullPath = null;
        internal string? m_TypeName = null;
        internal int m_IconIndexNormal = -1;        // index into the SystemImageListManager list for Normal icon
        internal int m_IconIndexOpen = -1;          // index into the SystemImageListManager list for Open icon
        internal int m_IconIndexNormalOrig = -1;    // index into the System Image list for Normal icon
        internal int m_IconIndexOpenOrig = -1;      // index into the SystemImage list for Open icon
        internal bool m_IsBrowsable;
        internal bool m_IsFileSystem;
        internal bool m_IsFolder;
        internal bool? m_HasSubFolders;
        internal bool m_IsLink;
        internal bool m_IsDisk;
        internal bool m_IsShared;
        internal bool m_IsHidden;
        internal bool m_IsNetWorkDrive;
        internal bool m_IsRemovable;
        internal bool m_IsReadOnly;
        // Properties of interest to Drag Operations
        internal bool m_CanMove;
        internal bool m_CanCopy;
        internal bool m_CanDelete;
        internal bool m_CanLink;
        internal bool m_CanRename;

        internal FileAttributes m_Attributes;  // True FileAttributes from FileInfo
        internal SFGAO m_SFGAO_Attributes;
        internal bool m_IsRemote;

        internal W32Find_Data? m_W32Data;

        internal int m_SortFlag;       // Used in comparisons

        //// For shell events 
        //internal CShellItemUpdater m_updater;

        // The following elements are only filled in on demand
        internal bool m_XtrInfo;
        internal DateTime m_LastWriteTime;
        internal DateTime m_CreationTime;
        internal DateTime m_LastAccessTime;
        internal long m_Length;

        // Indicates whether DisplayName, TypeName, SortFlag have been set up
        internal bool m_HasDispType;

        // Indicates whether IsReadOnly has been set up
        internal bool m_IsReadOnlySetup;

        // m_UpdateFolder is True if the IShellFolder (m_Folder) must be refetched
        internal bool m_UpdateFolder;

        // Holds a byte() representation of m_PIDL -- filled when needed
        internal CPidl m_cPidl;

        // Flags for Dispose state
        // Private m_IsDisposing As Boolean
        internal bool m_Disposed;

        //internal Dictionary<uint, CShellItem>? m_ChildrenDic = null; //not used currently but maybe useful in the future.

        #endregion

        #region    Properties


        #region Private properties

        private bool UpdateFolder
        {
            get => m_UpdateFolder;
            set => m_UpdateFolder = value;
        }

        private int SortFlag
        {
            get
            {
                //if (!m_HasDispType)
                //    SetDispType();
                return m_SortFlag;
            }
        }

        #region            IconIndex properties

        /// <summary>
        /// The Index of the "normal" Icon into the list maintained by SystemImageListManager and
        /// used for the IconIndex in ListViewItems and TreeNodes.
        /// </summary>
        /// <value></value>
        /// <returns>The "normal" IconIndex as used by ListViewItems and TreeNodes</returns>
        /// <remarks></remarks>
        public int IconIndexNormal
        {
            get
            {
                if (m_IconIndexNormal < 0)
                {
                    //if (!m_HasDispType)
                    //    SetDispType();
                    m_IconIndexNormal = SystemImageListManager.GetIconIndex(this);
                }
                return m_IconIndexNormal;
            }
        }

        /// <summary>
        /// The Index of the "Open" Icon into the list maintained by SystemImageListManager and
        /// used for the IconIndex in ListViewItems and TreeNodes.
        /// </summary>
        /// <value></value>
        /// <returns>The "Open" IconIndex as used by ListViewItems and TreeNodes</returns>
        /// <remarks></remarks>
        public int IconIndexOpen
        {
            get
            {
                if (m_IconIndexOpen < 0)
                {
                    //if (!m_HasDispType)
                    //    SetDispType();
                    if (!m_IsDisk && m_IsFileSystem && m_IsFolder)
                    {
                        m_IconIndexOpen = SystemImageListManager.GetIconIndex(this, true);
                    }
                    else
                    {
                        m_IconIndexOpen = m_IconIndexNormal;
                    }
                }
                return m_IconIndexOpen;
            }
            set;
        }

        /// <summary>
        /// Should not be directly referenced by the application.<br />
        /// Contains the base IconIndex of the "normal" Icon in the System ImageList 
        /// as returned by SHGetFileInfo
        /// </summary>
        /// <returns>The IconIndex into the System ImageList as returned by SHGetFileInfo</returns>
        /// <remarks>This is not the IconIndex returned by SystemImageListManager. It is the
        ///          IconIndex that is passed to SystemImageListManager to obtain the true index
        ///          into the per process System Image List. In most, but not all cases, the two
        ///          values are the same.</remarks>
        internal int IconIndexNormalOrig
        {
            get
            {
                if (m_IconIndexNormalOrig < 0)
                {
                    //if (!m_HasDispType)
                    //    SetDispType();
                    var shfi = new SHFILEINFO();
                    var dwflag = SHGFI.PIDL | SHGFI.SYSICONINDEX;
                    int dwAttr = 0;
                    if (m_IsFileSystem && !m_IsFolder)
                    {
                        dwflag = dwflag | SHGFI.USEFILEATTRIBUTES;
                        dwAttr = FILE_ATTRIBUTE_NORMAL;
                    }
                    var H = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, SHFILEINFO_size, dwflag);
                    m_IconIndexNormalOrig = shfi.iIcon;
                    if (m_IconIndexNormal < 0)
                        m_IconIndexNormal = SystemImageListManager.GetIconIndex(this);
                }
                return m_IconIndexNormalOrig;
            }
        }

        /// <summary>
        /// Should not be directly referenced by the application.<br />
        /// The base IconIndex of the "Open" image in the System Image List.
        /// </summary>
        /// <returns>The base IconIndex of the "Open" image as returned by SHGetFileInfo</returns>
        /// <remarks>On at least Win7 systems, the "open" Icon is the same as the "normal" Icon.</remarks>
        internal int IconIndexOpenOrig
        {
            get
            {
                if (m_IconIndexOpenOrig < 0)
                {
                    //if (!m_HasDispType)
                    //    SetDispType();
                    if (!m_IsDisk && m_IsFileSystem && m_IsFolder)
                    {
                        var dwflag = SHGFI.SYSICONINDEX | SHGFI.PIDL;
                        var shfi = new SHFILEINFO();
                        var H = SHGetFileInfo(m_Pidl, 0, ref shfi, SHFILEINFO_size, dwflag | SHGFI.OPENICON);
                        m_IconIndexOpenOrig = shfi.iIcon;
                        if (m_IconIndexOpen < 0)
                            m_IconIndexOpen = SystemImageListManager.GetIconIndex(this, true);
                    }
                    else
                    {
                        m_IconIndexOpenOrig = m_IconIndexNormalOrig;
                    }
                }
                return m_IconIndexOpenOrig;
            }
        }


        #endregion

        #endregion


        #region Public Properties

        /// <summary>
        /// Property used to store information returned by FindFirstFile/FindNextFile API call.
        /// </summary>
        /// <returns>The current value or Nothing if not set</returns>
        /// <remarks>Used to optimize the fetching of information otherwise only easily available from FileInfo/DirectoryInfo.</remarks>
        public W32Find_Data? W32Data
        {
            get
            {
                return m_W32Data;
            }
            set //this is never called
            {
                m_W32Data = value;
            }
        }

        /// <summary>
        /// Database ID
        /// </summary>
        public long ID { get; set; }

        /// <summary>
        /// Associated listview item
        /// </summary>
        public ListViewItem? LVItem { get; set; }

        /// <summary>
        /// Associated treeview item
        /// </summary>
        public TreeNode? TNode { get; set; }

        /// <summary>
        /// An Object which may used to store custom information
        /// </summary>
        /// <returns>The object provided by the consumer</returns>
        /// <remarks>
        /// Property may be used for any application defined purpose.
        /// </remarks>
        public object Tag { get; set; }

        /// <summary>
        /// The Name of the File or Directory. If a Special Folder, then the Windows name for that Special Folder
        /// </summary>
        /// <returns>The Name of the File or Directory. If a Special Folder, then the Windows name for that Special Folder</returns>
        /// <remarks>For a link file (xxx.txt.lnk for example) the
        /// DisplayName property will return xxx.txt</remarks>
        public string DisplayName
        {
            get
            {
                return m_DisplayName;
            }
        }

        /// <summary>
        /// An alternate way of obtaining the DisplayName
        /// </summary>
        /// <returns>The DisplayName</returns>
        /// <remarks>For a link file (xxx.txt.lnk for example) the
        /// DisplayName property will return xxx.txt</remarks>
        public string Text
        {
            get
            {
                return m_DisplayName;
            }
        }

        /// <summary>
        /// The Windows TypeName (eg "Text File")
        /// </summary>
        /// <returns>The Windows TypeName</returns>
        public string TypeName
        {
            get
            {
                return m_TypeName;
            }
            set;
        }

        /// <summary>
        /// Contains the full PIDL for the current instance as an IntPtr
        /// </summary>
        public IntPtr PIDL => m_Pidl;

        private IntPtr m_lastPidl = IntPtr.Zero;
        /// <summary>
        /// Contains the final SHITEMID from the PIDL property
        /// </summary>
        public IntPtr LastPIDL
        {
            get
            {
                if (m_Pidl == IntPtr.Zero) return IntPtr.Zero;
                if (m_lastPidl == IntPtr.Zero)
                {
                    m_lastPidl = CPidl.ILFindLastID(m_Pidl);
                }

                return m_lastPidl;
            }
        }

        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        //        public IShellFolder IShlFolder
        //        {
        //            get
        //            {
        //#if DEBUG
        //                var name = ShellHelper.GetShellFolderDisplayName(m_IShellFolder);
        //#endif
        //                if (m_IsFolder && m_UpdateFolder)
        //                {
        //                    if (m_IShellFolder is not null)
        //                        Marshal.ReleaseComObject(m_IShellFolder);
        //                    m_IShellFolder = ShellHelper.GetIShellFolder(Parent, ILFindLastID(m_Pidl));
        //                    m_UpdateFolder = false;
        //                }
        //                return m_IShellFolder;
        //            }
        //        }

        /// <summary>
        /// Contains the Full Path and file name of the instance as obtained from Folder.GetDisplayNameOf
        /// </summary>
        public string FullPath
        {
            get
            {
                if (m_FullPath is null)
                {
                    m_FullPath = CShellItemFactory.GetFullPath(this);
                }
                return m_FullPath;
            }
        }

        /// <summary>
        /// Contains the Full Path of the instance as obtained by traversing the internal cache's tree structure.
        /// </summary>
        /// <remarks>Useful for items located on certain removable drives not handled well by Folder.GetDisplayNameOf.</remarks>
        public string ItemPath
        {
            get
            {
                var item = this;
                var pathlist = new List<CShellItem>() { item };  // pathlist.Add(item)
                while (item.Parent is not null)
                {
                    pathlist.Add(item.Parent);
                    item = item.Parent;
                }
                pathlist.Reverse();
                var SB = new StringBuilder();
                foreach (CShellItem N in pathlist)
                {
                    SB.Append(N.DisplayName);
                    SB.Append(@"\");
                }
                return SB.ToString();
            }
        }

        private CShellItemCollection? _directories;
        internal readonly object _directoriesLock = new object(); //need this because m_directories is sometimes null

        /// <summary>
        /// Whether or not the child folders collection is populated.
        /// </summary>
        public bool DirectoriesInitialized => (_directories is not null); 
        internal DateTime? DirsCollectionTimestamp;

        /// <summary>
        /// Returns the number of Folders currently known to this instance. If not
        /// initialized, return 0
        /// </summary>
        /// <returns>The number of Folders currently known to this instance. If not
        /// initialized, return 0</returns>
        /// <remarks>Property added 02/10/2014 to avoid UpdateRefresh</remarks>
        public int DirCount => DirectoriesInitialized ? _directories.Count : 0;

        public CShellItemCollection? Directories
        {
            get
            {
                if (!m_IsFolder) //only folders have child elements
                {
                    return null;
                }

                if (_directories == null)
                {
                    lock (_directoriesLock)
                    {
                        if (_directories == null)
                        {
                            var directories = CShellItemFactory.GetContents(this, SHCONTF.FOLDERS | SHCONTF.INCLUDEHIDDEN); //todo: change the hidden handling
                            _directories = new CShellItemCollection(this, directories);
                            DirsCollectionTimestamp = DateTime.Now;
                        }
                    }
                }
                else
                {
                    // Under some circumstances, Windows does not post MKDIR msgs when Folders are created!!! Do a refresh to ensure we are up to date
                    // otherwise the rename function doesn't work, but after comment, it will affects tree updating, however performance is better
                    // Me.UpdateRefresh(False, True)   '6/30/2012 - Note that it is also true that in some circumstances Windows does not post a RMDIR when Folders are removed.
                }

                return _directories;
            }
            set
            {
                _directories = value;
                DirsCollectionTimestamp = DateTime.Now;
            }
        }

        /// <summary>
        /// Returns an Array of CShItems containing the sub Directories of this instance.
        /// </summary>
        /// <returns>Array of CShItems containing the sub Directories of this instance.</returns>
        public List<CShellItem>? DirectoriesList
        {
            get
            {
                if (!m_IsFolder)
                {
                    return null;
                }

                if (_directories != null)
                {
                    return _directories.Items;
                }
                else
                {
                    return null;
                }
            }
        }


        private CShellItemCollection? _files;
        internal readonly object _filesLock = new object(); //need this because m_directories is sometimes null

        /// <summary>
        /// Whether or not the child files collection is populated.
        /// </summary>
        public bool FilesInitialized => (_files is not null);

        /// <summary>
        /// Returns the number of Files currently known to this instance. If not
        /// initialized, return 0
        /// </summary>
        /// <returns>The number of Files currently known to this instance. If not
        /// initialized, return 0</returns>
        /// <remarks>Property added 02/10/2014 to avoid UpdateRefresh</remarks>
        public int FileCount => FilesInitialized ? _files.Count : 0;
        internal DateTime? FilesCollectionTimestamp;

        public CShellItemCollection? Files
        {
            get
            {
                if (!m_IsFolder) //only folders have child elements
                {
                    return null;
                }

                if (_files == null)
                {
                    lock (_filesLock)
                    {
                        if (_files == null)
                        {
                            Debug.WriteLine("CShellItem.Files: GetContents m_files for " + FullPath);
                            var files = CShellItemFactory.GetContents(this, SHCONTF.NONFOLDERS | SHCONTF.INCLUDEHIDDEN);
                            _files = new CShellItemCollection(this, files);
                            FilesCollectionTimestamp = DateTime.Now;
                        }
                    }
                }
                else
                {
                    //Under some circumstances, Windows does not post CREATE or DELETE msgs when Files are created!!! Do a refresh to ensure we are up to date
                    //{
                    //    UpdateRefresh(true, false); //infinite loop
                    //} 
                }

                return _files;
            }
            set
            {
                Debug.WriteLine("CShellItem.Files: Setting m_files for " + FullPath);
                _files = value;
                FilesCollectionTimestamp = DateTime.Now;
            }
        }

        /// <summary>
        /// Returns an Array of CShItems containing the Files contained in this instance.
        /// </summary>
        /// <returns>Array of CShItems containing the Files contained in this instance.</returns>
        public List<CShellItem>? FilesList
        {
            get
            {
                if (!m_IsFolder) //only folders have child elements
                {
                    return null;
                }

                if (_files != null)
                {
                    return _files.Items;
                }
                else return null;
            }
        }

        internal void ClearCaches()
        {
            if (_files is not null)
                _files.ClearCaches();
            if (_directories is not null)
                _directories.ClearCaches();
            //if (m_ChildrenDic is not null)
            //    m_ChildrenDic.Clear();
        }


        /// <summary>
        /// Contains the CShellItem of this instance's Parent Folder
        /// </summary>
        /// <returns>CShellItem of this instance's Parent Folder</returns>
        /// <remarks>Returns Nothing for the Desktop which has no Parent</remarks>
        public CShellItem? Parent
        {
            get
            {
                //if (m_Parent is null) //for the desktop, parent is supposed to be null since it has no parent.
                //{
                //    m_Parent = CShellItemFactory.GetCShItem(CPidl.TrimLast(m_Pidl));
                //}
                //for the desktop, parent is supposed to be null
                return m_Parent;
            }
            set
            {
                //parent is set before display name so this doesn't really work
                //Debug.WriteLine($"CShellItem.Parent setter: Setting parent of '{DisplayName}' to '{value?.DisplayName ?? "null"}'");
                m_Parent = value;
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        public void SetParent(CShellItem parent)
        {
            Parent = parent;
        }

        /// <summary>
        /// This instance's Shell Attributes as returned by Folder.GetAttributesOf
        /// </summary>
        /// <returns>This instance's Shell Attributes as returned by Folder.GetAttributesOf</returns>
        /// <remarks>Internal use only</remarks>
        public SFGAO SFGAO_Attributes        // Change 10/09/2011
        {
            get
            {
                return m_SFGAO_Attributes;
            }
        }

        /// <summary>
        /// True if instance is Browsable, False otherwise
        /// </summary>
        /// <returns>True if instance is Browsable, False otherwise</returns>
        /// <remarks>See MSDN for definition of "Browsable"</remarks>
        public bool IsBrowsable
        {
            get
            {
                return m_IsBrowsable;
            }
        }

        /// <summary>
        /// True if instance is a File System item
        /// </summary>
        /// <returns>True if instance is a File System item</returns>
        /// <remarks>Numerous Virtual and/or Shell Extension Folders and their content are not members of the File System</remarks>
        public bool IsFileSystem
        {
            get
            {
                return m_IsFileSystem;
            }
        }

        /// <summary>True if instance is a Folder, False otherwise
        /// </summary>
        /// <returns>True if instance is a Folder, False otherwise</returns>
        /// <remarks>Numerous Virtual and/or Shell Extension Folders are not members of the File System</remarks>
        public bool IsFolder
        {
            get
            {
                return m_IsFolder;
            }
        }

        /// <summary>
        /// True if item is a Folder and has sub-Folders
        /// </summary>
        /// <returns>True if item is a Folder and has sub-Folders, False otherwise</returns>
        /// <remarks>Modified to make this attribute behave (with respect to Remote Folders) like XP, even on Vista/Win7.
        /// That is, any Remote Folder is reported as HasSubFolders = True. Local Folders are tested with the API call.
        /// On Vista/Win7, Compressed files (eg - .Zip, .Cab, etc) are considered sub Folders by this Property.
        /// This behavior is NOT modified to behave like XP.</remarks>
        public bool HasSubFolders
        {
            get
            {
                if (m_HasSubFolders != null)
                {
                    return m_HasSubFolders.Value;
                }
                else if (m_IsRemote)
                {
                    m_HasSubFolders = true;
                }
                else
                {
                    m_HasSubFolders = HasAtLeastOneSubfolder();
                    if (m_HasSubFolders.Value) m_SFGAO_Attributes |= SFGAO.HASSUBFOLDER; //set flag
                    else m_SFGAO_Attributes &= ~SFGAO.HASSUBFOLDER; //unset flag
                }
                return m_HasSubFolders.Value;
            }
            set;
        }

        /// <summary>
        /// True if this instance is a Disk like device, False otherwise
        /// </summary>
        /// <returns>True if this instance is a Disk like device, False otherwise</returns>
        public bool IsDisk => m_IsDisk;

        /// <summary>
        /// True if this instance is a Link (.lnk or Shortcut), False otherwise
        /// </summary>
        /// <returns>True if this instance is a Link (.lnk or Shortcut), False otherwise</returns>
        public bool IsLink => m_IsLink;

        /// <summary>
        /// True if this instance is Shared, False otherwise
        /// </summary>
        /// <returns>True if this instance Shared, False otherwise</returns>
        public bool IsShared => m_IsShared;

        /// <summary>
        /// True if this instance is Hidden, False otherwise
        /// </summary>
        /// <returns>True if this instance Hidden, False otherwise</returns>
        public bool IsHidden => m_IsHidden;

        /// <summary>
        /// True if this instance is a Removable device, False otherwise
        /// </summary>
        /// <returns>True if this instance is a Removable device, False otherwise</returns>
        public bool IsRemovable => m_IsRemovable;

        /// <summary>
        /// Returns True if this CShellItem represents a Folder/File stored on a Remote system
        /// </summary>
        /// <returns>Returns True if this CShellItem represents a Folder/File stored on a Remote system, False otherwise.</returns>
        /// <remarks>
        /// A Remote item is any item whose path is a UNC not referring to the Local system or
        /// resides on a Mapped (Network) Drive. Set up in SetupAttributes.
        /// </remarks>
        public bool IsRemote => m_IsRemote;

        /// <summary>
        /// True if this instance can be Renamed, False otherwise
        /// </summary>
        /// <returns>True if this instance can be Renamed, False otherwise</returns>
        public bool CanRename => m_CanRename;

        private string m_size = "[]";
        private string currentPath;

        /// <summary>
        /// A Formatted String representation of the Item's FileSize
        /// </summary>
        /// <returns>A Formatted String representation of the Item's FileSize</returns>
        public string Size
        {
            get
            {
                if (m_size == "[]")
                {
                    GetSizeString();
                }
                return m_size;
            }
        }


        #region Drag Ops Properties

        /// <summary>
        /// Returns True if instance may be Moved, False otherwise.
        /// </summary>
        /// <returns>True if instance may be Moved, False otherwise.</returns>
        public bool CanMove => m_CanMove;

        /// <summary>
        /// Returns True if instance can be Copied, False otherwise
        /// </summary>
        /// <returns>True if instance can be Copied, False otherwise</returns>
        public bool CanCopy => m_CanCopy;

        /// <summary>
        /// Returns True if instance can be Deleted, False otherwise
        /// </summary>
        /// <returns>True if instance can be Deleted, False otherwise</returns>
        public bool CanDelete => m_CanDelete;

        /// <summary>
        /// Returns True if instance can be Linked to, False otherwise
        /// </summary>
        /// <returns>True if instance can be Linked to, False otherwise</returns>
        public bool CanLink => m_CanLink;

        /// <summary>
        /// Returns True if instance can be a Drop Target, False otherwise
        /// </summary>
        /// <returns>True if instance can be a Drop Target, False otherwise</returns>
        public bool IsDropTarget { get; set; }

        #endregion


        #region Shared public functions


        #region FileInfo derived Properties

        /// <summary>
        /// Contains the LastWriteTime (Last Modified) DateTime of this instance
        /// </summary>
        /// <returns>The LastWriteTime (Last Modified) DateTime of this instance</returns>
        /// <remarks>With other information, Filled by FillDemandInfo on first Get</remarks>
        public DateTime LastWriteTime
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_LastWriteTime;
            }
        }

        /// <summary>
        /// Contains the LastAccessTime DateTime of this instance
        /// </summary>
        /// <returns>The LastAccessTime DateTime of this instance</returns>
        /// <remarks>With other information, Filled by FillDemandInfo on first Get</remarks>
        public DateTime LastAccessTime
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_LastAccessTime;
            }
        }

        /// <summary>
        /// Contains the CreationTime DateTime of this instance
        /// </summary>
        /// <returns>The CreationTime DateTime of this instance</returns>
        /// <remarks>With other information, Filled by FillDemandInfo on first Get</remarks>
        public DateTime CreationTime
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_CreationTime;
            }
        }

        /// <summary>
        /// Contains the FileSize of this instance
        /// </summary>
        /// <returns>The FileSize of this instance</returns>
        /// <remarks>With other information, Filled by FillDemandInfo on first Get</remarks>
        public long Length
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_Length;
            }
        }

        /// <summary>
        /// Contains the FileAttributes of this instance
        /// </summary>
        /// <returns>The FileAttributes of this instance</returns>
        /// <remarks>This is the same information, formatted the same way, as found in FileInfo, GetAttr, etc.<br />
        ///          With other information, Filled by FillDemandInfo on first Get</remarks>
        public FileAttributes Attributes
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_Attributes;
            }
        }

        /// <summary>
        /// Returns True if instance is a Mapped (not Local) Drive, False otherwise
        /// </summary>
        /// <returns>True if instance is a Mapped (not Local) Drive, False otherwise</returns>
        /// <remarks>With other information, Filled by FillDemandInfo on first Get</remarks>
        public bool IsNetworkDrive
        {
            get
            {
                if (!m_XtrInfo)
                {
                    FillDemandInfo();
                }
                return m_IsNetWorkDrive;
            }
        }

        /// <summary>
        /// The CPidl representation of this instance's PIDL
        /// </summary>
        /// <returns>The CPidl representation of this instance's PIDL</returns>
        public CPidl ClsPidl
        {
            get
            {
                if (m_cPidl == null)
                {
                    m_cPidl = new CPidl(m_Pidl);
                }
                return m_cPidl;
            }
        }

        /// <summary>True if instance is ReadOnly, False otherwise</summary>
        /// <remarks>The IsReadOnly attribute causes an annoying access to any floppy drives
        /// on the system. To postpone this (or avoid, depending on user action),
        /// the attribute is only queried when asked for
        /// </remarks>
        public bool IsReadOnly
        {
            get
            {
                if (m_IsReadOnlySetup)
                {
                    return m_IsReadOnly;
                }
                else
                {
                    var shfi = new SHFILEINFO() { dwAttributes = SFGAO.READONLY };
                    var dwflag = SHGFI.PIDL | SHGFI.ATTRIBUTES | SHGFI.ATTR_SPECIFIED;
                    int dwAttr = 0;
                    var H = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, SHFILEINFO_size, dwflag);
                    if (H.ToInt32() != NOERROR && H.ToInt32() != 1)
                    {
                        Marshal.ThrowExceptionForHR(H.ToInt32());
                    }
                    m_IsReadOnly = (shfi.dwAttributes & SFGAO.READONLY) != 0;
                    m_SFGAO_Attributes = m_SFGAO_Attributes | shfi.dwAttributes & SFGAO.READONLY;
                    m_IsReadOnlySetup = true;
                    return m_IsReadOnly;
                }
            }
        }

        private bool _IsSystem_HaveSysInfo = default;
        private bool _IsSystem_m_IsSystem = default;
        public int ImageIndex = -1; //todo: store all the image indexes for all sizes and not just one at a time.

        /// <summary>True if this instance has been marked "System", False otherwise
        /// </summary>
        /// <returns>True if this instance has been marked "System", False otherwise</returns>
        /// <remarks>The IsSystem attribute is seldom used, but required by DragDrop operations.
        /// Since there is no way of getting ONLY the System attribute without getting
        /// the RO attribute (which forces a reference to the floppy drive), we pay
        /// the price of calling File.GetAttributes for this purpose alone.</remarks>
        public bool IsSystem
        {
            get   // true once we have gotten this attr
                  // the value of this attr once we have it
            {
                if (!_IsSystem_HaveSysInfo)
                {
                    try
                    {
                        _IsSystem_m_IsSystem = (File.GetAttributes(FullPath) & FileAttributes.System) == FileAttributes.System;
                        _IsSystem_HaveSysInfo = true;
                    }
                    catch (Exception ex)
                    {
                        _IsSystem_HaveSysInfo = true;
                    }
                }
                return _IsSystem_m_IsSystem;
            }
        }

        private Dictionary<string, ListViewSubitemData>? m_columnDic = null;

        public Dictionary<string, ListViewSubitemData> ColumnDic
        {
            get
            {
                if (m_columnDic == null)
                {
                    m_columnDic = new Dictionary<string, ListViewSubitemData>();
                }
                return m_columnDic;
            }
        }

        /// <summary>
        /// This indicates if the item has been updated since the last time it was consumed.
        /// Not currently in use but may be needed for the future.
        /// </summary>
        public bool NeedsRefresh = true;


        #endregion

        #endregion



        #region Public Properties


        #endregion

        #region Shared Properties
        /// <summary>
        /// Contains a String with the Local representation of "My Computer"
        /// </summary>
        //public static string StrMyComputer => m_strMyComputer;
        /// <summary>
        /// Contains a String with the Local representation of "System Folder".
        /// </summary>
        //public static string StrSystemFolder => m_strSystemFolder;
        /// <summary>
        /// Contains a String with the Full Path of the Desktop Directory
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        //public static string DesktopDirectoryPath => m_DeskTopDirectory?.FullPath;

        #endregion

        #endregion

        #endregion Properties

        #region    Constructors/Destructors

        /// <summary>
        /// Private Constructor. Creates CShellItem of the Desktop
        /// </summary>
        public CShellItem()
        {
        }

        public CShellItem(string path)
        {
            CShellItemFactory.PopulateCsiFromPath(this, path);
        }


        /// <summary>
        /// Summary of Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // Take yourself off of the finalization queue
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Deallocates CoTaskMem contianing m_Pidl and removes reference to m_Folder
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            // Allow your Dispose method to be called multiple times,
            // but throw an exception if the object has been disposed.
            // Whenever you do something with this class, 
            // check to see if it has been disposed.
            if (m_Disposed)
                return;

            m_Disposed = true;
            if (disposing)
            {
            }
            // Release unmanaged resources. If disposing is false,
            // only the following code is executed. 
            if (!m_Pidl.Equals(IntPtr.Zero))
            {
                Marshal.FreeCoTaskMem(m_Pidl);
                m_Pidl = IntPtr.Zero;
            }
        }


        /// <summary>
        /// This Finalize method will run only if the 
        /// Dispose method does not get called.
        /// By default, methods are NotOverridable. 
        /// This prevents a derived class from overriding this method.
        /// </summary>
        ~CShellItem()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }


        #endregion

        #region    Icomparable -- for default Sorting

        /// <summary>Computes the Sort key of this CShellItem, based on its attributes</summary>
        private int ComputeSortFlag()
        {
            int rVal = 0;
            if (m_IsDisk)
                rVal = 0x100000;

            if (m_TypeName.Equals(CShellItemFactory.StrSystemFolder))
            {
                if (!m_IsBrowsable)
                {
                    rVal = rVal | 0x10000;
                    if (CShellItemFactory.StrMyDocuments.Equals(m_DisplayName))
                    {
                        rVal = rVal | 0x1;
                    }
                }
                else
                {
                    rVal = rVal | 0x1000;
                }
            }
            if (m_IsFolder)
                rVal = rVal | 0x100;
            return rVal;
        }

        /// <summary>
        /// Compares an Object to this instance based on SortFlag. The Object must be a CShellItem
        /// </summary>
        /// <param name="obj">A CShellItem to be Compared to this instance.</param>
        /// <returns>-1 if this instance less than obj, 0 if equal, 1 if greater.</returns>
        /// <remarks>The Sort Order from Low to High is:
        /// <list type="bullet">
        /// <item><description>Nothing</description></item>
        /// <item><description>Disks</description></item>
        /// <item><description>non-browsable System Folders</description></item>
        /// <item><description>browsable System Folders</description></item>
        /// <item><description>Directories</description></item>
        /// <item><description>Files</description></item>
        /// </list>
        /// </remarks>
        public virtual int CompareTo(object? obj)
        {
            if (obj == null)
                return 1; // non-existant is always low
            CShellItem Other = obj as CShellItem;
            // UPDATE: Error Handling for CShellItem.CompareTo
            if (Other is null)
            {
#if DEBUG
                throw new ArgumentException("Invalid argument for CShellItem.CompareTo");
#endif
                return 0; // Ignore this in release builds
            }
            //if (!m_HasDispType)
            //    SetDispType();
            int cmp = Other.SortFlag - m_SortFlag; // Note the reversal
            if (cmp != 0)
            {
                return cmp;
            }
            else if (m_IsDisk) // implies that both are
            {
                return string.Compare(FullPath, Other.FullPath);
            }
            else
            {
                // Return String.Compare(m_DisplayName, Other.DisplayName)
                return StringLogicalComparer.CompareStrings(DisplayName, Other.DisplayName);
            }
        }
        #endregion


        #region    Public Methods

        #region    Shared Public Methods


        #region       AllFolderWalk
        /// <summary>The WalkAllCallBack delegate defines the signature of 
        /// the routine to be passed to AllFolderWalk which returns the CShellItem of each
        /// file and directory in and below an Folder CShellItem.
        /// </summary>
        /// <example>Dim DWalk as New CShellItem.WalkAllCallBack(addressof yourroutine)</example>
        public delegate bool WalkAllCallBack(CShellItem info, int UserLevel, int Tag);

        /// <summary>
        /// AllFolderWalk recursively walks down directories from cStart, calling its
        ///   callback routine, WalkAllCallBack, for each Directory and File encountered, including those in
        ///   cStart.  UserLevel is incremented by 1 for each level of dirs that DirWalker
        /// recurses thru.  Tag is an Integer that is simply passed, unmodified to the 
        /// callback, with each CShellItem encountered, both File and Directory CShItems.
        /// </summary>
        /// <param name="cStart">The CShellItem being examined</param>
        /// <param name="cback">AddressOf a WalkAllCallBack routine</param>
        /// <param name="UserLevel">An integer, incremented by 1 for each level of directory and passed to the CallBack routine</param>
        /// <param name="Tag">An integer passed unmodified to the CallBack routine</param>
        /// <returns>True to continue Walk, False if Callback said to stop</returns>
        /// <remarks>It is much more efficient to implement this Function (without CallBack) in the application.</remarks>
        public static bool AllFolderWalk(CShellItem cStart, WalkAllCallBack cback, int UserLevel, int Tag)
        {
            if (!(cStart == null) && cStart.IsFolder)
            {
                CShellItem cItem;
                // first processes all files in this directory
                foreach (CShellItem currentCItem in cStart._files)
                {
                    cItem = currentCItem;       // 7/2/2012 used Files
                    if (!cback(cItem, UserLevel, Tag))
                    {
                        return false;        // user said stop
                    }
                }
                // then process all dirs in this directory, recursively
                foreach (CShellItem currentCItem1 in cStart.Directories)
                {
                    cItem = currentCItem1;          // 7/2/2012 used Directories
                    if (!cback(cItem, UserLevel + 1, Tag))
                    {
                        return false;        // user said stop
                    }
                    else if (!AllFolderWalk(cItem, cback, UserLevel + 1, Tag))
                    {
                        return false;
                    }
                }
                return true;
            }
            else        // Invalid call
            {
                throw new ApplicationException("AllFolderWalk -- Invalid Start Directory");
            }
        }
        #endregion

        #endregion

        #region Public Instance Methods

        /// <summary>
        /// Compares this instance of CShellItem to another CShellItem. Equality is based on a string comparison of
        /// their Paths.
        /// </summary>
        /// <param name="other">A CShellItem to be tested for equality to the current instance.</param>
        /// <returns>True if both paths are equal.</returns>
        /// <remarks>An Obsolete method. Since only one copy of a CShellItem is allowed, the proper test
        /// is "If Me Is other".</remarks>
        public bool Equals(CShellItem other)
        {
            bool EqualsRet = default;
            EqualsRet = FullPath.Equals(other.FullPath);
            return EqualsRet;
        }

        /// <summary>
        /// Clear File and/or Folder items from the CShellItem internal cache.
        /// </summary>
        /// <param name="ClearFiles">Clear Files</param>
        /// <param name="ClearDirectories">Clear Folders</param>
        /// <remarks>Typically used to discard CShItems representing Files that are no longer displayed in 
        /// the GUI.</remarks>
        public void ClearItems(bool ClearFiles, bool ClearDirectories = false)
        {
            Debug.WriteLine("Clearing items for " + FullPath);
            lock (_files)
            {
                if (ClearFiles && _files is not null)
                {
                    _files.Clear();
                    _files = null;
                }
            }

            lock (_directories)
            {
                if (ClearDirectories && _directories is not null)
                {
                    _directories.Clear();
                    _directories = null;
                }
            }
        }

        /// <summary>
        /// Returns the Files of this sub-folder, filtered by a filtering string, as a
        ///   List of CShitems
        /// </summary>
        /// <param name="Filter">A filter string (for example: *.Doc)</param>
        /// <returns>A List of CShItems. May return an empty List if there are none.</returns>
        /// <remarks>Added 8/22/2012</remarks>
        public List<CShellItem> GetFiles(string Filter) //todo: mave this into CShellItemCollection
        {
            var GetFilesRet = new List<CShellItem>();
            if (m_IsFolder)
            {
                Filter = Filter.ToLower();
                foreach (CShellItem CSI in Files)
                {
                    if (Utils.WildcardLike(CSI.DisplayName.ToLowerInvariant(), Filter))
                    {
                        GetFilesRet.Add(CSI);
                    }
                }
            }

            return GetFilesRet;
        }

        /// <summary>GetFileName returns the Full file name of this item.
        /// Specifically, for a link file (xxx.txt.lnk for example) the
        /// DisplayName property will return xxx.txt, this method will
        /// return xxx.txt.lnk.</summary>
        /// <returns>The Name of this instance</returns>
        /// <remarks>In most cases this is equivalent to
        /// System.IO.Path.GetFileName(m_Path).  However, some m_Paths
        /// actually are GUIDs.  In that case, this routine returns the
        /// DisplayName</remarks>
        public string GetFileName()
        {
            if (FullPath.StartsWith("::{")) // Path is really a GUID
            {
                return DisplayName;
            }
            else if (m_IsDisk)
            {
                return FullPath.Substring(0, 1);
            }
            else
            {
                return System.IO.Path.GetFileName(FullPath);
            }
        }

        /// <summary>
        /// Resets the IconIndex to the current value
        /// </summary>
        /// <remarks>Certain, seldom occuring, Dynamic Updates will cause the actual Icon and its' IconIndex to change.
        ///          The handlers for these Update Events should Reset the IconIndex to show the new Icon.</remarks>
        public void ResetIconIndex()
        {
            m_IconIndexNormal = -1;        // index into the SystemImageListManager list for Normal icon
            m_IconIndexOpen = -1;          // index into the SystemImageListManager list for Open icon
            m_IconIndexNormalOrig = -1;    // index into the System Image list for Normal icon
            m_IconIndexOpenOrig = -1;      // index into the SystemImage list for Open icon
            ImageIndex = -1;
            SystemImageListManager.GetIconIndex(this, false);
            SystemImageListManager.GetIconIndex(this, true);
        }

        /// <summary>
        /// If the current instance (Me) is a Link then return the name of the Target of this link.
        /// </summary>
        /// <returns>If this instance is a link, then the name of the link target. If current instance
        /// is not a link, then returns the empty string.</returns>
        /// <remarks>Illustrates use of Activator.CreateInstance.</remarks>
        public string GetLinkTarget()
        {
            IPersistFile? pf;
            IShellLink? m_Link = null;

            try
            {
                Type? tShellLink = Type.GetTypeFromCLSID(CLSID_ShellLink);
                if (tShellLink is null) return string.Empty;

                m_Link = (IShellLink?)Activator.CreateInstance(tShellLink);
                if (m_Link is null) return string.Empty;

                if (IsLink)
                {
                    pf = (IPersistFile?)m_Link;
                    int HR = pf.Load(FullPath, 0);
                    if (HR == S_OK)
                    {
                        WIN32_FIND_DATA wfd;
                        var SB = new StringBuilder(WinSDK.MAX_PATH_NT);
                        HR = m_Link.GetPath(SB, SB.Capacity, out wfd, SLGP.UNCPRIORITY);
                        if (HR == S_OK)
                        {
                            return SB.ToString();
                        }
                    }
                }
            }
            finally
            {
                if (m_Link != null)
                    Marshal.ReleaseComObject(m_Link);
            }
            return "";
        }

        public IShellFolder? GetIShellFolder()
        {
            var shellfolder = ShellHelper.GetIShellFolder(PIDL);
            return shellfolder;
        }
        /// <summary>
        /// Returns the DisplayName as the normal ToString value
        /// </summary>
        /// <returns>The DisplayName</returns>
        public override string ToString()
        {
            return m_DisplayName;
        }
        
        /// <summary>
        /// Writes some key properties of this CShellItem to the Debug console.
        /// </summary>
        public void DebugDump()
        {
            Debug.WriteLine("DisplayName = " + m_DisplayName);
            Debug.WriteLine("PIDL        = " + m_Pidl.ToString());
            Debug.WriteLine("\tPath        = " + m_FullPath);
            Debug.WriteLine("\tTypeName    = " + TypeName);
            Debug.WriteLine("\tiIconNormal = " + m_IconIndexNormal);
            Debug.WriteLine("\tiIconSelect = " + m_IconIndexOpen);
            Debug.WriteLine("\tIsBrowsable = " + m_IsBrowsable);
            Debug.WriteLine("\tIsFileSystem= " + m_IsFileSystem);
            Debug.WriteLine("\tIsFolder    = " + m_IsFolder);
            Debug.WriteLine("\tIsLink    = " + m_IsLink);
            Debug.WriteLine("\tIsDropTarget = " + IsDropTarget);
            Debug.WriteLine("\tIsReadOnly   = " + IsReadOnly);
            Debug.WriteLine("\tCanCopy = " + CanCopy);
            Debug.WriteLine("\tCanLink = " + CanLink);
            Debug.WriteLine("\tCanMove = " + CanMove);
            Debug.WriteLine("\tCanDelete = " + CanDelete);
            if (m_IsFolder)
            {
                if (!(_directories == null))
                {
                    Debug.WriteLine("\tDirectory Count = " + _directories.Count);
                }
                else
                {
                    Debug.WriteLine("\tDirectory Count Not yet set");
                }
            }
        }
        
        /// <summary>
        /// This method obtains the IDropTarget of this CShellItem instance. 
        /// It primarily uses GetUIObjectOf via ShellHelper.GetIDropTarget, with a fallback to CreateViewObject.
        /// </summary>
        /// <param name="tn">The control in which the GUI representation of this CShellItem lives.</param>
        /// <returns>If successful, the IDropTarget interface of the Folder represented by this CShellItem.
        /// If unsuccessful, returns Nothing.</returns>
        /// <remarks>A similar function exists in the ShellHelper class. GetDropTargetOf is more efficient.</remarks>
        public Shell.IDropTarget GetDropTargetOf(Control tn)
        {
            if (!IsFolder)
                return null;

            // Standard way: GetUIObjectOf on the parent
            if (ShellHelper.GetIDropTarget(this, out var target))
            {
                return target;
            }

            // Fallback: CreateViewObject (might be needed for some virtual folders or background drops)
            IntPtr pInterface = IntPtr.Zero;
            var tnH = tn.Handle;

            var shellFolder = this.GetIShellFolder();
            try
            {
                if (shellFolder.CreateViewObject(tnH, ShellAPI.IID_IDropTarget, ref pInterface) == S_OK)
                {
                    var dropTarget = (Shell.IDropTarget)Marshal.GetTypedObjectForIUnknown(pInterface, typeof(Shell.IDropTarget));
                    Marshal.Release(pInterface); // RCW has its own ref; release the raw COM ref
                    return dropTarget;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shellFolder);
            }

            return null;
        }

        public bool HasAtLeastOneSubfolder()
        {
            if (!m_IsFolder) return false;

            if (m_IsFileSystem && !string.IsNullOrEmpty(FullPath))
            {
                try
                {
                    // Optimization: For file system objects, using Directory.EnumerateDirectories 
                    // is significantly faster than the Shell API and avoids heavy blocking.
                    return Directory.EnumerateDirectories(FullPath).Any();
                }
                catch
                {
                    // Fall back to Shell API on any error (e.g. Access Denied)
                }
            }

            IEnumIDList enumList = null;
            IntPtr[] pidlSub = new IntPtr[1];
            uint fetched = 0;

            if (!IsFolder) return false;

            // SHCONTF_FOLDERS: Only look for folders
            // SHCONTF_INCLUDEHIDDEN: Optional, if you want to be certain about hidden ones
            var shellFolder = this.GetIShellFolder();
            int hr = shellFolder.EnumObjects(IntPtr.Zero, SHCONTF.FOLDERS, out enumList);

            if (hr == S_OK && enumList != null)
            {
                // Try to get exactly one item
                hr = enumList.Next(1, pidlSub, out fetched);

                // Clean up immediately
                if (pidlSub[0] != IntPtr.Zero) Marshal.FreeCoTaskMem(pidlSub[0]);
                Marshal.ReleaseComObject(enumList);

                // S_OK means at least one was found.
                // S_FALSE means the folder is empty.
                return (hr == S_OK && fetched > 0);
            }

            return false;
        }

        #region        Utility functions

        /// <summary>
        /// Given a Byte() containing a valid PIDL of a Folder, return the IShellFolder of that Folder
        /// </summary>
        /// <param name="b">Byte() containing a valid PIDL of a Folder</param>
        /// <returns>The IShellFolder for the requested PIDL. If Byte() does not contain a valid PIDL of a Folder, return Nothing</returns>
        public static IShellFolder GetIShellFolder(byte[] b)
        {
            IShellFolder MakeFolderFromBytesRet = default;
            //GetDeskTop();                        // ensure we are initialized
            // MakeFolderFromBytes = Nothing       'get rid of VS2005 warning
            if (!CPidl.IsValid(b))
                return null;

            if ((b.Length == 2 && b[0] == 0 & b[1] == 0)
                || b.Length == 0) // this is the desktop
            {
                //return ShellController.DesktopCSI.IShlFolder;
                return ShellHelper.GetIShellFolder(ShellController.DesktopCSI.PIDL);
            }
            else
            {
                var ptr = Marshal.AllocCoTaskMem(b.Length);
                if (ptr.Equals(IntPtr.Zero))
                    return null;
                Marshal.Copy(b, 0, ptr, b.Length);
                // the next statement assigns a IshellFolder object to the function return, or has an error
                MakeFolderFromBytesRet = ShellHelper.GetIShellFolder(ptr);
                Marshal.FreeCoTaskMem(ptr);
            }

            return MakeFolderFromBytesRet;
        }

        #endregion

        #endregion

        #region        Update Methods

        /// <summary>
        /// On a Rename operation, we simply modify the existant CShellItem to reflect the new PIDL, Path, and
        /// Folder (if a folder).
        /// Since in this version of CShellItem, m_Pidl is an absolute, fully qualified pidl, it must be updated
        /// when any of the ancestor Folders is Renamed/Moved. 
        /// This is also true for both the Path property and the Folder property.
        /// For Pidls, we actually perform the update here. For Paths, we simply set it to String.Empty and let
        /// me.Path recreate it as needed.  The latter implies that m_Path should never be read -- use Me.Path instead
        /// for any _get references.
        /// For Folders, we set the UpdateFolder property so that the folder interface is re-fetched when needed.
        /// As with Path, this implies that Me.Folder should always be used rather than m_Folder.
        /// </summary>
        /// <remarks></remarks>
        internal void UpdateFolderPidlAndPath()
        {
            m_FullPath = string.Empty;             // will update when needed
            IntPtr newPidl;
            newPidl = CPidl.Concatenate(Parent.PIDL, ILFindLastID(PIDL));
            Marshal.FreeCoTaskMem(m_Pidl);
            m_Pidl = newPidl;
            if (IsFolder)
            {
                UpdateFolder = true;                // 05/22/2015 Where it should have always been done
                if (_files is not null)
                {
                    foreach (CShellItem item in _files)
                        item.UpdateFolderPidlAndPath();
                }
                if (_directories is not null)
                {
                    foreach (CShellItem item in _directories)
                        // item.UpdateFolder = True       '05/22/2015 Relocated this
                        item.UpdateFolderPidlAndPath();
                }
            }
        }

        #endregion

        #endregion


        #region ShallowCopy / Ghost

        /// <summary>
        /// Creates a new CShellItem with all value-type fields copied directly and
        /// reference-type fields sharing the same top-level reference (no deep clone).
        /// The PIDL is duplicated via CoTaskMem so each instance owns its own unmanaged memory.
        /// </summary>
        public CShellItem ShallowCopy()
        {
            var copy = new CShellItem();

            // PIDL – duplicate unmanaged memory so each instance owns its own
            if (m_Pidl != IntPtr.Zero)
            {
                copy.m_Pidl = ILClone(m_Pidl);
            }

            // Value types – direct copy
            copy.m_IconIndexNormal = m_IconIndexNormal;
            copy.m_IconIndexOpen = m_IconIndexOpen;
            copy.m_IconIndexNormalOrig = m_IconIndexNormalOrig;
            copy.m_IconIndexOpenOrig = m_IconIndexOpenOrig;
            copy.m_IsBrowsable = m_IsBrowsable;
            copy.m_IsFileSystem = m_IsFileSystem;
            copy.m_IsFolder = m_IsFolder;
            copy.m_HasSubFolders = m_HasSubFolders;
            copy.m_IsLink = m_IsLink;
            copy.m_IsDisk = m_IsDisk;
            copy.m_IsShared = m_IsShared;
            copy.m_IsHidden = m_IsHidden;
            copy.m_IsNetWorkDrive = m_IsNetWorkDrive;
            copy.m_IsRemovable = m_IsRemovable;
            copy.m_IsReadOnly = m_IsReadOnly;
            copy.m_CanMove = m_CanMove;
            copy.m_CanCopy = m_CanCopy;
            copy.m_CanDelete = m_CanDelete;
            copy.m_CanLink = m_CanLink;
            copy.m_CanRename = m_CanRename;
            copy.m_Attributes = m_Attributes;
            copy.m_SFGAO_Attributes = m_SFGAO_Attributes;
            copy.m_IsRemote = m_IsRemote;
            copy.m_W32Data = m_W32Data;
            copy.m_SortFlag = m_SortFlag;
            copy.m_XtrInfo = m_XtrInfo;
            copy.m_LastWriteTime = m_LastWriteTime;
            copy.m_CreationTime = m_CreationTime;
            copy.m_LastAccessTime = m_LastAccessTime;
            copy.m_Length = m_Length;
            copy.m_HasDispType = m_HasDispType;
            copy.m_IsReadOnlySetup = m_IsReadOnlySetup;
            copy.m_UpdateFolder = m_UpdateFolder;
            copy.m_Disposed = false;
            copy.ID = ID;
            copy.IsDropTarget = IsDropTarget;
            copy.ImageIndex = ImageIndex;
            copy.NeedsRefresh = NeedsRefresh;
            copy._IsSystem_HaveSysInfo = _IsSystem_HaveSysInfo;
            copy._IsSystem_m_IsSystem = _IsSystem_m_IsSystem;
            copy.DirsCollectionTimestamp = DirsCollectionTimestamp;
            copy.FilesCollectionTimestamp = FilesCollectionTimestamp;

            // Reference types – share top-level reference
            copy.m_Parent = m_Parent;
            copy.m_DisplayName = m_DisplayName;
            copy.m_FullPath = m_FullPath;
            copy.m_TypeName = m_TypeName;
            copy.m_cPidl = m_cPidl;
            copy.m_columnDic = m_columnDic;
            copy.LVItem = LVItem;
            copy.TNode = TNode;
            copy.Tag = Tag;
            copy._directories = _directories;
            copy._files = _files;
            copy.m_size = m_size;
            copy.currentPath = currentPath;

            return copy;
        }

        /// <summary>
        /// Sets all reference-type fields to null, effectively severing the
        /// shallow links created by <see cref="ShallowCopy"/>.
        /// Value types are left untouched.
        /// </summary>
        public void Ghost()
        {
            m_Parent = null;
            m_FullPath = null;
            m_TypeName = null;
            m_cPidl = null;
            m_columnDic = null;
            LVItem = null;
            TNode = null;
            Tag = null;
            _directories = null;
            _files = null;
        }

        #endregion

        #region    Private Methods

        public void ReloadInfo()
        {
            ResetInfo();
            CShellItemFactory.PopulateBasicFields(this);
            ResetIconIndex();
        }

        public void ResetInfo()
        {
            m_HasDispType = false;
            m_IsReadOnlySetup = false;
            m_XtrInfo = false;
            m_HasSubFolders = null;
            if (m_W32Data is not null && m_W32Data is W32Find_Data)
                m_W32Data = null;
            ResetIconIndex(); //todo: remove this from here
            m_columnDic?.Clear();
        }
        
        public void ResetChildren()
        {
            // propogate changes to the known children
            if (_files is not null)
            {
                foreach (CShellItem item in _files)
                    item.ResetInfo();
            }
            if (_directories is not null)
            {
                foreach (CShellItem item in _directories)
                    item.ResetInfo();
            }
        }


        /// <summary>
        /// Obtains information available from FileInfo. Uses data from W32Data rather than FileInfo/DirectoryInfo if W32Data is present.
        /// </summary>
        private void FillDemandInfo()
        {
            if (m_W32Data is not null)
            {
                if (m_IsFileSystem)
                {
                    var W_32 = m_W32Data;
                    m_LastWriteTime = W_32.LastWriteTime;
                    m_LastAccessTime = W_32.LastAccessTime;
                    m_CreationTime = W_32.CreationTime;
                    if (!m_IsFolder)
                        m_Length = W_32.Length;
                    m_Attributes = (FileAttributes)W_32.Attributes;
                }
                else
                {
                    var W_32 = m_W32Data;
                    m_LastWriteTime = W_32.LastWriteTime;
                    m_LastAccessTime = W_32.LastAccessTime;
                    m_CreationTime = W_32.CreationTime;
                    if (!m_IsFolder)
                        m_Length = W_32.Length;
                    m_Attributes = (FileAttributes)W_32.Attributes;
                }
                m_W32Data = null;      // have what we need. clear for updates
            }
            else
            {
                if (m_IsFileSystem & !m_IsFolder)
                {
                    // in this case, it's a file
                    var fi = new FileInfo(FullPath);
                    if (fi.Exists)
                    {
                        m_LastWriteTime = fi.LastWriteTime;
                        m_LastAccessTime = fi.LastAccessTime;
                        m_CreationTime = fi.CreationTime;
                        m_Length = fi.Length;
                        m_Attributes = fi.Attributes;
                    }
                }
                else if (m_IsFileSystem & m_IsFolder)
                {
                    var di = new DirectoryInfo(FullPath);
                    if (di.Exists)
                    {
                        m_LastWriteTime = di.LastWriteTime;
                        m_LastAccessTime = di.LastAccessTime;
                        m_CreationTime = di.CreationTime;
                        m_Attributes = di.Attributes;
                    }
                }
            }
            m_XtrInfo = true;
        }

        private void GetSizeString()
        {
            // Split the file size into bytes, kb, MB and GB
            if (!IsFolder && IsFileSystem || IsDisk)
            {
                if (Length >= 1073741824)
                {
                    m_size = $"{Length / 1073741824d:#,##0.##} GB";
                }
                else if (Length >= 1048576L)
                {
                    m_size = $"{Length / 1048576d:#,##0.##} MB";
                }
                else if (Length >= 1024L)
                {
                    m_size = $"{Length / 1024d:#,##0} KB";
                }
                else if (!(IsRemovable & Length == 0L)) // Don't show a CD-ROM's size if it doesn't have a disk in it
                {
                    m_size = $"{Length:#,##0} Bytes";
                }
                else
                {
                    m_size = "";
                } // Empty CD-ROM
            }
            else
            {
                m_size = "";
            }
        }

        /// <summary>
        /// Sets DisplayName, TypeName, and SortFlag when actually needed
        /// </summary>
        //internal void SetDispType() //todo: remove this.  the functionality is in cshellitemfactory now and is faster
        //{
        //    // Get Displayname, TypeName
        //    var shfi = new SHFILEINFO();
        //    var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL; //you can also ask for attributes here with SHGFI.ATTRIBUTES
        //    int dwAttr = 0;
        //    if (m_IsFileSystem && !m_IsFolder)
        //    {
        //        dwflag = dwflag | SHGFI.USEFILEATTRIBUTES;
        //        dwAttr = FILE_ATTRIBUTE_NORMAL;
        //    }

        //    var hr = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, SHFILEINFO_size, dwflag);
            
        //    m_DisplayName = shfi.szDisplayName;
        //    m_TypeName = shfi.szTypeName;
        //    m_SortFlag = ComputeSortFlag();
        //    m_HasDispType = true;
        //    // fix DisplayName
        //    if (string.IsNullOrEmpty(m_DisplayName))
        //        m_DisplayName = Path.GetFileName(FullPath);
        //}

        #endregion



    }

}