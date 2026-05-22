using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;

namespace WindowsApiLib.Shell
{
    public class CShellItemFactory
    {
        private static readonly object _lock = new();

        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        private static IShellFolder DesktopShellFolder { get; set; }

        /// <summary>
        /// Keep list of Drives and their DriveType for IsRemote testing
        /// </summary>
        private static readonly Dictionary<string, bool> DriveDict = new Dictionary<string, bool>();


        /// <summary>
        /// 
        /// </summary>
        private static CShellItem? DesktopCSI { get; set; }

        private static readonly ConcurrentDictionary<string, string> s_typeNameCache = new(StringComparer.OrdinalIgnoreCase);
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

        // To get My Documents sorted first, we need to know the Locale 
        // specific name of that folder.
        public static string? StrMyDocuments { get; private set; }

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

            (CShellItemFactory.DesktopShellFolder, CShellItemFactory.DesktopCSI) = GetDesktopRoot();
            DeskTopDirectory = CreateCShItem(CSIDL.DESKTOPDIRECTORY);

            RecycleBin = CreateCShItem(CSIDL.BITBUCKET);

            MyDocuments = CreateCShItem(CSIDL.MYDOCUMENTS);

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

        public static (IShellFolder, CShellItem) GetDesktopRoot()
        {
            if (DesktopCSI != null) return (DesktopShellFolder, DesktopCSI);

            var csi = new CShellItem();

            return PopulateDesktopCShellItem(csi);
        }

        private static (IShellFolder, CShellItem) PopulateDesktopCShellItem(CShellItem csi)
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
            csi.m_IShellFolder = iShellFolder;
            csi.m_DisplayName = shfi.szDisplayName;
            csi.m_Path = "::{" + DesktopGUID.ToString() + "}";
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

            return (iShellFolder, csi);
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
        public static CShellItem CreateCShItem(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero) return null;

