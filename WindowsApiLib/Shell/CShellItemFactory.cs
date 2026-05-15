using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;

namespace WindowsApiLib.Shell
{
    public class CShellItemFactory
    {
        public static CShellItemFactory Instance { get; } = new CShellItemFactory();


        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        public static IShellFolder Desktop { get; private set; }

        // The DesktopBase is set up via Sub New() (one time only) and
        // disposed of only when DesktopBase is finally disposed of
        public static CShellItem? DesktopCSI { get; private set; }

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


        private CShellItemFactory() {

            EmptyPidl = CreateEmptyPidl();
            DesktopPidl = GetShellNamespacePidl(ShellNamespaceGuids.DesktopFileSystem);

            int HR;
            // firstly determine what the local machine calls a "System Folder" and "My Computer"
            IntPtr tmpPidl = IntPtr.Zero;
            HR = SHGetSpecialFolderLocation(0, (int)CSIDL.DRIVES, ref tmpPidl);
            var shfi = new SHFILEINFO();
            var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            int dwAttr = 0;
            SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
            StrSystemFolder = shfi.szTypeName;
            StrMyComputer = shfi.szDisplayName;
            Marshal.FreeCoTaskMem(tmpPidl);

            // With That done, now set up Desktop CShellItem
            IShellFolder m_Folder = null;
            HR = SHGetDesktopFolder(ref m_Folder);
            Desktop = m_Folder;

            var csi = new CShellItem(DesktopPidl);
            DesktopCSI = csi;
            csi.m_Folder = Desktop;
            csi.m_Path = "::{" + DesktopGUID.ToString() + "}";
            csi.m_IsFolder = true;
            csi.m_HasSubFolders = true;
            csi.m_IsBrowsable = false;
            csi.m_TypeName = StrSystemFolder;   // not returned correctly by SHGetFileInfo
            csi.m_IconIndexNormal = shfi.iIcon;
            csi.m_IconIndexOpen = shfi.iIcon;
            csi.m_HasDispType = true;
            csi.IsDropTarget = true;
            csi.m_IsReadOnly = false;
            csi.m_IsReadOnlySetup = true;
            csi.SetDispType();

            csi.m_updater = new CShellItemUpdater(csi); // Start the Notification Process

            DeskTopDirectory = GetCShItem(CSIDL.DESKTOPDIRECTORY);

            // also get local name for "My Documents"
            var pchEaten = default(int);
            tmpPidl = IntPtr.Zero;
            int argpdwAttributes = default;
            HR = Desktop.ParseDisplayName(default, default, "::{" + ShellNamespaceGuids.Documents.ToString() + "}", ref pchEaten, ref tmpPidl, ref argpdwAttributes);
            shfi = new SHFILEINFO();
            dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
            dwAttr = 0;

            SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
            StrMyDocuments = shfi.szDisplayName;
            Marshal.FreeCoTaskMem(tmpPidl);

            // Get the SystemName for Remote item testing
            SystemName = Environment.MachineName;

            RecycleBin = GetCShItem(CSIDL.BITBUCKET);

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
        public static CShellItem GetCShItem(string path)
        {
            CShellItem GetCShItemRet = default;
            GetCShItemRet = null;    // assume failure
            int HR;
            IntPtr tmpPidl = IntPtr.Zero;
            int argpchEaten = 0;
            int argpdwAttributes = 0;
            HR = DesktopCSI.Folder.ParseDisplayName(0, IntPtr.Zero, path, ref argpchEaten, ref tmpPidl, ref argpdwAttributes);
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
                return DesktopCSI;
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


        /// <summary>Given an IntPtr representation of a PIDL,
        /// GetCshItem finds or creates a CShellItem and places any new CShellItem into the internal tree.
        /// The tree is expanded (filled in) as necessary to locate the CShellItem or to locate the proper
        /// placement of a new Item. The assumption is that the Folder system actually contains the item
        /// that is requested -- File or Directory.Exists equivalent. Returns Nothing on errors such as
        /// non-existant item.
        /// </summary>
        /// <param name="pidl">Absolute (Full) Pidl of item to be Found or Created</param>
        /// <returns>A CShellItem or, in case of error, Nothing</returns>
        public static CShellItem GetCShItem(IntPtr pidl)
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
                Marshal.ThrowExceptionForHR(HR);
#endif
            }    // Removed 10/22/2011 - restored 11/13/2013
            return rVal;
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
            foreach (IntPtr ptr in GetContentPtrs(csi, flags))
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
                        itm = new CShellItem(ptr, csi);
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
        /// This is the "engine" that maintains the hierarchical relationship between items.
        /// - Method: internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        /// - Logic: It traverses the cached tree from the Desktop down to the target PIDL.If an item doesn't
        /// exist in the cache, it expands the parent folders to find or place the item.This ensures that every
        /// CShellItem is correctly linked to its parent in the internal structure.
        /// 
        /// BrowseTo locates the desired item and places it in its proper location on the internal tree.
        /// Any and all sub-directories that need to be populated in the tree in order to properly place
        /// the desired item, are populated. This is the programatic equivalent of Browsing to a node in 
        /// <code>ExpTree's</code> TreeView.<br /> 
        /// BrowseTo also returns the Parent CShellItem. 
        /// If the desired CShellItem does not exist, the returned Parent is the CShellItem that would be the
        /// Immediate ancestor (containing CShellItem or Parent) of the desired item should it be created.
        /// </summary>
        /// <param name="absPidl">A Absolute PIDL whose CShellItem is to be found</param>
        /// <param name="Parent">Output parameter -- Immediate Ancestor CShellItem of the found item OR 
        /// the CShellItem that would contain the item if it existed OR Nothing if NO Immediate ancestor found 
        /// in the Shell namespace. </param>
        /// <returns>The desired CShellItem or, if not found, Nothing.</returns>
        /// <remarks>A by-product of this search is that any sub-dirs of the tree along the path will be 
        /// populated with their sub directories.
        /// It is logically possible that NO Immediate ancestor can be found.
        /// For Example: GetCShItem(Path) may be given a string specifying a non-existant directory.
        /// (eg -- C:\Test\NonExistant\junk.txt). 
        /// In that case, and that case only, Parent may be returned as Nothing.</remarks>
        internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        {
            CShellItem csi;
            var baseItem = DesktopCSI;
            CShellItem browseToRet = default;
            browseToRet = null;     // avoid VB2005 Warning
            Parent = default;

            bool FoundIt = false;      // True if we found item or an ancestor
                                       // Dim FirstWithThisBase As Boolean = True     '6/30/2012 Flag to prevent infinite loop
            while (!FoundIt)
            {
                foreach (var currentCSI in baseItem.Directories)
                {
                    csi = currentCSI;    // 7/2/2012 should use Directories here
                    if (IsAncestorOf(csi.PIDL, absPidl))
                    {
                        if (CPidl.IsEqual(csi.PIDL, absPidl))  // we found the desired item
                        {
                            Parent = baseItem;
                            return csi;
                        }
                        else            // Found an ancestor
                        {
                            baseItem = csi;
                            Parent = csi;
                            FoundIt = true;
                            break;
                        }
                    }
                }
                if (!FoundIt)
                {
                    // UPDATE: Check for files in the desktop
                    foreach (var currentCSI1 in DesktopCSI.Files)
                    {
                        csi = currentCSI1;           // Files will do an UpdateRefresh in case of missing a CREATE
                        if (CPidl.IsEqual(csi.PIDL, absPidl))
                        {
                            Parent = DesktopCSI;
                            return csi;
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
                if (!IsAncestorOf(baseItem.PIDL, absPidl, true))  // Don't have immediate ancestor
                {
                    // FirstWithThisBase = True    '6/30/2012
                    FoundIt = false;     // go around again
                }
                else
                {
                    Parent = baseItem;
                    foreach (var currentCSI2 in baseItem.Directories)
                    {
                        csi = currentCSI2;        // 6/6/2012 modified 7/2/2012 Directories needed here
                        if (CPidl.IsEqual(csi.PIDL, absPidl))
                        {
                            return csi;
                        }
                    }
                    // Not in Dirs, so look in Files 6/6/2012 fix
                    foreach (var currentCSI3 in baseItem.Files)
                    {
                        csi = currentCSI3;              // Files will do an UpdateRefresh in case of missing a CREATE
                        if (CPidl.IsEqual(csi.PIDL, absPidl))
                        {
                            return csi;
                        }
                    }
                    // fall thru here means it doesn't exist or we can't find it because of funny PIDL from SHParseDisplayName
                    return null;
                }
            }

            return browseToRet;
        }


        /// <summary>
        /// Returns the requested Items of this Folder as a List of relative PIDLs 
        /// (caller must free the pidls after use).
        /// </summary>
        /// <param name="csi">The CShellItem of the Folder to be enumerated</param>
        /// <param name="flags">A set of one or more SHCONTF flags indicating which items to return</param>
        /// <returns>On error, returns an empty (count=0) List. Otherwise, returns the relative PIDLs of
        /// the requested (via flags param) items in this Folder.</returns>
        private List<IntPtr> GetContentPtrs(CShellItem csi, SHCONTF flags)
        {
            var rVal = new List<IntPtr>();
            int HR;
            IEnumIDList IEnum = null;
            // UPDATE: Vista and above strictly respect the SHCONTF flags. The "flags" param is now used only to determine what user wants
            HR = csi.Folder.EnumObjects(0, SHCONTF.INCLUDEHIDDEN | SHCONTF.FOLDERS | SHCONTF.NONFOLDERS, ref IEnum);     // new code (12/11/09)
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
                        bool itemIsFolder = IsFolderRel(csi, ptr); //don't do this earlier so we can sometimes avoid the expense

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
            rVal = new List<IntPtr>(); // sometimes it is a non-fatal error,ignored
            goto NORMAL;
        }

        /// <summary>
        /// Given a relative PIDL (relative to Me.Folder) determine if item is a Folder.
        /// </summary>
        /// <param name="ptr">A relative PIDL, relative to Me.Folder</param>
        /// <returns>True if item is a Folder, False is item is NOT a Folder.</returns>
        /// <remarks>Container files (such as .zip or .cab) are marked as a "Folder" in WinXP and above, so
        /// some further testing must be done on XP and above systems. We define such items as non-Folders.</remarks>
        private bool IsFolderRel(CShellItem csi, IntPtr ptr)
        {
            bool IsFolderRelRet = default;
            IsFolderRelRet = false;         // assume it is not
            var attrFlag = SFGAO.FOLDER | SFGAO.STREAM;
            // Note: for GetAttributesOf, we must provide an array, in all cases with 1 element
            var aPidl = new IntPtr[1];
            aPidl[0] = ptr;
            csi.Folder.GetAttributesOf(1, aPidl, ref attrFlag);
            if (((attrFlag & SFGAO.FOLDER) != 0) && !((attrFlag & SFGAO.STREAM) != 0))         // XP or above
            {
                IsFolderRelRet = true;
            }

            return IsFolderRelRet;
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
            return ILIsParent(AncestorPidl, ChildPidl, fParent);
        }




        #endregion
    }
}
