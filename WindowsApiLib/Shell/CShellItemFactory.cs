using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;

namespace WindowsApiLib.Shell
{
    public class CShellItemFactory
    {
        private static readonly object _lock = new();

        /// <summary>
        /// Keep list of Drives and their DriveType for IsRemote testing
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> DriveDict = new ();


        /// <summary>
        /// 
        /// </summary>
        internal static CShellItem? DesktopCSI { get; set; }

        private static readonly ConcurrentDictionary<string, string> s_typeNameCache = new(StringComparer.OrdinalIgnoreCase);
#if DEBUG
        private static Queue<CShellItem> debugCsis = new(10);
#endif
        // Optional cache for no-extension files
        private const string NoExtensionCacheKey = "<​NOEXT>";


        public static CShellItemFactory Instance { get; private set; }
        
        /// <summary>
        /// Contains a String with the Local representation of "My Computer"
        /// </summary>
        public static string? StrMyComputer { get; private set; }
        /// <summary>
        /// Contains a String with the Local representation of "System Folder".
        /// </summary>
        public static string? StrSystemFolder { get; private set; }

        /// <summary>
        /// To get My Documents sorted first, we need to know the Locale specific name of that folder.
        /// </summary>
        public static string? StrMyDocuments { get; private set; }

        public static string? StrRecycleBin { get; private set; }

        /// <summary>
        /// Contains a String with the Full Path of the Desktop Directory
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        public static string? DesktopDirectoryPath { get; private set; }


        public static IntPtr EmptyPidl { get; private set; }
        public static IntPtr DesktopPidl { get; private set; }

        public static string SystemName { get; private set; }
        public static CShellItem RecycleBin { get; private set; }
        public static CShellItem DeskTopDirectory { get; private set; }

        public static CShellItem MyDocuments { get; private set; }

        public static CShellItemHierachyManager? HierachyManager { get; internal set; }

        private CShellItemFactory(CShellItemHierachyManager? hierachyManager = null) 
        {
            HierachyManager = hierachyManager;
            EmptyPidl = CreateEmptyPidl();
            DesktopPidl = GetShellNamespacePidl(ShellNamespaceGuids.DesktopFileSystem);

            // Get the SystemName for Remote item testing
            SystemName = Environment.MachineName;

            CShellItemFactory.DesktopCSI = GetDesktopRoot();
            DeskTopDirectory = Create(CSIDL.DESKTOPDIRECTORY);

            RecycleBin = Create(CSIDL.BITBUCKET);
            StrRecycleBin = RecycleBin.DisplayName;

            MyDocuments = Create(CSIDL.MYDOCUMENTS);

            StrMyDocuments = MyDocuments.m_DisplayName;
            StrSystemFolder = DesktopCSI.m_TypeName;
            StrMyComputer = DesktopCSI.m_DisplayName;
        }

        // Call once at startup
        public static void Initialize(CShellItemHierachyManager? hierachyManager = null)
        {
            lock (_lock)
            {
                if (Instance is not null) return;

                Instance = new CShellItemFactory(hierachyManager);
            }
        }

        /// <summary>
        /// Gets the existing Desktop item if it exists otherwise creates a new one.
        /// </summary>
        /// <returns></returns>
        public static CShellItem GetDesktopRoot()
        {
            if (DesktopCSI != null) return DesktopCSI;

            var csi = new CShellItem();

            return PopulateDesktopCShellItem(csi);
        }

        /// <summary>
        /// Clears the cached Desktop root so that the next call to <see cref="GetDesktopRoot"/>
        /// will create a fresh instance. Used by <see cref="CShellItemHierachyManager.Clear"/>
        /// to reset the hierarchy to a clean state.
        /// </summary>
        internal static void ResetDesktopCache()
        {
            DesktopCSI = null;
        }

        /// <summary>Given a Full Path in a String,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="path">The Full Path of the desired CShellItem</param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem Create(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero) return null;

            return FindAndAllowExpansion(pidl);
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
        public static CShellItem? Create(CSIDL ID)
        {
            CShellItem? csi = null;
            if (ID == CSIDL.DESKTOP)
            {
                csi = new CShellItem();

                csi = PopulateDesktopCShellItem(csi);

                return csi;
            }

            int HR;
            IntPtr tmpPidl = IntPtr.Zero;
            
            HR = SHGetSpecialFolderLocation(0, (int)ID, ref tmpPidl);

            if (HR == NOERROR)
            {
                csi = Create(tmpPidl, DesktopCSI);
            }

            if (csi is null && tmpPidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(tmpPidl);
            }

            return csi;
        }