            return GetOrCreateCShItem(pidl);
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
        public static CShellItem? CreateCShItem(CSIDL ID)
        {
            CShellItem? csi = null;
            if (ID == CSIDL.DESKTOP) return DesktopCSI;

            /* MYDOCUMENTS - the saga continues
             * In Vista and above, My Documents does not live immediately under the Desktop
             * (is not a member of DesktopBase.Directories)
             * Therefore, without special handling, this rtn will return Nothing as the 
             * CShellItem when CSIDL.MYDOCUMENTS is requested.
             * MS Documentation states that in Shell32.dll version 6.0 and above CSIDL_MYDOCUMENTS is 
             * Equivalent to CSIDL_PERSONAL. (6.0 = XP, 6.01 = Vista, 6.1 = Win7)
             * In XP, the PIDLs of PERSONAL and MYDOCUMENTS are Identical. In Vista and Win7, they are not.
             * In all OSes, the PIDL for MYDOCUMENTS has 1 item. In Vista and Win7, the PIDL for PERSONAL is a 
             * two item PIDL, which correctly reflects the location of the corresponding Folder in the directory tree.
             * Because of this, in Vista and above, I must use PERSONAL as the lookup CSIDL to obtain MYDOCUMENTS.
             */
            int HR;
            IntPtr tmpPidl = IntPtr.Zero;  // original code - retain
                                           
            if (ID == CSIDL.MYDOCUMENTS)
                ID = CSIDL.PERSONAL; // added 11/28/2010
            if (ID == CSIDL.MYDOCUMENTS)  // original code - retain
            {
                var pchEaten = default(int);
                int argpdwAttributes = default;
                HR = DesktopCSI.Folder.ParseDisplayName(default, default, $"::{ShellNamespaceGuids.Documents.ToString()}", ref pchEaten, ref tmpPidl, ref argpdwAttributes);
            }
            else
            {
                HR = SHGetSpecialFolderLocation(0, (int)ID, ref tmpPidl);
            }

            if (HR == NOERROR)
            {
                csi = CreateCShItem(tmpPidl, DesktopCSI);
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
        public static CShellItem CreateCShItem(byte[] pidlFolder, byte[] pidlItem)
        {
            CShellItem csi = null;    // assume failure

            if (pidlFolder == null && pidlItem == null)
                return csi; // can do no more with invalid pidls

            IntPtr fullPidl = IntPtr.Zero;

            GCHandle handle = GCHandle.Alloc(pidlFolder, GCHandleType.Pinned);
            GCHandle handle2 = GCHandle.Alloc(pidlFolder, GCHandleType.Pinned);
            try
            {
                fullPidl = CPidl.Concatenate(handle.AddrOfPinnedObject(), handle2.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
                handle2.Free();
            }

            csi = GetOrCreateCShItem(fullPidl);

            if (csi is null && fullPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(fullPidl);
            if (csi is not null && csi.PIDL == IntPtr.Zero)
            {
                csi.Dispose(); // last minute failsafe
                csi = null;
            }

            //byte[] fullPidl = CPidl.JoinPidlBytes(pidlFolder, pidlItem);

            //if (fullPidl == null)
            //    return csi; // can do no more with invalid pidls

            //var thisPidl = Marshal.AllocCoTaskMem(fullPidl.Length);
            //if (thisPidl.Equals(IntPtr.Zero))
            //    return null;

            //CPidl.PIDLClone(fullPidl);
            //Marshal.Copy(fullPidl, 0, thisPidl, fullPidl.Length);

            //csi = GetOrCreateCShItem(thisPidl);

            //if (!thisPidl.Equals(IntPtr.Zero))
            //    Marshal.FreeCoTaskMem(thisPidl);
            //if (csi.PIDL.Equals(IntPtr.Zero))
            //    csi = null; // last minute failsafe


            return csi;
        }


        public static CShellItem CreateCShItem(IntPtr pidl, CShellItem parent = null)
        {
            var csi = new CShellItem();

            IntPtr fullPidl;

            if (CPidl.SegmentCount(pidl) <= 1)
            { //relative pidl or desktop root
                if (CPidl.IsShellNamespaceRoot(pidl))
                {
                    PopulateCsi(csi, pidl);
                    csi.m_Parent = null;
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

            //if (parent != null)
            //{
            //    csi.m_Parent = parent;
            //    return csi;
            //}

            //var splitted = CPidl.Split(fullPidl);

            ////todo: change this to lazy load
            ////csi.m_Parent = CShellItemFactory.GetOrCreateCShItem(splitted.ParentPidl);
            //CShellItemFactory.BrowseTo(splitted.ParentPidl, out parent);
            //if (parent == null)
            //{
            //    parent = CreateCShItem(splitted.ParentPidl);
            //}

            //csi.m_Parent = parent;
            //return csi;

        }

        /// <summary>
        /// Note: batch calls of GetAttributesOf() doesn't work because it doesn't give open ended results - it only gives results based on the common denominator of flags.
        /// The only way to make this work would be to use file system querying instead of shell querying but that only works on some items.
        /// if HierachyManager is null, then batch get
        /// else
        ///   see if the parent exists in the hierachy
        ///   if it has children, don't do batch generation, iterate and return all children
        ///   else do batch generation
        /// </summary>
        /// <param name="pidls"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>

        //internal static List<CShellItem> CreateCShItems(List<nint> pidls, CShellItem parent)
        //{
        //    if (parent is null) throw new ArgumentException("");

        //    if (HierachyManager is null)
        //    {
        //        var results = BatchCreateCShItems(pidls, parent);
        //        return results;
        //    }
        //    else
        //    {
        //        var parentCsi = HierachyManager.FindInShellHierarchy(parent.PIDL, out var grandParent);
        //        if (parentCsi is not null && parentCsi.m_Files is not null)
        //        {
        //            var results = new List<CShellItem>(pidls.Count());

        //            foreach (var pidl in pidls)
        //            {
        //                var newCsi = GetOrCreateCShItem(pidl);
        //                results.Add(newCsi);
        //            }
        //            return results;
        //        }
        //        else
        //        {
        //            var results = BatchCreateCShItems(pidls, parentCsi);
        //            return results;
        //        }
        //    }
        //}

        /// <summary>
        /// batch calls of GetAttributesOf() doesn't work because 
        /// </summary>
        /// <param name="pidls"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        //private static List<CShellItem> BatchCreateCShItems(List<nint> pidls, CShellItem parent)
        //{
        //}

        /// <summary>Given an IntPtr representation of a PIDL,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="pidl">Absolute (Full) Pidl of item to be Found or Created</param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem GetOrCreateCShItem(IntPtr pidl)
        {
            CShellItem csi = default;
            CShellItem Parent = null;

            if (HierachyManager is null)
            {
                csi = new CShellItem();
                PopulateCsi(csi, pidl);
            }
            else
            {
                csi = HierachyManager.FindOrAdd(pidl, out Parent);
                if (csi == null)
                {
                    if (!(Parent == null))
                        csi = CreateCShItem(pidl, Parent);
                    else
                        csi = CreateCShItem(pidl);
                }
            }

            return csi;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="csi">the CShellItem to populate</param>
        /// <param name="pidl">A full pidl</param>
        internal static void PopulateCsi(CShellItem csi, IntPtr pidl, CShellItem parentCsi = null)
        {
            // Set unfetched value for IconIndex....
            csi.m_IconIndexNormal = -1;
            csi.m_IconIndexOpen = -1;

            if (CPidl.IsShellNamespaceRoot(pidl))
            {
                (_, _) = PopulateDesktopCShellItem(csi);
                return;
            }
            else
            {
                csi.m_Pidl = pidl;
               
                csi.m_Parent = parentCsi;

                // Get some attributes
                PopulateInitial(csi);

                if (csi.m_IsFolder)
                {
                    if (csi.m_Parent != null)
                    {
                        var splitted = CPidl.Split(pidl);
                        csi.m_IShellFolder = ShellHelper.GetIShellFolder(csi.m_Parent, splitted.ChildPidl); // get IShellFolder
                    }
                    else
                        csi.m_IShellFolder = ShellHelper.GetIShellFolder(pidl); // get IShellFolder from absolute PIDL
                }
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
        /// GetFolder returns the IShellFolder interface of the Folder designated by the input Parent and 
        /// relative PIDL.
        /// </summary>
        /// <param name="parent">The CShellItem of the Folder containing the folder for which the 
        /// IShellFolder interface is desired.</param>
        /// <param name="relPidl">The relative Pidl of the folder for which the interface is desired.</param>
        /// <returns>The desired interface or Nothing if error.</returns>
        /// <remarks></remarks>
        public static IShellFolder GetFolder(CShellItem parent, IntPtr relPidl)
        {
            IntPtr ptr = IntPtr.Zero;
            IShellFolder rVal = null;
            int HR = parent.Folder.BindToObject(relPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref ptr);
            if (HR >= S_OK && ptr != IntPtr.Zero)   // New code (12/12/09)
            {
                // The ASUS fix is slightly modified from its' original as per a suggestion from Calum 4/8/2010
                try                                                     // ASUS Fix
                {
                    rVal = (IShellFolder)Marshal.GetTypedObjectForIUnknown(ptr, typeof(IShellFolder));
                }
                catch (Exception ex)                                   // ASUS Fix - modified 11/13/2013 - was InvalidCastException
                {
#if DEBUG
                    Debug.WriteLine("GetFolder: " + ex.Message);         // ASUS Fix
                    throw;                                            // ASUS Fix
#endif
                }
                finally
                {
                    Marshal.Release(ptr); // Must do this in all cases
                }                                                 // ASUS Fix
            }
            else
            {
                if (ptr != IntPtr.Zero)
                    Marshal.Release(ptr); // Added Code (12/12/09)
#if DEBUG
                CPidl.Dump(relPidl);
                var ex = Marshal.GetExceptionForHR(HR);
                Debug.WriteLine($"{ex.Message}");

#endif
            }    // Removed 10/22/2011 - restored 11/13/2013
            return rVal;
        }


        /// <summary>
        /// Returns the requested Items of this Folder as a List of relative PIDLs 
        /// (caller must free the pidls after use).
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        /// <returns>On error, returns an empty (count=0) List. Otherwise, returns the relative PIDLs of
        /// the requested (via flags param) items in this Folder.</returns>
        public static List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags)
        {
            const int S_OK = 0;
            const int S_FALSE = 1;
            const uint BATCH_SIZE = 64;

            List<IntPtr> listPidls = new List<IntPtr>(0);
            int HR;
            IEnumIDList IEnum = null;

            listPidls = new List<IntPtr>();

            try
            {
                HR = csi.Folder.EnumObjects(0, flags, ref IEnum);
                if (HR != S_OK)
                    return listPidls;

                bool includeFolders = (flags & SHCONTF.FOLDERS) != 0;
                bool includeNonFolders = (flags & SHCONTF.NONFOLDERS) != 0;

                IntPtr[] batch = new IntPtr[BATCH_SIZE];
                uint fetched = 0;

                while (true)
                {
                    // IMPORTANT: This assumes your interop signature supports array/batch Next (see note below).
                    HR = IEnum.Next(BATCH_SIZE, batch, out fetched);

                    // Any COM error besides S_FALSE(end) should go to error path.
                    if (HR != S_OK && HR != S_FALSE) // UPDATE: Vista and above strictly respect the SHCONTF flags. The "flags" param is now used only to determine what user wants
                    {
                        // Sharepoint folders return this at the end of the enum
                        if (HR == unchecked((int)0x80004005))
                            break;
                        else
                            listPidls = new List<IntPtr>(); // sometimes it is a non-fatal error, ignored
                        break;
                    }

                    // Handle partial batches (fetched may be < BATCH_SIZE).
                    for (uint i = 0; i < fetched; i++)
                    {
                        IntPtr ptr = batch[i];
                        batch[i] = IntPtr.Zero; // clear slot immediately

                        if (ptr == IntPtr.Zero)
                            continue;

                        if (!includeFolders && !includeNonFolders)
                        {
                            Marshal.FreeCoTaskMem(ptr);
                        }
                        else if (includeFolders && includeNonFolders)
                        {
                            listPidls.Add(ptr);
                        }
                        else // Only one category is allowed; now we need to know what this item is.
                        {
                            bool itemIsFolder = IsFolderRel(csi, ptr); // only when needed
                            if ((itemIsFolder && !includeFolders) || (!itemIsFolder && !includeNonFolders))
                                Marshal.FreeCoTaskMem(ptr);
                            else
                                listPidls.Add(ptr);
                        }
                    }

                    // S_FALSE means end of enumeration (possibly with a short final batch already processed).
                    if (HR == S_FALSE)
                        break;

                    // Defensive guard against unusual providers returning S_OK with 0 items.
                    if (fetched == 0)
                        break;
                }
            }
            finally
            {
                if (IEnum != null)
                    Marshal.ReleaseComObject(IEnum);
            }
            return listPidls;

        }


        /// <summary>
        /// Returns the requested Items of the given Folder as a CShitemCollection
        /// </summary>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        public CShellItemCollection GetContents(CShellItem csi, SHCONTF flags)
        {
            var rVal = new CShellItemCollection(csi);
            if (csi.Folder is null)
                return rVal; // Added 10/22/2011 to deal with certain Virtual Folders
            CShellItem itm;
            // Debug.WriteLine("GContent " & Me.Path)
            // Dim StTime As DateTime = Now()
            // Dim content As ArrayList = GetContentPtrs(flags)       '11/09/2013 - should have been commented out originally
            // Debug.WriteLine("GPtrRel " & Now().Subtract(StTime).TotalMilliseconds.ToString & " ms")
            // StTime = Now()
            // For Each ptr In content
            foreach (IntPtr ptr in GetPidlsOfFolder(csi, flags))
            {
                if (ptr == IntPtr.Zero)                                               // 11/09/2013 - Investigate other
                {
                    Debug.WriteLine("Content=IntPtr.Zero while filling " + csi.FullPath);     // 11/09/2013 - Investigate other
                    Marshal.FreeCoTaskMem(ptr);                                          // 11/09/2013 - Investigate other
                    continue;                                                        // 11/09/2013 - Investigate other
                }
                else
                {
                    try                                         // ASUS Fix 'mod 06/27/09 First fix added
                    {
                        itm = CreateCShItem(ptr, csi);
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

        public static IntPtr GetShellNamespacePidl(Guid shellLocationGuid)
        {
            int hr = SHGetKnownFolderIDList(shellLocationGuid, 0, IntPtr.Zero, out IntPtr pidl);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return pidl; // caller owns memory
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

        #region Private methods

        /// <summary>
        /// Given a relative PIDL (relative to Me.Folder) determine if item is a Folder.
        /// </summary>
        /// <param name="ptr">A relative PIDL, relative to Me.Folder</param>
        /// <returns>True if item is a Folder, False is item is NOT a Folder.</returns>
        /// <remarks>Container files (such as .zip or .cab) are marked as a "Folder" in WinXP and above, so
        /// some further testing must be done on XP and above systems. We define such items as non-Folders.</remarks>
        private static bool IsFolderRel(CShellItem csi, IntPtr ptr)
        {
            bool IsFolderRelRet = default;
            IsFolderRelRet = false;         // assume it is not
            var attrFlag = SFGAO.FOLDER | SFGAO.STREAM;
            // Note: for GetAttributesOf, we must provide an array, in all cases with 1 element
            var aPidl = new IntPtr[1];
            aPidl[0] = ptr;
            csi.Folder.GetAttributesOf(1, aPidl, ref attrFlag);
            if (((attrFlag & SFGAO.FOLDER) != 0) && !((attrFlag & SFGAO.STREAM) != 0))
            {
                IsFolderRelRet = true;
            }

            return IsFolderRelRet;
        }


        #endregion

        private static void PopulateInitial(CShellItem csiOutput)
        {
            PopulateBasicAttributes(csiOutput);
            SetDisplayNameAndType(csiOutput);
            ComputeSortFlag(csiOutput);
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
                csiOutput.m_Path = "::{" + DesktopGUID.ToString() + "}";
            }
            else
            {
                csiOutput.m_IsFileSystem = (attrFlag & SFGAO.FILESYSTEM) != 0;
                csiOutput.m_Path = CShellItemFactory.GetFullPath(csiOutput);
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

            if (csiOutput.m_IsFolder && csiOutput.m_Path.Length == 3 && csiOutput.m_Path.Substring(1).Equals(@":\"))
            {
                csiOutput.m_IsDisk = true;
                try // 04/16/2012 Entire Try Block
                {
                    var disk = new System.Management.ManagementObject("win32_logicaldisk.deviceid=\"" + csiOutput.FullPath.Substring(0, 2) + "\"");
                    csiOutput.m_Length = Convert.ToInt64(disk["Size"]);
                    if ((Convert.ToUInt32(disk["DriveType"]).ToString() ?? "") == (4.ToString() ?? ""))
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
                    if (!DriveDict.ContainsKey(csiOutput.m_Path))
                    {
                        DriveDict.Add(csiOutput.m_Path, csiOutput.m_IsRemote);
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
            if (!(csiOutput.m_IsDisk || csiOutput.m_Path.StartsWith("::")))
            {
                if (csiOutput.m_Path.StartsWith(@"\\"))
                {
                    string[] tmp = csiOutput.m_Path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tmp.Length > 0 && tmp[0].Equals(CShellItemFactory.SystemName, StringComparison.InvariantCultureIgnoreCase))
                        csiOutput.m_IsRemote = false;
                    else
                        csiOutput.m_IsRemote = true;
                }
                else if (csiOutput.m_Path.Length > 2 && csiOutput.m_Path.Substring(1, 2).Equals(@":\"))
                {
                    string itemroot = csiOutput.m_Path.Substring(0, 3);
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

            // Fast path for filesystem file items (most common case)
            if (csi.m_IsFileSystem && !csi.m_IsFolder)
            {
                csi.m_DisplayName = GetFastFileDisplayName(csi.FullPath);

                // Cached type-name by extension
                csi.m_TypeName = GetCachedTypeNameForFile(csi.FullPath);

                // Rare fallback if we couldn't resolve a type name
                if (string.IsNullOrWhiteSpace(csi.m_TypeName))
                {
                    TryPopulateViaShell(csi, out _, out var shellTypeName);
                    if (!string.IsNullOrWhiteSpace(shellTypeName))
                        csi.m_TypeName = shellTypeName;
                }
            }
            else
            {
                // Folder and non-filesystem items: keep shell semantics
                if (TryPopulateViaShell(csi, out var shellDisplayName, out var shellTypeName))
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

        private static bool TryPopulateViaShell(CShellItem csi, out string displayName, out string typeName)
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

        public static string? GetFullPath(CShellItem csi)
        {
            var pidl = csi.PIDL;
            if (pidl == IntPtr.Zero) throw new ArgumentNullException(nameof(pidl));

            if (csi.m_IsFileSystem)
                return CPidl.GetFileSystemPath(pidl);
            else return CPidl.GetParsingPath(pidl);
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

    }
}
