using System.Collections;
using System.Diagnostics;
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
        private static string m_strSystemFolder;

        // My Computer is also commonly used (though not internally),
        // so save & expose its name on the current machine
        private static string m_strMyComputer;

        // To get My Documents sorted first, we need to know the Locale 
        // specific name of that folder.
        private static string m_strMyDocuments;

        // The DesktopBase is set up via Sub New() (one time only) and
        // disposed of only when DesktopBase is finally disposed of
        private static CShellItem DesktopBase;

        // DragDrop, possibly among others, needs to know the Path of
        // the DeskTopDirectory in addition to the Desktop itself
        // Also need the actual CShellItem for the DeskTopDirectory, so get it
        private static CShellItem m_DeskTopDirectory;

        /// <summary>
        /// The CShellItem of the Recycle Bin. Set in New() (the Desktop creator)
        /// Used to prevent UPDATEDIR on this Item from processing.
        /// Als used to prevent normal UPDATEDIR on Desktop from processing the
        /// Recycle Bin which would cause an effectively endless loop.
        /// </summary>
        private static CShellItem m_Recycle;            // 6/21/2012

        // Keep the local System Name for IsRemote testing
        private static string SystemName;                              // 4/14/2012
                                                                       // Keep list of Drives and their DriveType for IsRemote testing
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
        private static readonly object LockObj = new object();

        #endregion


        #region    Instance Private Fields
        // m_Folder and m_Pidl must be released/freed at Dispose time
        private IShellFolder m_Folder;    // if item is a folder, contains the Folder interface for this instance
        private IntPtr m_Pidl;            // The Absolute PIDL for this item (not retained for files)
        private string m_DisplayName = "";
        private string m_Path;
        private string m_TypeName;
        private CShellItem m_Parent;
        private int m_IconIndexNormal = -1;        // index into the SystemImageListManager list for Normal icon
        private int m_IconIndexOpen = -1;          // index into the SystemImageListManager list for Open icon
        private int m_IconIndexNormalOrig = -1;    // index into the System Image list for Normal icon
        private int m_IconIndexOpenOrig = -1;      // index into the SystemImage list for Open icon
        private bool m_IsBrowsable;
        private bool m_IsFileSystem;
        private bool m_IsFolder;
        private bool m_HasSubFolders;
        private bool m_IsLink;
        private bool m_IsDisk;
        private bool m_IsShared;
        private bool m_IsHidden;
        private bool m_IsNetWorkDrive;
        private bool m_IsRemovable;
        private bool m_IsReadOnly;
        // Properties of interest to Drag Operations
        private bool m_CanMove;
        private bool m_CanCopy;
        private bool m_CanDelete;
        private bool m_CanLink;
        private bool m_IsDropTarget;
        private bool m_CanRename;

        private CShellItemCollection m_Directories;
        private CShellItemCollection m_Files;

        private SFGAO m_SFGAO_Attributes;     // the original, returned from GetAttributesOf Added 10/09/2011 
        private bool m_IsRemote;           // 4/14/2012

        private object m_Tag;                 // Added 10/09/2011
        private W32Find_Data m_W32Data;       // 4/24/2012

        private int m_SortFlag;       // Used in comparisons

        // For shell events 
        private CShellItemUpdater m_updater;

        // The following elements are only filled in on demand
        private bool m_XtrInfo;
        private DateTime m_LastWriteTime;
        private DateTime m_CreationTime;
        private DateTime m_LastAccessTime;
        private long m_Length;
        private FileAttributes m_Attributes;  // Added 10/09/2011 'True FileAttributes from FileInfo

        // Indicates whether DisplayName, TypeName, SortFlag have been set up
        private bool m_HasDispType;

        // Indicates whether IsReadOnly has been set up
        private bool m_IsReadOnlySetup; // 

        // m_UpdateFolder is True is the IShellFolder (m_Folder) must be refetched
        private bool m_UpdateFolder;

        // Holds a byte() representation of m_PIDL -- filled when needed
        private CPidl m_cPidl;

        // Flags for Dispose state
        // Private m_IsDisposing As Boolean
        private bool m_Disposed;


        #endregion


        #region Private properties

        private bool UpdateFolder
        {
            get
            {
                return m_UpdateFolder;
            }
            set
            {
                m_UpdateFolder = value;
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        internal CShellItemCollection FileList
        {
            get
            {
                return m_Files;
            }
        }

        private void GetSize()
        {
            // Split the file size into bytes, kb, MB and GB
            if (!IsFolder & IsFileSystem | IsDisk)
            {
                if (Length >= 1048576 * 1024)
                {
                    m_size = $"{Length / (double)(1048576 * 1024):#,##0.#} GB";
                }
                else if (Length >= 1048576L)
                {
                    m_size = $"{Length / 1048576d:#,##0.#} MB";
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
        private void SetDispType()
        {
            // Get Displayname, TypeName
            var shfi = new SHFILEINFO();
            var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            int dwAttr = 0;
            if (m_IsFileSystem & !m_IsFolder)
            {
                dwflag = dwflag | SHGFI.USEFILEATTRIBUTES;
                dwAttr = FILE_ATTRIBUTE_NORMAL;
            }
            var H = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, cbFileInfo, dwflag);
            m_DisplayName = shfi.szDisplayName;
            m_TypeName = shfi.szTypeName;
            // fix DisplayName
            if (m_DisplayName.Equals(""))
            {
                m_DisplayName = FullPath;
            }
            // Fix TypeName
            // If m_IsFolder And m_TypeName.Equals("File") Then
            // m_TypeName = "File Folder"
            // End If
            m_SortFlag = ComputeSortFlag();
            m_HasDispType = true;
        }

        private int SortFlag
        {
            get
            {
                if (!m_HasDispType)
                    SetDispType();
                return m_SortFlag;
            }
        }

        #region            IconIndex properties
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
                    if (!m_HasDispType)
                        SetDispType();
                    var shfi = new SHFILEINFO();
                    var dwflag = SHGFI.PIDL | SHGFI.SYSICONINDEX;
                    int dwAttr = 0;
                    if (m_IsFileSystem & !m_IsFolder)
                    {
                        dwflag = dwflag | SHGFI.USEFILEATTRIBUTES;
                        dwAttr = FILE_ATTRIBUTE_NORMAL;
                    }
                    var H = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, cbFileInfo, dwflag);
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
                    if (!m_HasDispType)
                        SetDispType();
                    if (!m_IsDisk & m_IsFileSystem & m_IsFolder)
                    {
                        var dwflag = SHGFI.SYSICONINDEX | SHGFI.PIDL;
                        var shfi = new SHFILEINFO();
                        var H = SHGetFileInfo(m_Pidl, 0, ref shfi, cbFileInfo, dwflag | SHGFI.OPENICON);
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


        #region    Constructors/Destructors

        /// <summary>
        /// Private Constructor. Creates CShellItem of the Desktop
        /// </summary>
        private CShellItem()           // only used when desktopfolder has not been intialized
        {
            if (!(DesktopBase == null))
            {
                throw new Exception("Attempt to initialize CShellItem for second time");
            }

            int HR;
            // firstly determine what the local machine calls a "System Folder" and "My Computer"
            IntPtr tmpPidl = IntPtr.Zero;
            HR = SHGetSpecialFolderLocation(0, (int)CSIDL.DRIVES, ref tmpPidl);
            var shfi = new SHFILEINFO();
            var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            int dwAttr = 0;
            SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
            m_strSystemFolder = shfi.szTypeName;
            m_strMyComputer = shfi.szDisplayName;
            Marshal.FreeCoTaskMem(tmpPidl);

            // With That done, now set up Desktop CShellItem
            m_Path = "::{" + DesktopGUID.ToString() + "}";
            m_IsFolder = true;
            m_HasSubFolders = true;
            m_IsBrowsable = false;
            HR = SHGetDesktopFolder(ref m_Folder);
            // m_Pidl = GetSpecialFolderLocation(IntPtr.Zero, (int)CSIDL.DESKTOP);
            // Force m_Pidl to be the virtual root PIDL (empty)
            m_Pidl = Marshal.AllocCoTaskMem(2);
            Marshal.WriteInt16(m_Pidl, 0, 0);

            dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.SYSICONINDEX | SHGFI.PIDL;
            dwAttr = 0;
            var desktop = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, cbFileInfo, dwflag);

            m_DisplayName = shfi.szDisplayName;
            m_TypeName = StrSystemFolder;   // not returned correctly by SHGetFileInfo
            m_IconIndexNormal = shfi.iIcon;
            m_IconIndexOpen = shfi.iIcon;
            m_HasDispType = true;
            m_IsDropTarget = true;
            m_IsReadOnly = false;
            m_IsReadOnlySetup = true;

            // also get local name for "My Documents"
            var pchEaten = default(int);
            tmpPidl = IntPtr.Zero;
            int argpdwAttributes = default;
            HR = Folder.ParseDisplayName(default, default, "::{450d8fba-ad25-11d0-98a8-0800361b1103}", ref pchEaten, ref tmpPidl, ref argpdwAttributes);
            shfi = new SHFILEINFO();
            dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            dwAttr = 0;
            SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
            m_strMyDocuments = shfi.szDisplayName;
            Marshal.FreeCoTaskMem(tmpPidl);
            // this must be done after getting "My Documents" string
            m_SortFlag = ComputeSortFlag();
            // Set DesktopBase
            DesktopBase = this;
            // Get the SystemName for Remote item testing
            SystemName = Environment.MachineName;    // 4/14/2012
                                                     // Get the Path and CShellItem of the DesktopDirectory
            m_DeskTopDirectory = GetCShItem(CSIDL.DESKTOPDIRECTORY);
            // Get the CShellItem for the Recycle Bin   6/21/2012
            m_Recycle = GetCShItem(CSIDL.BITBUCKET); // 6/21/2012
                                                     // Start the Notification Process
            m_updater = new CShellItemUpdater(this);
        }

        internal CShellItem(IntPtr pidl, CShellItem parent = null)
        {
            if (DesktopBase == null)
            {
                DesktopBase = new CShellItem(); // This initializes the Desktop folder
            }
            m_Parent = parent;
            if (parent == null)
            {
                m_Pidl = pidl;
                // Get some attributes
                IShellFolder m_Folder = null;
                SHGetDesktopFolder(ref m_Folder);

                SetUpAttributes(m_Folder, pidl);
            }
            else 
            {
                m_Pidl = CPidl.ConcatPidls(parent.PIDL, pidl);
                // Get some attributes
                SetUpAttributes(parent.Folder, pidl);
            }


            // Set unfetched value for IconIndex....
            m_IconIndexNormal = -1;
            m_IconIndexOpen = -1;
            // finally, set up my Folder
            if (m_IsFolder)
            {
                m_Folder = ShellHelper.GetFolder(parent, pidl);
                // m_Folder may be returned as Nothing. This is handled in GetContents
            }
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
            if (!m_Disposed)
            {
                // If disposing equals true, dispose all managed 
                // and unmanaged resources.
                m_Disposed = true;
                if (disposing)
                {
                }
                // Release unmanaged resources. If disposing is false,
                // only the following code is executed. 
                if (!(m_Folder == null))
                {
                    Marshal.ReleaseComObject(m_Folder);
                    m_Folder = null;
                }
                if (!m_Pidl.Equals(IntPtr.Zero))
                {
                    Marshal.FreeCoTaskMem(m_Pidl);
                    m_Pidl = IntPtr.Zero;
                }
            }
            else
            {
                throw new Exception("CShellItem Disposed more than once");
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


        #region        Utility functions

        /// <summary>
        /// Given a Byte() containing a valid PIDL of a Folder, return the IShellFolder of that Folder
        /// </summary>
        /// <param name="b">Byte() containing a valid PIDL of a Folder</param>
        /// <returns>The IShellFolder for the requested PIDL. If Byte() does not contain a valid PIDL of a Folder, return Nothing</returns>
        public static IShellFolder MakeFolderFromBytes(byte[] b)
        {
            IShellFolder MakeFolderFromBytesRet = default;
            GetDeskTop();                        // ensure we are initialized
                                                 // MakeFolderFromBytes = Nothing       'get rid of VS2005 warning
            if (!CPidl.IsValidPidl(b))
                return null;
            if (b.Length == 2 && b[0] == 0 & b[1] == 0) // this is the desktop
            {
                return DesktopBase.Folder;
            }
            else if (b.Length == 0)   // Also indicates the desktop
            {
                return DesktopBase.Folder;
            }
            else
            {
                var ptr = Marshal.AllocCoTaskMem(b.Length);
                if (ptr.Equals(IntPtr.Zero))
                    return null;
                Marshal.Copy(b, 0, ptr, b.Length);
                // the next statement assigns a IshellFolder object to the function return, or has an error
                MakeFolderFromBytesRet = ShellHelper.GetFolder(DesktopBase, ptr);
                Marshal.FreeCoTaskMem(ptr);
            }

            return MakeFolderFromBytesRet;
        }




        /// <summary>Get the base attributes of the folder/file that this CShellItem represents</summary>
        /// <param name="folder">Parent Folder of this Item</param>
        /// <param name="pidl">Relative Pidl of this Item.</param>
        private void SetUpAttributes(IShellFolder folder, IntPtr pidl)
        {
            SFGAO attrFlag;
            attrFlag = SFGAO.BROWSABLE;                 // D
            attrFlag = attrFlag | SFGAO.FILESYSTEM;     // FD
                                                        // attrFlag = attrFlag Or SFGAO.HASSUBFOLDER   'D  'made into an on-demand attribute
            attrFlag = attrFlag | SFGAO.FOLDER;
            attrFlag = attrFlag | SFGAO.LINK;           // F
            attrFlag = attrFlag | SFGAO.SHARE;          // FD
            attrFlag = attrFlag | SFGAO.HIDDEN;         // FD
            attrFlag = attrFlag | SFGAO.REMOVABLE;
            // attrFlag = attrFlag Or SFGAO.RDONLY   'made into an on-demand attribute
            attrFlag = attrFlag | SFGAO.CANCOPY;
            attrFlag = attrFlag | SFGAO.CANDELETE;
            attrFlag = attrFlag | SFGAO.CANLINK;
            attrFlag = attrFlag | SFGAO.CANMOVE;
            attrFlag = attrFlag | SFGAO.DROPTARGET;
            attrFlag = attrFlag | SFGAO.CANRENAME;      // FD
            attrFlag = attrFlag | SFGAO.STREAM;         // F
                                                        // Note: for GetAttributesOf, we must provide an array, in  all cases with 1 element
            var aPidl = new IntPtr[1];
            aPidl[0] = pidl;
            folder.GetAttributesOf(1, aPidl, ref attrFlag);
            m_SFGAO_Attributes = attrFlag;
            m_IsBrowsable = (attrFlag & SFGAO.BROWSABLE) != 0;
            m_IsFileSystem = (attrFlag & SFGAO.FILESYSTEM) != 0;
            // m_HasSubFolders = (attrFlag & SFGAO.HASSUBFOLDER) != 0;  'made into an on-demand attribute
            m_IsFolder = (attrFlag & SFGAO.FOLDER) != 0;
            m_IsLink = (attrFlag & SFGAO.LINK) != 0;
            m_IsShared = (attrFlag & SFGAO.SHARE) != 0;
            m_IsHidden = (attrFlag & SFGAO.HIDDEN) != 0;
            m_IsRemovable = (attrFlag & SFGAO.REMOVABLE) != 0;
            // m_IsReadOnly = (attrFlag & SFGAO.RDONLY) != 0;      'made into an on-demand attribute
            m_CanCopy = (attrFlag & SFGAO.CANCOPY) != 0;
            m_CanDelete = (attrFlag & SFGAO.CANDELETE) != 0;
            m_CanLink = (attrFlag & SFGAO.CANLINK) != 0;
            m_CanMove = (attrFlag & SFGAO.CANMOVE) != 0;
            m_IsDropTarget = (attrFlag & SFGAO.DROPTARGET) != 0;
            m_CanRename = (attrFlag & SFGAO.CANRENAME) != 0;

            // Get the Path
            SetPath();

            // check for zip file = folder on xp, leave it a file
            if (m_IsFolder && m_IsFileSystem && WinSDK.XPorAbove)
            {
                // If (m_Attributes = (m_Attributes And SFGAO.STREAM)) Then
                if ((attrFlag & SFGAO.STREAM) != 0)   // in this case, it is not a Folder, but a .zip or .cab or etc
                {
                    m_IsFolder = false;
                }
            }

            if (m_IsFolder && m_Path.Length == 3 && m_Path.Substring(1).Equals(@":\"))
            {
                m_IsDisk = true;
                try // 04/16/2012 Entire Try Block
                {
                    var disk = new System.Management.ManagementObject("win32_logicaldisk.deviceid=\"" + FullPath.Substring(0, 2) + "\"");
                    m_Length = Convert.ToInt64(disk["Size"]);
                    if ((Convert.ToUInt32(disk["DriveType"]).ToString() ?? "") == (4.ToString() ?? ""))
                    {
                        m_IsNetWorkDrive = true;
                        m_IsRemote = true;
                    }
                }
                catch (Exception ex)
                {
                    // Disconnected Network Drives etc. will generate 
                    // an error here, just assume that it is a network
                    // drive
                    m_IsNetWorkDrive = true;
                    m_IsRemote = true;
                }
                finally
                {
                    m_XtrInfo = true;
                    if (!DriveDict.ContainsKey(m_Path))
                    {
                        DriveDict.Add(m_Path, m_IsRemote);
                    }
                }
            }

            // Setup IsRemote             '4/14/2012
            // Reworked 5/15/2012 when testing discovered that contrary to the Docs, IO.Path.GetPathRoot(m_Path)
            // will throw an exception when presented with a long path that GetDisplayNameOf made legal by
            // using 8.3 names for some of the directories! IO.Path.GetPathRoot is not supposed to do anything to
            // reference the actual components of the Path. It should be strictly String manipulation!
            // Error on Path = "C:\Testing\XXXXXA~1\YYYYYY~1\ABCDEF~1\ZZZZZZ~1\abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01234567890123456789012345678901234.txt"
            // which is only 138 chars long.
            if (!(m_IsDisk || m_Path.StartsWith("::")))
            {
                if (m_Path.StartsWith(@"\\"))
                {
                    string[] tmp = m_Path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tmp.Length > 0 && tmp[0].Equals(SystemName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        m_IsRemote = false;
                    }
                    else
                    {
                        m_IsRemote = true;
                    }
                }
                else if (m_Path.Length > 2 && m_Path.Substring(1, 2).Equals(@":\"))
                {
                    string itemroot = m_Path.Substring(0, 3);
                    if (DriveDict.ContainsKey(itemroot) && DriveDict[itemroot])
                        m_IsRemote = true;
                }
            }
        }

        /// <summary>
        /// Sets m_Path to the Full Path of the current Item.
        /// </summary>
        /// <remarks>Reworked 11/13/3013 to deal with the case of folder.GetDisplayNameOf returning an error.<br />
        ///          This can occur for incompletely implemented or otherwise corrupt Shell Extension Folders.<br />
        ///          All CShellItem constructors will call SetUpAttributes which will call SetPath. Effectively all
        ///          CShellItem constructors will be called by GetContents. 
        ///          GetContents will deal with the exceptions that might be thrown here by simply not inserting the
        ///          faulting CShellItem into the internal tree. Since the CShellItem is not in the tree, no change 
        ///          notification will be called for the Item.<br />
        ///          A Move of a file/folder from a known Folder to a faulty Folder will cause the moved item to 
        ///          disappear from its' original location and not appear anywhere else.
        /// </remarks>
        private void SetPath()
        {
            // Get the Path
            // Debug.WriteLine("SetPath:" & Me.Parent.DisplayName & " Parent Folder = " & Me.Parent.ToString & " Parent Path = " & Me.Parent.Path)
            var folder = Parent.Folder;
            using (var memScope = new CoTaskMemPoolScope(WinSDK.s_memPool_MaxName))
            {
                var strr = memScope.Block;
                try
                {
                    var pidl = ILFindLastID(m_Pidl);
                    // If CLng(pidl) - CLng(m_Pidl) < 0 Then
                    // Debug.WriteLine("pidl - m_pidl = " & pidl.ToString & " - " & m_Pidl.ToString & " = " & (CLng(pidl) - CLng(m_Pidl)).ToString)
                    // End If
                    Marshal.WriteInt32(strr, 0, 0); //zero out
                    var itemflags = SHGDN.FORPARSING;
                    int HR = folder.GetDisplayNameOf(pidl, itemflags, strr); //might want to change this so it get's this lazily
                    if (HR == S_OK)
                    {
                        var buf = new StringBuilder(WinSDK.MAX_NAME);
                        HR = StrRetToBuf(strr, pidl, buf, WinSDK.MAX_NAME);
                        if (HR == NOERROR)
                        {
                            m_Path = buf.ToString();
                        }
                        else
                        {
                            Marshal.ThrowExceptionForHR(HR);
                        }
                    }
                    else
                    {
                        Marshal.ThrowExceptionForHR(HR);
                    }
                }
                // Debug.WriteLine(m_Path)
                catch (Exception ex)
                {
                    // Debug.WriteLine("SetPath: Exception")
                    // Debug.WriteLine(ex.ToString)
                    // Debug.WriteLine("SetPath m_Pidl:")
                    // DumpPidl(m_Pidl)
                    m_Path = "Unknown";
                    throw;                // 11/14/2013
                }
            }
        }

        #endregion

        #region Shared private functions
        /// <summary>
        /// BrowseTo locates the desired item and places it in its proper location on the internal tree.
        /// Any and all sub-directories that need to be populated in the tree in order to properly place
        /// the desired item, are populated. This is the programatic equivalent of Browsing to a node in <code>ExpTree's</code> TreeView.<br />
        /// BrowseTo also returns the Parent CShellItem. 
        /// If the desired CShellItem does not exist, the returned Parent is the CShellItem that would be the
        /// Immediate ancestor (containing CShellItem or Parent) of the desired item should it be created.
        /// </summary>
        /// <param name="absPidl">A Absolute PIDL whose CShellItem is to be found</param>
        /// <param name="Parent">Output parameter -- Immediate Ancestor CShellItem of the found item OR 
        /// the CShellItem that would contain the item if it existed OR Nothing if NO Immediate ancestor found in the Shell namespace. </param>
        /// <returns>The desired CShellItem or, if not found, Nothing.</returns>
        /// <remarks>A by-product of this search is that any sub-dirs of the tree along the path will be 
        /// populated with their sub directories.
        /// It is logically possible that NO Immediate ancestor can be found.
        /// For Example: GetCShItem(Path) may be given a string specifying a non-existant directory.
        /// (eg -- C:\Test\NonExistant\junk.txt). 
        /// In that case, and that case only, Parent may be returned as Nothing.</remarks>
        internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        {
            CShellItem BrowseToRet = default;
            BrowseToRet = null;     // avoid VB2005 Warning
            Parent = default;
            var BaseItem = GetDeskTop();

            CShellItem CSI;
            bool FoundIt = false;      // True if we found item or an ancestor
                                       // Dim FirstWithThisBase As Boolean = True     '6/30/2012 Flag to prevent infinite loop
            while (!FoundIt)
            {
                foreach (var currentCSI in BaseItem.Directories)
                {
                    CSI = currentCSI;    // 7/2/2012 should use Directories here
                    if (IsAncestorOf(CSI.PIDL, absPidl))
                    {
                        if (CPidl.IsEqual(CSI.PIDL, absPidl))  // we found the desired item
                        {
                            Parent = BaseItem;
                            return CSI;
                        }
                        else            // Found an ancestor
                        {
                            BaseItem = CSI;
                            Parent = CSI;
                            FoundIt = true;
                            break;
                        }
                    }
                }
                if (!FoundIt)
                {
                    // UPDATE: Check for files in the desktop
                    foreach (var currentCSI1 in DesktopBase.Files)
                    {
                        CSI = currentCSI1;           // Files will do an UpdateRefresh in case of missing a CREATE
                        if (CPidl.IsEqual(CSI.PIDL, absPidl))
                        {
                            Parent = DesktopBase;
                            return CSI;
                        }
                    }
                    // The next block of code is to deal with a rare case of missing a MKDIR - 6/30/2012
                    // No longer necessary since BaseItem.Directories above will do an UpdateRefresh
                    // If FirstWithThisBase Then
                    // FirstWithThisBase = False
                    // Debug.WriteLine("***Bingo")
                    // BaseItem.UpdateRefresh(False, True)
                    // Continue Do
                    // End If
                    Parent = null;        // didn't find an ancestor
                    return null;
                }
                // The complication is that the desired item may not be a directory
                if (!IsAncestorOf(BaseItem.PIDL, absPidl, true))  // Don't have immediate ancestor
                {
                    // FirstWithThisBase = True    '6/30/2012
                    FoundIt = false;     // go around again
                }
                else
                {
                    Parent = BaseItem;
                    foreach (var currentCSI2 in BaseItem.Directories)
                    {
                        CSI = currentCSI2;        // 6/6/2012 modified 7/2/2012 Directories needed here
                        if (CPidl.IsEqual(CSI.PIDL, absPidl))
                        {
                            return CSI;
                        }
                    }
                    // Not in Dirs, so look in Files 6/6/2012 fix
                    foreach (var currentCSI3 in BaseItem.Files)
                    {
                        CSI = currentCSI3;              // Files will do an UpdateRefresh in case of missing a CREATE
                        if (CPidl.IsEqual(CSI.PIDL, absPidl))
                        {
                            return CSI;
                        }
                    }
                    // fall thru here means it doesn't exist or we can't find it because of funny PIDL from SHParseDisplayName
                    return null;
                }
            }

            return BrowseToRet;
        }

        /// <summary>Given an IntPtr representation of a PIDL,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="pidl">Absolute (Full) Pidl of item to be Found or Created</param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        internal static CShellItem GetCShItem(IntPtr pidl)
        {
            CShellItem GetCShItemRet = default;
            CShellItem Parent = null;
            GetCShItemRet = BrowseTo(pidl, out Parent);
            if (GetCShItemRet == null)
            {
                if (!(Parent == null))
                {
                    try
                    {
                        GetCShItemRet = new CShellItem(ILFindLastID(pidl), Parent);
                    }
                    catch
                    {
                        GetCShItemRet = null;
                    }
                }
            }

            return GetCShItemRet;
        }


        #endregion


        #region Shared public functions


        /// <summary>Given a Full Path in a String,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="path">The Full Path of the desired CShellItem</param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem GetCShItem(string path)
        {
            CShellItem GetCShItemRet = default;
            GetCShItemRet = null;    // assume failure
            int HR;
            IntPtr tmpPidl = IntPtr.Zero;
            int argpchEaten = 0;
            int argpdwAttributes = 0;
            HR = GetDeskTop().Folder.ParseDisplayName(0, IntPtr.Zero, path, ref argpchEaten, ref tmpPidl, ref argpdwAttributes);
            if (HR == 0)
            {
                GetCShItemRet = GetCShItem(tmpPidl);
            }
            if (!tmpPidl.Equals(IntPtr.Zero))
            {
                Marshal.FreeCoTaskMem(tmpPidl);
            }

            return GetCShItemRet;
        }

        /// <summary>Given a CSIDL,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="ID"></param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem GetCShItem(CSIDL ID)
        {
            CShellItem GetCShItemRet = default;
            GetCShItemRet = null;      // avoid VB2005 Warning
            if (ID == CSIDL.DESKTOP)
            {
                return GetDeskTop();
            }
            int HR;
            IntPtr tmpPidl = IntPtr.Zero;  // original code - retain
                                           // MYDOCUMENTS - the saga continues
                                           // In Vista and above, My Documents does not live immediately under the Desktop
                                           // (is not a member of DesktopBase.Directories)
                                           // Therefore, without special handling, this rtn will return Nothing as the 
                                           // CShellItem when CSIDL.MYDOCUMENTS is requested.
                                           // MS Documentation states that in Shell32.dll version 6.0 and above CSIDL_MYDOCUMENTS is 
                                           // Equivalent to CSIDL_PERSONAL. (6.0 = XP, 6.01 = Vista, 6.1 = Win7)
                                           // In XP, the PIDLs of PERSONAL and MYDOCUMENTS are Identical. In Vista and Win7, they are not.
                                           // In all OSes, the PIDL for MYDOCUMENTS has 1 item. In Vista and Win7, the PIDL for PERSONAL is a 
                                           // two item PIDL, which correctly reflects the location of the corresponding Folder in the directory tree.
                                           // Because of this, in Vista and above, I must use PERSONAL as the lookup CSIDL to obtain MYDOCUMENTS.

            if (ID == CSIDL.MYDOCUMENTS && WinSDK.VistaOrAbove)
                ID = CSIDL.PERSONAL; // added 11/28/2010
            if (ID == CSIDL.MYDOCUMENTS)  // original code - retain
            {
                var pchEaten = default(int);
                int argpdwAttributes = default;
                HR = GetDeskTop().Folder.ParseDisplayName(default, default, "::{450d8fba-ad25-11d0-98a8-0800361b1103}", ref pchEaten, ref tmpPidl, ref argpdwAttributes);
            }
            else
            {
                HR = SHGetSpecialFolderLocation(0, (int)ID, ref tmpPidl);
            }
            if (HR == NOERROR)
            {
                GetCShItemRet = GetCShItem(tmpPidl);
            }
            if (!tmpPidl.Equals(IntPtr.Zero))
            {
                Marshal.FreeCoTaskMem(tmpPidl);
            }

            return GetCShItemRet;
        }

        /// <summary>Given a Byte() containing the PIDL of a Folder and a Byte() containing the relative PIDL of the desired item,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="FoldBytes"></param>
        /// <param name="ItemBytes"></param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem GetCShItem(byte[] FoldBytes, byte[] ItemBytes)
        {
            CShellItem GetCShItemRet = default;
            GetCShItemRet = null;    // assume failure
            byte[] b = CPidl.JoinPidlBytes(FoldBytes, ItemBytes);
            if (b == null)
                return GetCShItemRet; // can do no more with invalid pidls

            var thisPidl = Marshal.AllocCoTaskMem(b.Length);
            if (thisPidl.Equals(IntPtr.Zero))
                return null;
            Marshal.Copy(b, 0, thisPidl, b.Length);
            // Dim Parent As CShellItem = Nothing
            GetCShItemRet = GetCShItem(thisPidl);
            if (!thisPidl.Equals(IntPtr.Zero))
                Marshal.FreeCoTaskMem(thisPidl);
            if (GetCShItemRet.PIDL.Equals(IntPtr.Zero))
                GetCShItemRet = null; // last minute failsafe
            return GetCShItemRet;
        }

        #region        FindCShItem --- various signatures of FindCShItem
        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="ptr">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 - most code in this function replaced by a call to FindCShItem(BaseItem as CShellItem, Abs as IntPtr)</remarks>
        public static CShellItem FindCShItem(IntPtr ptr)
        {
            return FindCShItem(GetDeskTop(), ptr);
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="Abs">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 -Function added to replace algorithm used in FindCShItem(ptr as IntPtr) which now only calls this routine.</remarks>
        public static CShellItem FindCShItem(CShellItem BaseItem, IntPtr Abs)
        {
            CShellItem FindCShItemRet = default;
            FindCShItemRet = null;
            if (CPidl.IsEqual(BaseItem.PIDL, Abs))
                return BaseItem;
            if (BaseItem.FilesInitialized && IsAncestorOf(BaseItem.PIDL, Abs, true))
            {
                foreach (CShellItem FItem in BaseItem.FileList)          // 7/2/2012 was BaseItem.Files
                {
                    if (CPidl.IsEqual(FItem.PIDL, Abs))
                        return FItem;
                }
            }
            if (BaseItem.FoldersInitialized)
            {
                foreach (CShellItem DItem in BaseItem.DirectoryList)     // 7/2/2012 was BaseItem.Directories
                {
                    if (CPidl.IsEqual(DItem.PIDL, Abs))
                        return DItem;
                    if (IsAncestorOf(DItem.PIDL, Abs))
                    {
                        return FindCShItem(DItem, Abs);
                    }
                }
            }

            return FindCShItemRet;
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="b">A Byte array representation of a Full or Absolute PIDL 
        /// referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks></remarks>
        public static CShellItem FindCShItem(byte[] b)
        {
            CShellItem FindCShItemRet = default;
            if (!CPidl.IsValidPidl(b))
                return null;
            var thisPidl = Marshal.AllocCoTaskMem(b.Length);
            if (thisPidl.Equals(IntPtr.Zero))
                return null;
            Marshal.Copy(b, 0, thisPidl, b.Length);
            FindCShItemRet = FindCShItem(thisPidl);
            Marshal.FreeCoTaskMem(thisPidl);
            return FindCShItemRet;
        }

        #endregion

        #endregion

        #region    Icomparable -- for default Sorting

        /// <summary>Computes the Sort key of this CShellItem, based on its attributes</summary>
        private int ComputeSortFlag()
        {
            int rVal = 0;
            if (m_IsDisk)
                rVal = 0x100000;
            if (m_TypeName.Equals(StrSystemFolder))
            {
                if (!m_IsBrowsable)
                {
                    rVal = rVal | 0x10000;
                    if (m_strMyDocuments.Equals(m_DisplayName))
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
        public virtual int CompareTo(object obj)
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
            if (!m_HasDispType)
                SetDispType();
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

        #region    Properties


        #region        Normal Properties


        // Private Shared m_ExtDict As New Dictionary(Of String, Integer)

        // ''' <summary>
        // ''' The following optimization of IconIndexNormal is a successful but invalid way of optimizing the initial fetch of
        // ''' IconIndexNormal. It is successful because it reduces Icon fetch time by 2/3 (2 seconds vs 6 seconds in 3000 file test dir on WHS1)
        // ''' but is invalid since all of a file type will have the same Icon - the first one seen - 
        // ''' this is really bad for .exe and .dll files and for certain image file types (eg .bmp, .ico, .png).
        // ''' These Icons in a normal Win7 (at least) system will actually be a view of the Image which is very handy for most purposes.
        // ''' The code avoids the trap of renamed link files, but cannot, without boosting the time and complexity, avoid the Image file
        // ''' problem. It is worth noting that .bmp and .png files display, each with a single image using the normal SystemImageListManager
        // ''' optimization - though .ico files show each with its' own unique icon - hmmm - probably need a different API call, or at least
        // ''' an additional flag bit set. TBD. Note that in .bmp and .png files with normal SystemImageListManager optimization show a 
        // ''' unique per type icon that is the old, regular icon.
        // ''' </summary>
        // ''' <value></value>
        // ''' <returns></returns>
        // ''' <remarks></remarks>
        // Public ReadOnly Property IconIndexNormal() As Integer
        // Get
        // If m_IconIndexNormal < 0 Then
        // If Not m_HasDispType Then SetDispType()
        // Dim shfi As New SHFILEINFO()
        // Dim dwflag As SHGFI = SHGFI.PIDL Or _
        // SHGFI.SYSICONINDEX
        // Dim dwAttr As Integer = 0
        // Dim Ext As String
        // If m_IsFileSystem And Not m_IsFolder Then
        // dwflag = dwflag Or SHGFI.USEFILEATTRIBUTES
        // dwAttr = FILE_ATTRIBUTE_NORMAL
        // Ext = IO.Path.GetExtension(m_DisplayName)
        // If m_ExtDict.ContainsKey(Ext) Then
        // m_IconIndexNormal = m_ExtDict(Ext)
        // End If
        // End If
        // If m_IconIndexNormal < 0 Then         'it won't be if set above
        // Dim H As IntPtr = SHGetFileInfo(m_Pidl, dwAttr, shfi, cbFileInfo, dwflag)
        // m_IconIndexNormal = shfi.iIcon
        // If Ext IsNot Nothing AndAlso Not Me.IsLink AndAlso Ext <> "" Then m_ExtDict.Add(Ext, m_IconIndexNormal) 'Only set if should be in ExtDict, but isn't yet
        // End If
        // End If
        // Return m_IconIndexNormal
        // End Get
        // End Property

        #endregion

        #region            FileInfo derived Properties

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
        public FileAttributes Attributes // Added 10/09/2011
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
                    var shfi = new SHFILEINFO() { dwAttributes = SFGAO.RDONLY };
                    var dwflag = SHGFI.PIDL | SHGFI.ATTRIBUTES | SHGFI.ATTR_SPECIFIED;
                    int dwAttr = 0;
                    var H = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, cbFileInfo, dwflag);
                    if (H.ToInt32() != NOERROR && H.ToInt32() != 1)
                    {
                        Marshal.ThrowExceptionForHR(H.ToInt32());
                    }
                    m_IsReadOnly = (shfi.dwAttributes & SFGAO.RDONLY) != 0;
                    m_SFGAO_Attributes = m_SFGAO_Attributes | shfi.dwAttributes & SFGAO.RDONLY;
                    m_IsReadOnlySetup = true;
                    return m_IsReadOnly;
                }
            }
        }

        private bool _IsSystem_HaveSysInfo = default;
        private bool _IsSystem_m_IsSystem = default;
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

        #endregion

        #endregion

        #endregion


        #region Public Properties

        /// <summary>
        /// Property used to store information returned by FindFirstFile/FindNextFile API call.
        /// </summary>
        /// <returns>The current value or Nothing if not set</returns>
        /// <remarks>Used to optimize the fetching of information otherwise only easily available from FileInfo/DirectoryInfo.</remarks>
        public W32Find_Data W32Data
        {
            get
            {
                return m_W32Data;
            }
            set
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
                if (!m_HasDispType)
                    SetDispType();
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
                if (!m_HasDispType)
                    SetDispType();
                return m_DisplayName;
            }
        }

        /// <summary>
        /// Name is another way of obtaining the DisplayName
        /// </summary>
        /// <returns>The DisplayName of the Item</returns>
        /// <remarks>For a link file (xxx.txt.lnk for example) the
        /// DisplayName property will return xxx.txt</remarks>
        public string Name
        {
            get
            {
                if (!m_HasDispType)
                    SetDispType();
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
                if (!m_HasDispType)
                    SetDispType();
                return m_TypeName;
            }
        }

        /// <summary>
        /// Contains the PIDL for the current instance as an IntPtr
        /// </summary>
        public IntPtr PIDL
        {
            get
            {
                return m_Pidl;
            }
        }


        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        public IShellFolder Folder
        {
            get
            {
#if DEBUG
                var name = ShellHelper.GetShellFolderDisplayName(m_Folder);
#endif
                if (m_UpdateFolder)
                {
                    if (m_Folder is not null)
                        Marshal.ReleaseComObject(m_Folder);
                    m_Folder = ShellHelper.GetFolder(Parent, ILFindLastID(m_Pidl));
                    m_UpdateFolder = false;
                }
                return m_Folder;
            }
        }

        /// <summary>
        /// Contains the Full Path and file name of the instance as obtained from Folder.GetDisplayNameOf
        /// </summary>
        public string FullPath
        {
            get
            {
                if (m_Path.Equals(string.Empty))
                {
                    SetPath();
                }
                return m_Path;
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

        /// <summary>
        /// For internal use only
        /// </summary>
        public bool FoldersInitialized
        {
            get
            {
                return m_Directories is not null;
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        public bool FilesInitialized
        {
            get
            {
                return m_Files is not null;
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        public CShellItemCollection DirectoryList
        {
            get
            {
                return m_Directories;
            }
        }

        /// <summary>
        /// Returns an Array of CShItems containing the sub Directories of this instance.
        /// </summary>
        /// <returns>Array of CShItems containing the sub Directories of this instance.</returns>
        public CShellItem[] Directories
        {
            get
            {
                if (!m_IsFolder)
                {
                    return (CShellItem[])Array.CreateInstance(typeof(CShellItem), 0);    // mod 6/27/09
                }
                else if (m_Directories == null)
                {
                    m_Directories = GetContents(SHCONTF.FOLDERS | SHCONTF.INCLUDEHIDDEN);
                }
                else
                {
                    // **********Comment by Lukai-2021.12.02, otherwise the rename function doesn't work, but after comment, it will affects tree updating, however performance is better
                    // Me.UpdateRefresh(False, True)   '6/30/2012 - Note that it is also true that in some circumstances Windows does not post a RMDIR when Folders are removed.
                }        // 6/30/2012 - Under some circumstances, Windows does not post MKDIR msgs when Folders are created!!! Do a refresh to ensure we are up to date
                return m_Directories.ToArray();
            }
        }
        /// <summary>
        /// Returns the number of Folders currently known to this instance. If not
        /// initialized, return 0
        /// </summary>
        /// <returns>The number of Folders currently known to this instance. If not
        /// initialized, return 0</returns>
        /// <remarks>Property added 02/10/2014 to avoid UpdateRefresh</remarks>
        public int DirCount => FoldersInitialized ? m_Directories.Count : 0;
        /// <summary>
        /// Returns the number of Files currently known to this instance. If not
        /// initialized, return 0
        /// </summary>
        /// <returns>The number of Files currently known to this instance. If not
        /// initialized, return 0</returns>
        /// <remarks>Property added 02/10/2014 to avoid UpdateRefresh</remarks>
        public int FileCount => FilesInitialized ? m_Files.Count : 0;


        /// <summary>
        /// Returns an Array of CShItems containing the Files contained in this instance.
        /// </summary>
        /// <returns>Array of CShItems containing the Files contained in this instance.</returns>
        public CShellItem[] Files
        {
            get
            {
                if (!m_IsFolder)
                {
                    return (CShellItem[])Array.CreateInstance(typeof(CShellItem), 0);    // mod 6/27/09
                }
                else if (m_Files == null)
                {
                    m_Files = GetContents(SHCONTF.NONFOLDERS | SHCONTF.INCLUDEHIDDEN);
                }
                else        // 6/30/2012 - Under some circumstances, Windows does not post CREATE msgs when Files are created!!! Do a refresh to ensure we are up to date
                {
                    UpdateRefresh(true, false);
                }   // 6/30/2012 - Note that it is also true that in some circumstances Windows does not post a DELETE when Files are removed.
                return m_Files.ToArray();
            }
        }

        /// <summary>
        /// Contains the CShellItem of this instance's Parent Folder
        /// </summary>
        /// <returns>CShellItem of this instance's Parent Folder</returns>
        /// <remarks>Returns Nothing for the Desktop which has no Parent</remarks>
        public CShellItem Parent
        {
            get
            {
                return m_Parent;
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        public void SetParent(CShellItem parent)
        {
            m_Parent = parent;
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

        private bool m_HasSubFoldersSetup;

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
                if (m_HasSubFoldersSetup)
                {
                    return m_HasSubFolders;
                }
                else if (m_IsRemote)
                {
                    m_HasSubFolders = true;
                    m_HasSubFoldersSetup = true;
                }
                else
                {
                    var psfi = new SHFILEINFO() { dwAttributes = SFGAO.HASSUBFOLDER };
                    var uFlags = SHGFI.PIDL | SHGFI.ATTRIBUTES | SHGFI.ATTR_SPECIFIED;
                    int dwAttr = 0;
                    var H = SHGetFileInfo(m_Pidl, dwAttr, ref psfi, cbFileInfo, uFlags);
                    if (H.ToInt32() != NOERROR && H.ToInt32() != 1)
                    {
                        Marshal.ThrowExceptionForHR(H.ToInt32());
                    }
                    m_HasSubFolders = (psfi.dwAttributes & SFGAO.HASSUBFOLDER) != 0;
                    m_SFGAO_Attributes = m_SFGAO_Attributes | psfi.dwAttributes & SFGAO.HASSUBFOLDER;
                    m_HasSubFoldersSetup = true;
                }
                return m_HasSubFolders;
            }
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
                    GetSize();
                }
                return m_size;
            }
        }

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
                    if (!m_HasDispType)
                        SetDispType();
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
                    if (!m_HasDispType)
                        SetDispType();
                    if (!m_IsDisk & m_IsFileSystem & m_IsFolder)
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
        }

        #region        Drag Ops Properties

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
        public bool IsDropTarget => m_IsDropTarget;

        #endregion

        #region        Shared Properties
        /// <summary>
        /// Contains a String with the Local representation of "My Computer"
        /// </summary>
        public static string StrMyComputer => m_strMyComputer;
        /// <summary>
        /// Contains a String with the Local representation of "System Folder".
        /// </summary>
        public static string StrSystemFolder => m_strSystemFolder;
        /// <summary>
        /// Contains a String with the Full Path of the Desktop Directory
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        public static string DesktopDirectoryPath => m_DeskTopDirectory?.FullPath;

        #endregion

        #endregion


        #region    Public Methods

        #region    Shared Public Methods


        /// <summary>
        /// If not initialized, then build DesktopBase
        /// once done, or if initialized already, returns DestopBase
        /// </summary>
        /// <returns>The DesktopBase CShellItem representing the desktop</returns>
        public static CShellItem GetDeskTop()
        {
            if (DesktopBase == null)
            {
                DesktopBase = new CShellItem();
            }
            return DesktopBase;
        }

        /// <summary>True if parameter "ancestor" is an ancestor of parameter "current" 
    /// </summary>
    /// <returns>IsAncestorOf returns True if input CShellItem ancestor is an ancestor of input CShellItem current</returns>
    /// <remarks>if OS is Win2K or above, uses the ILIsParent API, otherwise uses the
    /// cPidl function StartsWith.  This is necessary since ILIsParent in only available
    /// in Win2K or above systems AND StartsWith fails on some folders on XP systems (most
    /// obviously some Network Folder Shortcuts, but also Control Panel. Note, StartsWith
    /// always works on systems prior to XP.<br />
    /// NOTE: if ancestor and current reference the same Item, both
    /// methods return True</remarks>
        public static bool IsAncestorOf(CShellItem ancestor, CShellItem current, bool fParent = false)
        {
            return IsAncestorOf(ancestor.PIDL, current.PIDL, fParent);
        }
        /// <summary> Compares a candidate Ancestor PIDL with a Child PIDL and
        /// returns True if Ancestor is an ancestor of the child.
        /// if fParent is True, then only return True if Ancestor is the immediate
        /// parent of the Child</summary>
        /// <param name="AncestorPidl">The Absolute PIDL that is the candidate for being an Ancestor of ChildPidl.</param>
        /// <param name="ChildPidl">The Absolute PIDL whose ancestory is being searched for.</param>
        /// <param name="fParent">A flag. If True, then only return True if AncestorPidl is the immediate Parent of ChildPidl.</param>
        /// <returns>True if AncestorPidl is an ancestor of ChildPidl.
        ///          If fParent is False then will also return True if AncestorPidl and ChildPidl are equal. 
        ///          If fParent is True, <i>only</i> returns True if AncestorPidl is the Parent of ChildPidl</returns>
        ///          
        public static bool IsAncestorOf(IntPtr AncestorPidl, IntPtr ChildPidl, bool fParent = false)
        {
            bool IsAncestorOfRet = default;
            if (Is2KOrAbove())
            {
                return ILIsParent(AncestorPidl, ChildPidl, fParent);
            }
            else
            {
                var Child = new CPidl(ChildPidl);
                var Ancestor = new CPidl(AncestorPidl);
                IsAncestorOfRet = Child.StartsWith(Ancestor);
                if (!IsAncestorOfRet)
                    return IsAncestorOfRet;
                if (fParent) // check for immediate ancestor, if desired
                {
                    object[] oAncBytes = Ancestor.Decompose();
                    object[] oChildBytes = Child.Decompose();
                    if (oAncBytes.Length != oChildBytes.Length - 1)
                    {
                        IsAncestorOfRet = false;
                    }
                }
            }

            return IsAncestorOfRet;
        }

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
                foreach (CShellItem currentCItem in cStart.FileList)
                {
                    cItem = currentCItem;       // 7/2/2012 used Files
                    if (!cback(cItem, UserLevel, Tag))
                    {
                        return false;        // user said stop
                    }
                }
                // then process all dirs in this directory, recursively
                foreach (CShellItem currentCItem1 in cStart.DirectoryList)
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
        /// For internal use only
        /// </summary>
        internal void AddItem(CShellItem item)
        {
            bool Changed = false;
            lock (LockObj)
            {
                try
                {
                    item.m_Parent = this;
                    if (item.IsFolder)
                    {
                        if (FoldersInitialized && !m_Directories.Contains(item.PIDL))
                        {
                            m_Directories.Add(item);
                            Changed = true;
                        }
                    }
                    else if (FilesInitialized && !m_Files.Contains(item.PIDL))
                    {
                        m_Files.Add(item);
                        Changed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in CShellItem.AddItem -- " + ex.ToString());
                }
            }
            if (Changed)
            {
                CShItemUpdate?.Invoke(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Created));
            }
        }

        /// <summary>
        /// For internal use only
        /// </summary>
        internal void RemoveItem(CShellItem item)
        {
            bool Changed = false;
            lock (LockObj)
            {
                try
                {
                    if (item.IsFolder)
                    {
                        if (FoldersInitialized && m_Directories.Contains(item))
                        {
                            // Debug.WriteLine("Removing " & item.Path & " From " & Me.Path)
                            m_Directories.Remove(item);
                            Changed = true;
                        }
                    }
                    else if (FilesInitialized && m_Files.Contains(item))
                    {
                        m_Files.Remove(item);
                        Changed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in CShellItem.RemoveItem -- " + ex.ToString());
                }
            }
            if (Changed)
            {
                CShItemUpdate?.Invoke(this, new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted));
            }
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
            lock (LockObj)
            {
                if (ClearFiles && m_Files is not null)
                {
                    m_Files.Clear();
                    m_Files = null;
                }
                if (ClearDirectories && m_Directories is not null)
                {
                    m_Directories.Clear();
                    m_Directories = null;
                }
            }
        }


        /// <summary>
    /// Stops monitoring of changes to the File System.
    /// </summary>
    /// <returns>True if Successful, False otherwise</returns>
    /// <remarks>Global Change Notification is started by default. Call this function to turn it off.
    ///          Only turn Notification Off under rare, well understood circumstances. If turned off, NO
    ///          changes, including those made by the application will be noticed.</remarks>
        public bool StopGlobalNotification()
        {
            bool StopGlobalNotificationRet = default;
            StopGlobalNotificationRet = false;        // assume failure
            if (!ReferenceEquals(this, DesktopBase))
                return StopGlobalNotificationRet;
            if (m_updater is null)
            {
                StopGlobalNotificationRet = true;     // Already stopped
                return StopGlobalNotificationRet;
            }
            m_updater.Dispose();
            m_updater = null;
            StopGlobalNotificationRet = true;
            return StopGlobalNotificationRet;
        }

        /// <summary>
        /// Restarts the Dynamic Update listening for Windows Notify messages
        /// </summary>
        /// <returns>True if successful, False otherwise</returns>
        /// <remarks>Resumesthe detection of changes to the FileSystem after a StopGlobalNotification call.
        ///          Changes between that call and a restart will be lost.</remarks>
        public bool StartGlobalNotification()
        {
            bool StartGlobalNotificationRet = default;
            StartGlobalNotificationRet = false;       // assume failure
            if (!ReferenceEquals(this, DesktopBase))
                return StartGlobalNotificationRet;
            if (m_updater is not null)
            {
                StartGlobalNotificationRet = true;        // Already started
                return StartGlobalNotificationRet;
            }
            m_updater = new CShellItemUpdater(this);
            if (m_updater is not null)
            {
                StartGlobalNotificationRet = true;
            }

            return StartGlobalNotificationRet;
        }

        /// <summary>
    /// Returns the sub-directories of the current instance, if the current instance is a
    /// Folder. Similar to to Property Directories except that it returns the Directories
    /// as an ArrayList.
    /// </summary>
    /// <returns>If the current instance is a Folder, returns its sub-directories as an 
    /// ArrayList containing the CShItems of its sub-directories. Returns an empty list if
    /// there are no sub-directories. Returns Nothing if the current instance is not a Folder.</returns>
    /// <remarks></remarks>
        public ArrayList GetDirectories()
        {
            CShellItem[] D = Directories;         // 7/2/2012 OK to use Directories in this case
            if (D is null)
                return null;
            var AL = new ArrayList();
            AL.AddRange(D);
            return AL;
        }

        /// <summary>
    /// If the current instance is a Folder then returns an ArrayList of the CShItems of Files 
    /// contained in the current instance. Otherwise returns Nothing.
    /// </summary>
    /// <returns>An ArrayList of the CShItems of the Files in the current instance. If the 
    /// current instance is not a Folder, returns Nothing. If there are no Files in the 
    /// current instance, returns an empty ArrayList.</returns>
    /// <remarks></remarks>
        public ArrayList GetFiles()
        {
            CShellItem[] F = Files;
            if (F is null)
                return null;
            var AF = new ArrayList();
            AF.AddRange(F);
            return AF;
        }

        /// <summary>
    /// Returns the Files of this sub-folder, filtered by a filtering string, as an
    ///   ArrayList of CShitems
    /// </summary>
    /// <param name="Filter">A filter string (for example: *.Doc)</param>
    /// <returns>An ArrayList of CShItems. May return an empty ArrayList if there are none.</returns>
    /// <remarks>Added 8/22/2012</remarks>
        public ArrayList GetFiles(string Filter)
        {
            ArrayList GetFilesRet = default;
            GetFilesRet = new ArrayList();
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

        /// <summary>
    /// Returns the Directories and Files of this sub-folder as a sorted
    ///   ArrayList of CShitems
    /// </summary>
    /// <returns>An ArrayList of CShItems. May return an empty ArrayList if there are none.</returns>
    /// <remarks>This version is the Optimized version added after any distribution of v2.14</remarks>
        public ArrayList GetItems()
        {
            ArrayList GetItemsRet = default;
            var rVal = new ArrayList();
            if (m_IsFolder)
            {
                var Flags = SHCONTF.INCLUDEHIDDEN;

                lock (LockObj)
                {
                    if (m_Directories is null)
                        Flags = Flags | SHCONTF.FOLDERS;
                    if (m_Files is null)
                        Flags = Flags | SHCONTF.NONFOLDERS;
                    if (Flags != SHCONTF.INCLUDEHIDDEN) // if already have both already, just report what we have
                    {
                        var Items = GetContents(Flags);
                        GetItemsRet = new ArrayList(Items.Count);       // Actual expected return
                        var Dirs = new ArrayList(Items.Count);      // trade space for time - capacity set to max possible
                        var Files = new ArrayList(Items.Count);     // trade space for time - capacity set to max possible
                        foreach (CShellItem Item in Items)
                        {
                            if (Item.IsFolder)
                            {
                                Dirs.Add(Item);
                            }
                            else
                            {
                                Files.Add(Item);
                            }
                        }
                        if (m_Directories is null)
                        {
                            m_Directories = new CShellItemCollection(this);   // First time we even asked
                            m_Directories.AddRange(Dirs);
                        }
                        if (m_Files is null)
                        {
                            m_Files = new CShellItemCollection(this);         // First time we even asked
                            m_Files.AddRange(Files);
                        }
                    }
                    rVal.AddRange(m_Directories);    // 7/14/2012 - trust in SyncLock
                    rVal.AddRange(m_Files);          // 7/14/2012 - trust in SyncLock
                                                     // rVal.AddRange(Me.Directories)   'use this instead of local list as a last sanity precaution and to prevent race conditions
                                                     // rVal.AddRange(Me.Files)         'use this instead of local list as a last sanity precaution and to prevent race conditions
                }                        // should have prevented race conditions, but Windows messages can be funky
                rVal.Sort();
            }
            return rVal;
        }

        // Previous, unoptimized version of GetItems
        // Public Function GetItems() As ArrayList
        // Dim rVal As New ArrayList()
        // If m_IsFolder Then
        // rVal.AddRange(Me.Directories)
        // rVal.AddRange(Me.Files)
        // rVal.Sort()
        // Return rVal
        // Else
        // Return rVal
        // End If
        // End Function

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
                        var SB = new StringBuilder(WinSDK.MAX_PATH);
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
            Debug.WriteLine("\tPath        = " + m_Path);
            Debug.WriteLine("\tTypeName    = " + TypeName);
            Debug.WriteLine("\tiIconNormal = " + m_IconIndexNormal);
            Debug.WriteLine("\tiIconSelect = " + m_IconIndexOpen);
            Debug.WriteLine("\tIsBrowsable = " + m_IsBrowsable);
            Debug.WriteLine("\tIsFileSystem= " + m_IsFileSystem);
            Debug.WriteLine("\tIsFolder    = " + m_IsFolder);
            Debug.WriteLine("\tIsLink    = " + m_IsLink);
            Debug.WriteLine("\tIsDropTarget = " + m_IsDropTarget);
            Debug.WriteLine("\tIsReadOnly   = " + IsReadOnly);
            Debug.WriteLine("\tCanCopy = " + CanCopy);
            Debug.WriteLine("\tCanLink = " + CanLink);
            Debug.WriteLine("\tCanMove = " + CanMove);
            Debug.WriteLine("\tCanDelete = " + CanDelete);
            if (m_IsFolder)
            {
                if (!(m_Directories == null))
                {
                    Debug.WriteLine("\tDirectory Count = " + m_Directories.Count);
                }
                else
                {
                    Debug.WriteLine("\tDirectory Count Not yet set");
                }
            }
        }
        
        /// <summary>
    /// This method uses the CreateViewObject method of IShellFolder to obtain the IDropTarget of this
    /// CShellItem instance. 
    /// </summary>
    /// <param name="tn">The control in which the GUI representation of this CShellItem lives.</param>
    /// <returns>If successful, the IDropTarget interface of the Folder represented by this CShellItem.
    /// If unsuccessful, returns Nothing.</returns>
    /// <remarks>A similar function exists in the ShellHelper class. GetDropTargetOf is more efficient.</remarks>
        public Shell.IDropTarget GetDropTargetOf(Control tn)
        {
            if (Folder == null)
                return null;
            IntPtr pInterface = IntPtr.Zero;
            Shell.IDropTarget theInterface;
            var tnH = tn.Handle;
            if (Folder.CreateViewObject(tnH, ShellAPI.IID_IDropTarget, ref pInterface) == S_OK)
            {
                theInterface = (Shell.IDropTarget)Marshal.GetTypedObjectForIUnknown(pInterface, typeof(Shell.IDropTarget));
                return theInterface;
            }
            else
            {
                return null;
            }
        }

        #region        Update Methods

        /// <summary>
        /// CShItemUpdate is the Event Raised to notify the using application, typically the GUI portion, of changes made to
        /// Folders and Files that the application has an interest in.<br />
        /// See <see cref="WindowsApiLib.ShellItemUpdateEventArgs.UpdateType">UpdateType</see> for details.
        /// </summary>
        /// <param name="sender">The CShellItem of the Folder that has changes in its' content.</param>
        /// <param name="e">A <see cref="ShellItemUpdateEventArgs">ShellItemUpdateEventArgs</see> which provides information about the change.</param>
        /// <remarks></remarks>
        public static event CShItemUpdateEventHandler CShItemUpdate;

        public delegate void CShItemUpdateEventHandler(object sender, ShellItemUpdateEventArgs e);

        public void Refresh()
        {
            Update(IntPtr.Zero, CShItemUpdateType.Updated);
        }

        /// <summary>
        /// The UpdateRefresh function compares the Current content of the Folder with the
        /// current state of m_Directories and m_Files, adding/deleting CShItems as appropriate  (thus causing
        /// appropriate events to be raised for listening clients. 
        /// Called internally to handle WM_UPDATEDIR messages which map to CShItemUpdateType.UpdateDir. 
        /// This message indicates that the Contents of this Folder has changed.  Typically, it is fired 
        /// when multiple items are added/deleted. In practice, several explicit add/delete notification 
        /// messages are fired followed by WM_UPDATEDIR to indicate that there are more changes. 
        /// Certain other types of file operations (eg Save) use only WM_UPDATEDIR rather than WM_CREATE.
        /// </summary>
        /// <param name="UpdateFiles">True to examine Files of this folder for changes.</param>
        /// <param name="UpdateFolders">True to examine sub-directories of this folder for changes.</param>
        /// <returns>True if changes have been made, False otherwise</returns>
        /// <remarks>If m_Directories or m_Files is Nothing, then no attempt is made to compare with current 
        /// contents.  That is, if m_files is Nothing then it is not updated, m_Directories is treated the same.
        /// Note that m_xxxx.Count=0 is not the same thing as m_xxxx is Nothing! m_xxxx = Nothing means
        /// no one cares about the content.  m_xxxx.Count = 0 means that someone does care, but there were 
        /// no such items known until (perhaps) now.</remarks>
        /// <summary>
        /// Refreshes the information for this item from the shell and raises an Update event.
        /// </summary>
        public bool UpdateRefresh(bool UpdateFiles = true, bool UpdateFolders = true)
        {
            bool UpdateRefreshRet = default;
            UpdateRefreshRet = false;
            if (m_IsFolder)
            {
                lock (LockObj)
                {
                    var attrFlag = SHCONTF.INCLUDEHIDDEN;
                    if (m_Files is not null && UpdateFiles)
                        attrFlag = attrFlag | SHCONTF.NONFOLDERS;
                    if (m_Directories is not null && UpdateFolders)
                        attrFlag = attrFlag | SHCONTF.FOLDERS;
                    if (attrFlag == SHCONTF.INCLUDEHIDDEN)
                        return UpdateRefreshRet; // nothing expanded therefore no change

                    var InvalidItems = new List<CShellItem>();              // Holds CShItems no longer present
                    var curPidls = GetContentPtrs(attrFlag);                // Relative PIDLs of current content
                    var tmpCurrent = new List<IntPtr>((IEnumerable<IntPtr>)curPidls.ToArray(typeof(IntPtr)));  // working list of current content
                    if (curPidls.Count < 1)                                 // no items currently in Folder, so mark any previously known as invalid
                    {
                        if (m_Files is not null && UpdateFiles)
                            InvalidItems.AddRange(m_Files.ToArray());
                        if (m_Directories is not null && UpdateFolders)
                            InvalidItems.AddRange(m_Directories.ToArray());
                    }
                    else            // there are currently some items of interest in Me.Folder
                    {
                        var tmpItems = new List<CShellItem>();              // working list of old known items
                        if (m_Directories is not null && UpdateFolders)
                            tmpItems.AddRange(m_Directories.ToArray());
                        if (m_Files is not null && UpdateFiles)
                            tmpItems.AddRange(m_Files.ToArray());
                        var oldPidls = new IntPtr[tmpItems.Count];          // working list of relative pidls of known items
                        for (int i = 0, loopTo = tmpItems.Count - 1; i <= loopTo; i++)
                            oldPidls[i] = ILFindLastID(tmpItems[i].PIDL);
                        for (int iold = 0, loopTo1 = oldPidls.Length - 1; iold <= loopTo1; iold++)
                        {
                            for (int icur = tmpCurrent.Count - 1; icur >= 0; icur -= 1) // 5/21/2012 changed to bottom-up loop
                            {
                                // 5/23/2012 revised the following block of code to also check vs AreBytesEqual
                                if (CPidl.IsEqual(oldPidls[iold], tmpCurrent[icur]))    // found the same item
                                {
                                    if (!ReferenceEquals(this, m_Recycle) && !CPidl.AreBytesEqual(oldPidls[iold], tmpCurrent[icur]))  // 7/14/2012
                                    {
                                        // in this case, some aspect besides name has changed treat as UpdateItem for the old one
                                        var UpdCSI = tmpItems[iold];
                                        // Debug.WriteLine("***Raising Updated based on AreBytesEqual - " & UpdCSI.Name)
                                        UpdCSI.ResetInfo();
                                        if (UpdCSI.IsFolder)
                                        {
                                            UpdCSI.ResetChildren();
                                        }
                                        CShItemUpdate?.Invoke(UpdCSI.Parent, new ShellItemUpdateEventArgs(UpdCSI, CShItemUpdateType.Updated)); // 6/3/2012
                                        UpdateRefreshRet = true;        // 5/24/2012  
                                    }
                                    // either way, we have found the matching PIDL so continue with the next "old" one (in tree)
                                    tmpCurrent.RemoveAt(icur); // Have match, don't look at this one again - and do not add it in the following code
                                    goto NXTOLD;
                                }
                                // 5/23/2012 end of revised code
                            }
                            // falling thru here means couldn't find iold entry
                            InvalidItems.Add(tmpItems[iold]);
                        NXTOLD:
                            ;
                        }
                    }
                    // any not found should be removed from my collections (raising event)
                    if (InvalidItems.Count > 0)
                    {
                        UpdateRefreshRet = true;
                        foreach (var csi in InvalidItems)
                            RemoveItem(csi);
                    }
                    // anything remaining in tmpcurrent is a new entry Add it (raising event)
                    if (tmpCurrent.Count > 0)
                    {
                        UpdateRefreshRet = true;
                        foreach (IntPtr iptr in tmpCurrent)   // these are relative PIDLs
                        {
                            try                                 // ASUS Fix
                            {
                                var NewItem = new CShellItem(iptr, this);  // 11/13/2013
                                AddItem(NewItem);                                // 11/13/2013
                            }
                            catch (Exception ex)               // ASUS Fix - modified 11/13/2013 was only looking for InvalidCastExcepton
                            {
                            }                             // ASUS Fix
                        }
                    }
                    // we obtained some new relative PIDLs in curPidls, so free them
                    foreach (IntPtr itm in curPidls)
                        Marshal.FreeCoTaskMem(itm);
                    // 6/18/2012 - If something changed in this Folder, then Raise an Updated Event AFTER all Adds, Deletes, etc have been posted
                    // 6/18/2012 - One was previously Raised when working down the Tree from Me's Parent, but Adds, Deletes, etc details had not been posted
                    // 6/18/2012 - at that time. The App did not know HOW this Folder had changed (except for attributes)
                    if (UpdateRefreshRet && IsFolder)
                    {
                        if (Parent is null)
                        {
                            CShItemUpdate?.Invoke(GetDeskTop(), new ShellItemUpdateEventArgs(this, CShItemUpdateType.Updated));
                        }
                        else
                        {
                            CShItemUpdate?.Invoke(Parent, new ShellItemUpdateEventArgs(this, CShItemUpdateType.Updated));
                        }
                    }
                }
            }

            return UpdateRefreshRet;
        }

        #endregion

        #endregion


        #endregion


        #region    Private Methods

        private void ResetInfo()
        {
            m_HasDispType = false;
            m_IsReadOnlySetup = false;
            m_XtrInfo = false;
            m_HasSubFoldersSetup = false;
            if (W32Data is not null && W32Data is W32Find_Data)
                W32Data = null;
            ResetIconIndex();
        }
        
        private void ResetChildren()
        {
            // propogate changes to the known children
            if (m_Files is not null)
            {
                foreach (CShellItem item in m_Files)
                    item.ResetInfo();
            }
            if (m_Directories is not null)
            {
                foreach (CShellItem item in m_Directories)
                    item.ResetInfo();
            }
        }

        #region        UpdateRefresh

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
        private void UpdateFolderPidlAndPath()
        {
            m_Path = string.Empty;             // will update when needed
            IntPtr newPidl;
            newPidl = CPidl.ConcatPidls(Parent.PIDL, ILFindLastID(PIDL));
            Marshal.FreeCoTaskMem(m_Pidl);
            m_Pidl = newPidl;
            if (IsFolder)
            {
                UpdateFolder = true;                // 05/22/2015 Where it should have always been done
                if (m_Files is not null)
                {
                    foreach (CShellItem item in m_Files)
                        item.UpdateFolderPidlAndPath();
                }
                if (m_Directories is not null)
                {
                    foreach (CShellItem item in m_Directories)
                        // item.UpdateFolder = True       '05/22/2015 Relocated this
                        item.UpdateFolderPidlAndPath();
                }
            }
        }

        /// <summary>For internal use only<br />
        /// Update is called by the CShItemUpdater Class when that Class receives a WM_Notify message. The purpose of this Class is to
        /// translate the information passed to it into the appropriate set of actions needed to maintain the internal cache and to,
        /// directly or indirectly (thru the routines it calls), Raise CShItemUpdate events to notify the using application of changes.
        /// </summary>
        /// <param name="newPidl">The absolute PIDL of the affected item. The definition of "affected item" varies with the type of
        ///                       change being reported.</param>
        /// <param name="changeType">The type of change.</param>
        /// <remarks>Serves as a bridge between CShItemUpdater and the CShellItem that should handle a change.</remarks>
        internal void Update(IntPtr newPidl, CShItemUpdateType changeType)
        {
            Debug.WriteLine("Entered Update: " + changeType.ToString());
            switch (changeType)
            {
                case CShItemUpdateType.Renamed:      // Item has been renamed or moved
                    {

                        IntPtr newParent, newPidlRel = IntPtr.Zero;
                        IntPtr PidlRel = IntPtr.Zero, newFolderPtr = IntPtr.Zero;
                        newParent = CPidl.TrimPidl(newPidl, ref newPidlRel);
                        var oldParentItem = Parent;    // Save in case "renamed" to a new directory
                        var newParentItem = FindCShItem(newParent);
                        if (newParentItem is null)            // renamed to a dir that is not yet in internal tree
                        {
                            Parent.RemoveItem(this);                // no longer in this Folder
                            m_Parent = null;                      // and therefore no longer in tree
                        }
                        else if (SHGetRealIDL(newParentItem.Folder, newPidlRel, out PidlRel) == S_OK)            // new parent of this item IS in internal tree, fix up and update any files/folders of THIS item
                        {
                            Marshal.FreeCoTaskMem(m_Pidl);
                            m_Pidl = CPidl.ConcatPidls(newParent, PidlRel);  // we use PidlRel because newPidlRel is a "simple" PIDL rather than a regular 1-item SHITEMID
                            if (IsFolder)            // deal with potential "Move" to a new dir
                            {
                                if (!ReferenceEquals(newParentItem, Parent))
                                {
                                    Parent.RemoveItem(this);
                                    newParentItem.AddItem(this);
                                }
                                ResetInfo();
                                SetPath();
                                if (newParentItem.Folder.BindToObject(PidlRel, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref newFolderPtr) == S_OK)
                                {
                                    Marshal.ReleaseComObject(Folder);
                                    m_Folder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(newFolderPtr, typeof(IShellFolder));
                                    Marshal.Release(newFolderPtr);
                                    if (m_Files is not null)
                                    {
                                        foreach (CShellItem item in m_Files)
                                            item.UpdateFolderPidlAndPath();
                                    }
                                    if (m_Directories is not null)
                                    {
                                        foreach (CShellItem item in m_Directories)
                                            item.UpdateFolderPidlAndPath();
                                    }
                                }
                            }
                            else if (!ReferenceEquals(oldParentItem, newParentItem))
                            {
                                if (oldParentItem.FilesInitialized)    // deal with potential "Move" to a new dir
                                {
                                    oldParentItem.RemoveItem(this);
                                }
                                if (newParentItem.FilesInitialized)
                                {
                                    newParentItem.AddItem(this);
                                    ResetInfo();         // new since sent to others
                                    SetPath();           // new since sent to others
                                }
                                else
                                {
                                    m_Parent = null;
                                    ResetInfo();
                                }         // new since sent to others
                            }
                            else                    // Added for fix to the fix
                            {
                                ResetInfo();         // Added for fix to the fix
                                SetPath();
                                // ResetInfo()         'newly deleted since sent to others
                                // SetPath()           'newly deleted since sent to others
                            }           // Added for fix to the fix
                                        // Not oldParentItem Is newParentItem
                        }   // SHGetRealIDL = S_OK
                            // Check for New ParentDir in internal Tree
                            // Note: FreeCoTaskMem will ignore IntPtr.Zero
                        Marshal.FreeCoTaskMem(PidlRel);
                        Marshal.FreeCoTaskMem(newParent);
                        Marshal.FreeCoTaskMem(newPidlRel);
                        CShItemUpdate?.Invoke(oldParentItem, new ShellItemUpdateEventArgs(this, changeType));
                        break;
                    }
                case CShItemUpdateType.UpdateDir: // raised when content of a dir changes
                    {
                        DoUpdateDir(this); // recursively check this Folder and all known sub-Folders for change     '5/21/2012
                        break;
                    }

                case CShItemUpdateType.Updated: // raised when Attributes (Item or Items under a Folder) change
                    {
                        // Debug.WriteLine("Updated for " & Me.Path)
                        ResetInfo();
                        // Previous versions called ResetChildren. Changed to UpdateRefresh - which impacts performance.
                        // Decided for now (6/12/2012) to do neither, so commented it out. This message is often closely followed or preceeded
                        // by an UPDATEDIR which will, in fact call UpdateRefresh which will also call ResetChildren in many cases.
                        // Performance impact is greatly aggravated by the (common on Win7) closely paired UPDATEDIR and UPDATEITEM messages
                        // on the same Folder, caused by the same change! Removing this code limits the impact.
                        // If Me.IsFolder Then
                        // 'Me.ResetChildren()     'Original code
                        // 'Me.UpdateRefresh()     '6/3/2012
                        // End If
                        CShItemUpdate?.Invoke(Parent, new ShellItemUpdateEventArgs(this, changeType));
                        break;
                    }
                case CShItemUpdateType.IconChange:
                    {
                        // Debug.WriteLine("IconChange for " & Me.Path)
                        ResetInfo();
                        CShItemUpdate?.Invoke(Parent, new ShellItemUpdateEventArgs(this, changeType));
                        break;
                    }
                case CShItemUpdateType.MediaChange:          // CD/DVD/External Drive/Etc Added or Removed
                    {
                        // Debug.WriteLine("MediaChange for " & Me.Path)
                        ClearItems(true, true);
                        ResetInfo();
                        SetPath();
                        CShItemUpdate?.Invoke(Parent, new ShellItemUpdateEventArgs(this, changeType));
                        break;
                    }
            }
        }

        private void DoUpdateDir(CShellItem CSI)     // 5/21/2012
        {
            if (ReferenceEquals(CSI, m_Recycle))
                return; // 6/21/2012
            CSI.UpdateRefresh();
            if (CSI.m_Directories is not null)
            {
                foreach (CShellItem FolderItem in CSI.m_Directories) // 02/18/2014 Using Directories here is redundant, causing an extra UpdateRefresh
                    DoUpdateDir(FolderItem);
            }
        }

        /// <summary>
        /// Obtains information available from FileInfo. Uses data from W32Data rather than FileInfo/DirectoryInfo if W32Data is present.
        /// </summary>
        private void FillDemandInfo()
        {
            if (m_W32Data is not null)  // 04/24/2012 - changed to use m_W32Data rather than .Tag
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
            m_XtrInfo = true;            // 05/15/2012 even if there were errors, we have what we can get (long file name problem)
        }

        /// <summary>
        /// Returns the requested Items of this Folder as a CShitemCollection
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        private CShellItemCollection GetContents(SHCONTF flags)
        {
            var rVal = new CShellItemCollection(this);
            if (Folder is null)
                return rVal; // Added 10/22/2011 to deal with certain Virtual Folders
            CShellItem itm;
            // Debug.WriteLine("GContent " & Me.Path)
            // Dim StTime As DateTime = Now()
            // Dim content As ArrayList = GetContentPtrs(flags)       '11/09/2013 - should have been commented out originally
            // Debug.WriteLine("GPtrRel " & Now().Subtract(StTime).TotalMilliseconds.ToString & " ms")
            // StTime = Now()
            // For Each ptr In content
            foreach (IntPtr ptr in GetContentPtrs(flags))
            {
                if (ptr == IntPtr.Zero)                                               // 11/09/2013 - Investigate other
                {
                    Debug.WriteLine("Content=IntPtr.Zero while filling " + FullPath);     // 11/09/2013 - Investigate other
                    Marshal.FreeCoTaskMem(ptr);                                          // 11/09/2013 - Investigate other
                    continue;                                                        // 11/09/2013 - Investigate other
                }
                else
                {
                    try                                         // ASUS Fix 'mod 06/27/09 First fix added
                    {
                        itm = new CShellItem(ptr, this);
                        rVal.Add(itm);
                    }
                    // Catch ex As InvalidCastException             'ASUS Fix - superceeded 11/13/2013
                    // Debug.WriteLine("GetContents - InvCast") 'ASUS Fix
                    // Debug.WriteLine("GetContents - Exception: " & ex.Message)   '11/09/2013 - Investigate other
                    // Debug.WriteLine("Processing " & Me.Path)                    '11/09/2013 - Investigate other
                    // DumpPidl(ptr)                                               '11/09/2013 - Investigate other
                    catch (Exception ex)                                           // 11/09/2013 - Investigate other
                                                                                   // ASUS Fix
                    {
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(ptr);
                    }
                }           // ASUS Fix
                            // 11/09/2013 - Investigate other
            }
            // Debug.WriteLine("BuildItems " & Now().Subtract(StTime).TotalMilliseconds.ToString & " ms")
            return rVal;
        }

        /// <summary>
        /// Given a relative PIDL (relative to Me.Folder) determine if item is a Folder.
        /// </summary>
        /// <param name="ptr">A relative PIDL, relative to Me.Folder</param>
        /// <returns>True if item is a Folder, False is item is NOT a Folder.</returns>
        /// <remarks>Container files (such as .zip or .cab) are marked as a "Folder" in WinXP and above, so
        /// some further testing must be done on XP and above systems. We define such items as non-Folders.</remarks>
        private bool IsFolderRel(IntPtr ptr)
        {
            bool IsFolderRelRet = default;
            IsFolderRelRet = false;         // assume it is not
            var attrFlag = SFGAO.FOLDER | SFGAO.STREAM;
            // Note: for GetAttributesOf, we must provide an array, in all cases with 1 element
            var aPidl = new IntPtr[1];
            aPidl[0] = ptr;
            Folder.GetAttributesOf(1, aPidl, ref attrFlag);
            if (!WinSDK.XPorAbove)
            {
                if ((attrFlag & SFGAO.FOLDER) != 0) // is folder
                {
                    IsFolderRelRet = true;
                }
            }
            else if (((attrFlag & SFGAO.FOLDER) != 0) && !((attrFlag & SFGAO.STREAM) != 0))         // XP or above
            {
                IsFolderRelRet = true;
            }

            return IsFolderRelRet;
        }

        /// <summary>
        /// Returns the requested Items of this Folder as an ArrayList of relative PIDLs 
        /// (caller must free the pidls after use).
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        /// <returns>On error, returns an empty (count=0) ArrayList. Otherwise, returns the relative PIDLs of
        /// the requested (via flags param) items in this Folder.</returns>
        private ArrayList GetContentPtrs(SHCONTF flags)
        {
            var rVal = new ArrayList();
            int HR;
            IEnumIDList IEnum = null;
            // UPDATE: Vista and above strictly respect the SHCONTF flags. The "flags" param is now used only to determine what user wants
            HR = Folder.EnumObjects(0, SHCONTF.INCLUDEHIDDEN | SHCONTF.FOLDERS | SHCONTF.NONFOLDERS, ref IEnum);     // new code (12/11/09)
                                                                                                                     // HR = Me.Folder.EnumObjects(0, flags, IEnum)    'Old Code
            if (HR == NOERROR)
            {
                var ptr = IntPtr.Zero;
                int itemCnt;
                HR = IEnum.Next(1, out ptr, out itemCnt);
                while (HR == NOERROR && itemCnt > 0 && !ptr.Equals(IntPtr.Zero))
                {
                    bool includeFolders = (flags & SHCONTF.FOLDERS) != 0;
                    bool includeNonFolders = (flags & SHCONTF.NONFOLDERS) != 0;

                    if (!includeFolders && !includeNonFolders)
                    {
                        // Nothing is allowed, so we can reject without checking item type.
                        Marshal.FreeCoTaskMem(ptr);
                    }
                    else if (includeFolders && includeNonFolders)
                    {
                        // Everything is allowed, so no need to check item type.
                        rVal.Add(ptr);
                    }
                    else
                    {
                        // Only one category is allowed; now we need to know what this item is.
                        bool itemIsFolder = IsFolderRel(ptr); //don't do this earlier so we can sometimes avoid the expense

                        if ((itemIsFolder && !includeFolders) || (!itemIsFolder && !includeNonFolders))
                            Marshal.FreeCoTaskMem(ptr);
                        else
                            rVal.Add(ptr);
                    }

                    ptr = IntPtr.Zero;
                    itemCnt = 0;
                    HR = IEnum.Next(1, out ptr, out itemCnt);
                }
                if (HR != 1)
                    goto HRError; // 1 means no more
            }
            else
            {
                goto HRError;
            }
            // Normal Exit
        NORMAL:
            if (!(IEnum == null))
                Marshal.ReleaseComObject(IEnum);
            return rVal;

            // Error Exit for all Com errors
        HRError:
            // not ready disks will return the following error
            // If HR = &HFFFFFFFF800704C7 Then
            // GoTo NORMAL
            // ElseIf HR = &HFFFFFFFF80070015 Then
            // GoTo NORMAL
            // 'unavailable net resources will return these
            // ElseIf HR = &HFFFFFFFF80040E96 Or HR = &HFFFFFFFF80040E19 Then
            // GoTo NORMAL
            // ElseIf HR = &HFFFFFFFF80004001 Then 'Certain "Not Implemented" features will return this
            // GoTo NORMAL
            // Sharepoint folders return this at the end of the enum
            if ((HR == (unchecked((long)0xFFFFFFFF80004005))))
            {
                goto NORMAL;
                // ElseIf HR = &HFFFFFFFF800704C6 Then
                // GoTo NORMAL
            }
#if DEBUG
            // If Not IsNothing(IEnum) Then Marshal.ReleaseComObject(IEnum)
            // Marshal.ThrowExceptionForHR(HR)
#endif
            rVal = new ArrayList(); // sometimes it is a non-fatal error,ignored
            goto NORMAL;
        }


        #endregion

        #endregion


        /// <summary>
        /// CShItemUpdateType is an Enum of the various types of change that will be reported in a ShellItemUpdateEventArgs.
        /// </summary>
        /// <remarks>This Enum is also used by the CShItemUpdater Class to report change types to CShellItem.Update which passes it 
        ///          on to the Application.</remarks>
        public enum CShItemUpdateType
        {
            Created,
            IconChange,
            Updated,
            UpdateDir,
            Renamed,
            Deleted,
            MediaChange
        }


    }

}