        /// <summary>Given a Byte() containing the PIDL of a Folder and a Byte() containing the relative PIDL of the desired item,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="pidlFolder"></param>
        /// <param name="pidlItem"></param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem Create(byte[] pidlFolder, byte[] pidlItem)
        {
            CShellItem csi = null;    // assume failure

            if (pidlFolder == null && pidlItem == null)
                return csi; // can do no more with invalid pidls

            IntPtr fullPidl = IntPtr.Zero;

            GCHandle handle = GCHandle.Alloc(pidlFolder, GCHandleType.Pinned);
            GCHandle handle2 = GCHandle.Alloc(pidlItem, GCHandleType.Pinned);
            try
            {
                fullPidl = CPidl.Concatenate(handle.AddrOfPinnedObject(), handle2.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
                handle2.Free();
            }

            csi = FindAndAllowExpansion(fullPidl);

            if (csi is null && fullPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(fullPidl);
            if (csi is not null && csi.PIDL == IntPtr.Zero)
            {
                csi.Dispose(); // last minute failsafe
                csi = null;
            }

            return csi;
        }

        /// <summary>
        /// Creates a cshellitem from a pidl.
        /// </summary>
        /// <param name="pidl">can be a relative or full pidl</param>
        /// <param name="parent">required for relative pidls</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static CShellItem Create(IntPtr pidl, CShellItem? parent = null)
        {
            var csi = new CShellItem();

            IntPtr fullPidl;
            var segments = CPidl.SegmentCount(pidl);
            if (segments == 0)
            {
                throw new ArgumentException("CShellItemFactory.Create: Invalid zero segment pidl provided.");
            }
            else if (segments == 1) //relative pidl or desktop root
            {   
                if (CPidl.IsShellNamespaceRoot(pidl))
                {
                    PopulateCsi(csi, pidl);
                    csi.Parent = null;
                    return csi;
                }
                else
                {
                    if (parent is null) throw new ArgumentException("parent can't be null when pidl is not absolute.");
                    

                    fullPidl = CPidl.Concatenate(parent.PIDL, pidl);
                }
            }
            else
            {
                fullPidl = pidl;
            }

            PopulateCsi(csi, fullPidl, parent);

            return csi;

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
        public static CShellItem? FindAndAllowExpansion(IntPtr pidl)
        {
            CShellItem? csi = default;
            CShellItem? Parent = null;

            if (HierachyManager is null)
            {
                csi = new CShellItem();
                PopulateCsi(csi, pidl);
            }
            else
            {
                csi = HierachyManager.FindAndAllowExpansion(pidl, out Parent);
                if (csi == null)
                {
                    if (!(Parent == null))
                        csi = Create(pidl, Parent);
                    else
                        csi = Create(pidl);
                }
            }

            return csi;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="csi">the CShellItem to populate</param>
        /// <param name="pidl">A full pidl</param>
        internal static void PopulateCsi(CShellItem csi, IntPtr pidl, CShellItem? parentCsi = null)
        {
            // Set unfetched value for IconIndex....
            csi.m_IconIndexNormal = -1;
            csi.m_IconIndexOpen = -1;

            if (CPidl.IsShellNamespaceRoot(pidl))
            {
                _ = PopulateDesktopCShellItem(csi);
                return;
            }
            else
            {
#if DEBUG
                //var length = CPidl.SegmentCount(pidl);
#endif
                csi.m_Pidl = pidl;
               
                csi.Parent = parentCsi;

                // Get some attributes
                PopulateBasicFields(csi);

                if (csi.m_IsFolder)
                {
                    if (csi.m_Parent != null)
                    {
                        //var splitted = CPidl.Split(pidl);
                        //csi.m_IShellFolder = ShellHelper.GetIShellFolder(csi.m_Parent, splitted.ChildPidl); // prevent possible cross sta thread com rpc problems when reusing ishellfolder
                        //csi.m_IShellFolder = ShellHelper.GetIShellFolder(pidl);
                    }
                    else {
                        //csi.m_IShellFolder = ShellHelper.GetIShellFolder(pidl); // get IShellFolder from absolute PIDL
                    }
                }

#if DEBUG
                if (csi.FullPath == @"C:\Downloads")
                {
                    debugCsis.Enqueue(csi);
                }
#endif
            }
        }

        //public static CShellItem FindOrCreateCsiFromPath(string path)
        //{
        //    IntPtr pidl = ILCreateFromPathW(path);

        //    var target = HierachyManager.FindInShellHierarchy(pidl, out var parent);

        //    if (target != null) return target;

        //    var csi = new CShellItem();
        //    PopulateCsi(csi, pidl);

        //    return csi;
        //}

        public static CShellItem PopulateCsiFromPath(CShellItem csi, string path)
        {
            IntPtr pidl = ILCreateFromPathW(path);

            PopulateCsi(csi, pidl);

            return csi;
        }

        /// <summary>
        /// Returns the requested Items of this Folder as a List of relative or full PIDLs 
        /// (caller must free the pidls after use).
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        /// <returns>On error, returns an empty (count=0) List. Otherwise, returns the relative PIDLs of
        /// the requested (via flags param) items in this Folder.</returns>
        public static List<IntPtr> GetChildPidls(CShellItem csi, SHCONTF flags, bool fullPidls = false)
        {
            const uint BATCH_SIZE = 64; //this always only fetches 1 pidl at a time
            bool includeFolders = (flags & SHCONTF.FOLDERS) != 0;
            bool includeNonFolders = (flags & SHCONTF.NONFOLDERS) != 0;

            List<IntPtr> results = new List<IntPtr>(0);
            if (!includeFolders && !includeNonFolders) //nonsense flags
            {
                return results;
            }

            int hr;
            IEnumIDList enumerator = null;

            results = new List<IntPtr>();

            //IShellFolder iShellFolder = csi.IShlFolder;
            IShellFolder parentIShellFolder = ShellHelper.GetIShellFolder(csi.PIDL);

            if (parentIShellFolder is null)
            {
                //i think there is a bug wherein we are storing a pidl that is actually OS owned and sometimes it can be released by the OS before the current point in code.
                Debugger.Break(); 
            }

            try
            {
                hr = parentIShellFolder.EnumObjects(0, flags, out enumerator);
                if (hr != S_OK)
                    return results;

                IntPtr[] batch = new IntPtr[BATCH_SIZE];
                uint fetched = 0;

                while (true)
                {
                    // IMPORTANT: This assumes your interop signature supports array/batch Next (see note below).
                    hr = enumerator.Next(BATCH_SIZE, batch, out fetched);

                    //Console.WriteLine($"\tfetched {fetched.ToString()} pidls.");

                    // Any COM error besides S_FALSE(end) should go to error path.
                    if (hr != S_OK && hr != S_FALSE) // UPDATE: Vista and above strictly respect the SHCONTF flags. The "flags" param is now used only to determine what user wants
                    {
                        // Sharepoint folders return this at the end of the enum
                        if (hr == unchecked((int)0x80004005))
                            break;
                        else if (hr == -2147417848) //RPC_E_DISCONNECTED
                            break;
                        else break;
                    }

                    // S_FALSE means end of enumeration (possibly with a short final batch already processed).
                    if (hr == S_FALSE)
                        break;

                    // Defensive guard against unusual providers returning S_OK with 0 items.
                    if (fetched == 0)
                        break;

                    // Handle partial batches (fetched may be < BATCH_SIZE).
                    for (uint i = 0; i < fetched; i++)
                    {
                        IntPtr pidlChild = batch[i];
                        batch[i] = IntPtr.Zero; // clear slot immediately

                        if (pidlChild == IntPtr.Zero)
                            continue;

                        // get a full pidl if desired
                        IntPtr finalPidl = pidlChild;
                        if (fullPidls) { 
                            Object shellItem;
                            hr = SHCreateItemWithParent(
                                IntPtr.Zero,
                                parentIShellFolder,
                                pidlChild,
                                ref IID_IShellItem,
                                out shellItem);

                            if (hr == 0 && shellItem != null)
                            {
                                // 5. Extract the full absolute PIDL from the IShellItem
                                hr = SHGetIDListFromObject(shellItem, out finalPidl);
                                if (hr == 0 && finalPidl != IntPtr.Zero)
                                    results.Add(finalPidl);
                            }

                            Marshal.FreeCoTaskMem(pidlChild);
                        }

                        if (includeFolders && includeNonFolders)
                        {
                            results.Add(finalPidl);
                        }
                        else // Only one category is allowed; now we need to know what this item is.
                        {
                            bool itemIsFolder = IsFolderRel(parentIShellFolder, pidlChild); // only when needed
                            if ((itemIsFolder && !includeFolders) || (!itemIsFolder && !includeNonFolders))
                                Marshal.FreeCoTaskMem(finalPidl);
                            else
                                results.Add(finalPidl);
                        }
                    }
                }
            }
            catch(Exception ex) 
            {
                Debug.WriteLine("ERROR: CShellItemFactory.GetChildPidls exception - " + ex.ToString());
            }
            finally
            {
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
                Marshal.ReleaseComObject(parentIShellFolder);
            }
            return results;
        }

        /// <summary>
        /// Returns the requested Items of the given Folder as a CShitemCollection
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        public static List<CShellItem>? GetContents(CShellItem csi, SHCONTF flags) //todo: move to shellcontroller
        {
            if (!csi.IsFolder) return null;


            Debug.WriteLine($"CShellItemFactory: Getting contents for folder '{csi.FullPath}'.");

            //var pidls = CShellItemFactory.GetChildPidls(csi, flags, true);
            var pidls = CShellItemFactory.GetChildPidls(csi, flags);
            var items = new List<CShellItem>(pidls.Count);

            Debug.WriteLine("\tCreating " + pidls.Count() + " cshellitems...");
            var parentIshellfolder = csi.GetIShellFolder();

            foreach (IntPtr pidl in pidls)
            {
                if (pidl == IntPtr.Zero)
                {
                    Debug.WriteLine("\tFetch pidl==0 while reading contents of " + csi.FullPath);
                    Marshal.FreeCoTaskMem(pidl);
                    continue;
                }
                else
                {
                    //var tmpCsi = CShellItemFactory.Create(pidl, csi);
                    var tmpCsi = CShellItemFactory.Create(pidl, csi);
                    items.Add(tmpCsi);
                    Marshal.FreeCoTaskMem(pidl);
                }
            }

            Debug.WriteLine("\tFinished creating cshellitems");

            return items;
        }

        public static IntPtr GetShellNamespacePidl(Guid shellLocationGuid)
        {
            int hr = SHGetKnownFolderIDList(shellLocationGuid, 0, IntPtr.Zero, out IntPtr pidl);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return pidl;
        }

        /// <summary>
        /// Creates an empty PIDL (just the 2-byte terminator: SHITEMID.cb == 0).
        /// Caller must free with FreeEmptyPidl.
        /// </summary>
        public static IntPtr CreateEmptyPidl()
        {
            // Empty PIDL is exactly 2 bytes: terminating USHORT 0
            IntPtr pidl = Marshal.AllocCoTaskMem(2);
            Marshal.WriteInt16(pidl, 0); // terminator
            return pidl;
        }

        public static string? GetFullPath(CShellItem csi)
        {
            var pidl = csi.PIDL;
            if (pidl == IntPtr.Zero) throw new ArgumentNullException(nameof(pidl));

            if (csi.m_IsFileSystem)
                return CPidl.GetFileSystemPath(pidl);
            else return CPidl.GetParsingPath(pidl);
        }

        public static bool Exists(IntPtr pidl)
        {
            IShellItem shellItem;
            int hr = SHCreateItemFromIDList(pidl, ref ShellAPI.IID_IShellItem, out shellItem);

            if (hr >= 0)
            {
                return true;
            }
            else return false;
        }


        /// <summary>
        /// Get the base attributes of the folder/file that this CShellItem represents, and set the 
        /// DisplayName and TypeName fields.  The exact fields populated can be changed as desired.
        /// </summary>
        /// <param name="csiOutput"></param>
        public static void PopulateBasicFields(CShellItem csiOutput)
        {
            PopulateBasicAttributes(csiOutput);
            SetDisplayNameAndType(csiOutput);
            ComputeSortFlag(csiOutput);
        }


        #region Private methods

        /// <summary>
        /// Given a relative PIDL (relative to Me.Folder) determine if item is a Folder.
        /// </summary>
        /// <param name="ptr">A relative PIDL, relative to Me.Folder</param>
        /// <returns>True if item is a Folder, False is item is NOT a Folder.</returns>
        /// <remarks>Container files (such as .zip or .cab) are marked as a "Folder" in WinXP and above, so
        /// some further testing must be done on XP and above systems. We define such items as non-Folders.</remarks>
        private static bool IsFolderRel(IShellFolder iShellFolder, IntPtr ptr)
        {
            bool isFolderRelRet = false;

            var attrFlag = SFGAO.FOLDER | SFGAO.STREAM;
            // Note: for GetAttributesOf, we must provide an array
            var aPidl = new IntPtr[1];
            aPidl[0] = ptr;

            //IntPtr pUnk = Marshal.GetIUnknownForObject(iShellFolder);

            iShellFolder.GetAttributesOf(1, aPidl, ref attrFlag);
            if (((attrFlag & SFGAO.FOLDER) != 0) && !((attrFlag & SFGAO.STREAM) != 0))
            {
                isFolderRelRet = true;
            }

            return isFolderRelRet;
        }

        private static CShellItem PopulateDesktopCShellItem(CShellItem csi)
        {
            int HR;
            //IntPtr tmpPidl = IntPtr.Zero;
            //HR = SHGetSpecialFolderLocation(0, (int)CSIDL.DESKTOP, ref tmpPidl);
            var shfi = new SHFILEINFO();
            var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            int dwAttr = 0;
            SHGetFileInfo(DesktopPidl, dwAttr, ref shfi, SHFILEINFO_size, dwflag);

            IShellFolder iShellFolder = null;
            HR = SHGetDesktopFolder(ref iShellFolder);
            csi.m_Pidl = DesktopPidl;
            //csi.m_IShellFolder = iShellFolder;
            csi.m_DisplayName = shfi.szDisplayName;
            csi.m_FullPath = "::{" + DesktopGUID.ToString() + "}";
            csi.m_IsFolder = true;
            csi.m_HasSubFolders = true;
            csi.m_IsBrowsable = true;
            csi.m_TypeName = shfi.szTypeName;   // not returned correctly by SHGetFileInfo
            csi.m_IconIndexNormal = shfi.iIcon;
            csi.m_IconIndexOpen = shfi.iIcon;
            csi.m_HasDispType = true;
            csi.IsDropTarget = true;
            csi.m_IsReadOnly = false;
            csi.m_IsReadOnlySetup = true;
            PopulateBasicAttributes(csi);
            SetDisplayNameAndType(csi);

            return csi;
        }

        /// <summary>Get the base attributes of the folder/file that this CShellItem represents</summary>
        /// <param name="folder">Parent Folder of this Item</param>
        /// <param name="pidl">Relative Pidl of this Item.</param>
        private static void PopulateBasicAttributes(CShellItem csiOutput)
        {
            SFGAO attrFlag;
            attrFlag = SFGAO.BROWSABLE | SFGAO.FILESYSTEM | SFGAO.FOLDER | SFGAO.LINK | SFGAO.SHARE
             | SFGAO.HIDDEN | SFGAO.REMOVABLE | SFGAO.CANCOPY | SFGAO.CANDELETE | SFGAO.CANLINK
             | SFGAO.CANMOVE | SFGAO.DROPTARGET | SFGAO.CANRENAME | SFGAO.STREAM;
            // SFGAO.RDONLY   'made into an on-demand attribute
            // SFGAO.HASSUBFOLDER   'made into an on-demand attribute

            var iid = typeof(IShellItem).GUID;
            var pidl = csiOutput.m_Pidl;
            SHCreateItemFromIDList(pidl, ref iid, out IntPtr item);
            IShellItem shellItem = (IShellItem)Marshal.GetObjectForIUnknown(item);
            Marshal.Release(item);
            shellItem.GetAttributes((uint)attrFlag, out uint attrs);
            Marshal.ReleaseComObject(shellItem);

            attrFlag = (SFGAO)attrs;
            csiOutput.m_SFGAO_Attributes = attrFlag;
            csiOutput.m_IsBrowsable = (attrFlag & SFGAO.BROWSABLE) != 0;
            csiOutput.m_IsFolder = (attrFlag & SFGAO.FOLDER) != 0;
            csiOutput.m_IsLink = (attrFlag & SFGAO.LINK) != 0;
            csiOutput.m_IsShared = (attrFlag & SFGAO.SHARE) != 0;
            csiOutput.m_IsHidden = (attrFlag & SFGAO.HIDDEN) != 0;
            csiOutput.m_IsRemovable = (attrFlag & SFGAO.REMOVABLE) != 0;
            csiOutput.m_CanCopy = (attrFlag & SFGAO.CANCOPY) != 0;
            csiOutput.m_CanDelete = (attrFlag & SFGAO.CANDELETE) != 0;
            csiOutput.m_CanLink = (attrFlag & SFGAO.CANLINK) != 0;
            csiOutput.m_CanMove = (attrFlag & SFGAO.CANMOVE) != 0;
            csiOutput.IsDropTarget = (attrFlag & SFGAO.DROPTARGET) != 0;
            csiOutput.m_CanRename = (attrFlag & SFGAO.CANRENAME) != 0;
            if (pidl == DesktopPidl)
            {
                csiOutput.m_IsFileSystem = false;
                csiOutput.m_FullPath = "::{" + DesktopGUID.ToString() + "}";
            }
            else
            {
                csiOutput.m_IsFileSystem = (attrFlag & SFGAO.FILESYSTEM) != 0;
                csiOutput.m_FullPath = CShellItemFactory.GetFullPath(csiOutput);
            }
            // m_IsReadOnly = (attrFlag & SFGAO.RDONLY) != 0;      'made into an on-demand attribute
            // m_HasSubFolders = (attrFlag & SFGAO.HASSUBFOLDER) != 0;  'made into an on-demand attribute

            // check for zip file = folder on xp, leave it a file
            if (csiOutput.m_IsFolder && csiOutput.m_IsFileSystem)
            {
                // If (m_Attributes = (m_Attributes And SFGAO.STREAM)) Then
                if ((attrFlag & SFGAO.STREAM) != 0)   // in this case, it is not a Folder, but a .zip or .cab or etc
                    csiOutput.m_IsFolder = false;
            }

            if (csiOutput.m_IsFolder && csiOutput.m_FullPath.Length == 3 && csiOutput.m_FullPath.Substring(1).Equals(@":\"))
            {
                csiOutput.m_IsDisk = true;
                try
                {
                    var di = new DriveInfo(csiOutput.FullPath.Substring(0, 2));
                    csiOutput.m_Length = di.TotalSize;
                    if (di.DriveType == DriveType.Network)
                    {
                        csiOutput.m_IsNetWorkDrive = true;
                        csiOutput.m_IsRemote = true;
                    }
                }
                catch (Exception ex)
                {
                    // Disconnected Network Drives etc. will generate 
                    // an error here, just assume that it is a network
                    // drive
                    csiOutput.m_IsNetWorkDrive = true;
                    csiOutput.m_IsRemote = true;
                }
                finally
                {
                    csiOutput.m_XtrInfo = true;
                    if (!DriveDict.ContainsKey(csiOutput.m_FullPath))
                    {
                        DriveDict.TryAdd(csiOutput.m_FullPath, csiOutput.m_IsRemote);
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
            if (!(csiOutput.m_IsDisk || csiOutput.m_FullPath.StartsWith("::")))
            {
                if (csiOutput.m_FullPath.StartsWith(@"\\"))
                {
                    string[] tmp = csiOutput.m_FullPath.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tmp.Length > 0 && tmp[0].Equals(CShellItemFactory.SystemName, StringComparison.InvariantCultureIgnoreCase))
                        csiOutput.m_IsRemote = false;
                    else
                        csiOutput.m_IsRemote = true;
                }
                else if (csiOutput.m_FullPath.Length > 2 && csiOutput.m_FullPath.Substring(1, 2).Equals(@":\"))
                {
                    string itemroot = csiOutput.m_FullPath.Substring(0, 3);
                    if (DriveDict.ContainsKey(itemroot) && DriveDict[itemroot])
                        csiOutput.m_IsRemote = true;
                }
            }
        }

        /// <summary>
        /// Sets DisplayName, TypeName, and SortFlag when actually needed.
        /// Optimized to avoid expensive shell calls when filesystem APIs are enough.
        /// </summary>
        internal static void SetDisplayNameAndType(CShellItem csi)
        {
            if (csi.m_HasDispType)
                return;

            csi.m_FullPath = GetFullPath(csi);

            // Fast path for filesystem file items (most common case)
            if (csi.m_IsFileSystem && !csi.m_IsFolder)
            {
                csi.m_DisplayName = GetFastFileDisplayName(csi.FullPath);

                // Cached type-name by extension
                csi.m_TypeName = GetCachedTypeNameForFile(csi.FullPath);

                // Rare fallback if we couldn't resolve a type name
                if (string.IsNullOrWhiteSpace(csi.m_TypeName))
                {
                    TryGetTypeNameViaShell(csi, out _, out var shellTypeName);
                    if (!string.IsNullOrWhiteSpace(shellTypeName))
                        csi.m_TypeName = shellTypeName;
                }
            }
            else
            {
                // Folder and non-filesystem items: keep shell semantics
                if (TryGetTypeNameViaShell(csi, out var shellDisplayName, out var shellTypeName))
                {
                    csi.m_DisplayName = shellDisplayName;
                    csi.m_TypeName = shellTypeName;
                }

                // Fast fallback display name
                if (string.IsNullOrEmpty(csi.m_DisplayName))
                    csi.m_DisplayName = GetFastFileDisplayName(csi.FullPath);
            }

            // Final hard fallbacks
            if (string.IsNullOrWhiteSpace(csi.m_DisplayName))
                csi.m_DisplayName = Path.GetFileName(csi.FullPath);

            if (string.IsNullOrWhiteSpace(csi.m_TypeName))
                csi.m_TypeName = csi.m_IsFolder ? "Folder" : "File";

            csi.m_HasDispType = true;
        }

        private static string GetFastFileDisplayName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return string.Empty;

            // Trim trailing separators to handle folder-like paths safely
            var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);

            // Root paths (e.g. "C:\") yield empty name; fallback to path itself
            return string.IsNullOrEmpty(name) ? fullPath : name;
        }

        private static string GetCachedTypeNameForFile(string fullPath)
        {
            string ext = Path.GetExtension(fullPath);
            string key = string.IsNullOrEmpty(ext) ? NoExtensionCacheKey : ext;

            return s_typeNameCache.GetOrAdd(key, _ =>
            {
                // For files with no extension
                if (key == NoExtensionCacheKey)
                {
                    // "File" is a fast fallback; you can choose to do a shell lookup here once if desired.
                    return "File";
                }

                // Ask Windows association subsystem for friendly type name.
                string assocType = TryGetAssocTypeName(ext);
                if (!string.IsNullOrWhiteSpace(assocType))
                    return assocType;

                // Fallback
                return "File";
            });
        }

        private static string TryGetAssocTypeName(string extension)
        {
            // AssocQueryString expects ".ext"
            if (string.IsNullOrWhiteSpace(extension))
                return null;

            uint pcchOut = 0;
            // First call to get required buffer size
            AssocQueryString(
                ASSOCF.NONE,
                ASSOCSTR.FRIENDLYDOCNAME,
                extension,
                null,
                null,
                ref pcchOut);

            if (pcchOut == 0)
                return null;

            var sb = new StringBuilder((int)pcchOut);
            int hr = AssocQueryString(
                ASSOCF.NONE,
                ASSOCSTR.FRIENDLYDOCNAME,
                extension,
                null,
                sb,
                ref pcchOut);

            return hr == 0 ? sb.ToString() : null;
        }

        private static bool TryGetTypeNameViaShell(CShellItem csi, out string? displayName, out string? typeName)
        {
            displayName = null;
            typeName = null;

            var shfi = new SHFILEINFO();
            var flags = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            int attrs = 0;

            // USEFILEATTRIBUTES helps avoid touching disk for filesystem files
            if (csi.m_IsFileSystem && !csi.m_IsFolder)
            {
                flags |= SHGFI.USEFILEATTRIBUTES;
                attrs = FILE_ATTRIBUTE_NORMAL;
            }

            IntPtr result = SHGetFileInfo(csi.m_Pidl, attrs, ref shfi, SHFILEINFO_size, flags);
            if (result == IntPtr.Zero)
                return false;

            displayName = shfi.szDisplayName;
            typeName = shfi.szTypeName;
            return true;
        }

        /// <summary>Computes the Sort key of this CShellItem, based on its attributes</summary>
        private static int ComputeSortFlag(CShellItem csi)
        {
            int rVal = 0;
            if (csi.m_IsDisk)
                rVal = 0x100000;

            if (csi.m_TypeName.Equals(CShellItemFactory.StrSystemFolder))
            {
                if (!csi.m_IsBrowsable)
                {
                    rVal = rVal | 0x10000;
                    if (CShellItemFactory.StrMyDocuments.Equals(csi.m_DisplayName))
                    {
                        rVal = rVal | 0x1;
                    }
                }
                else
                {
                    rVal = rVal | 0x1000;
                }
            }
            if (csi.m_IsFolder)
                rVal = rVal | 0x100;
            return rVal;
        }

#endregion Private methods
    }
}
