using WindowsApiLib.Shell;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{

    /// <summary>
    /// Provides an Icon and IConIndex manager between <see cref="WindowsApiLib.CShellItem">CShellItem</see> and 3 per process System Image Lists,
    /// one for Small Icons, one for Large Icons, and one for Extra Large Icons. The IConIndex for a given combination of base Icon and
    /// overlays is synchronized such that the same IConIndex will serve for each list. 
    /// </summary>
    /// <remarks>
    /// Correct usage is to obtain a CShellItem in any of the normal methods of the CShellItem Class. Typically, that CShellItem will
    /// not have its' IConIndex property assigned.<br />
    /// Then call <see cref="WindowsApiLib.SystemImageListManager.GetIconIndex">SystemImageListManager.GetIconIndex</see> to obtain the 
    /// true IConIndex into the per process ImageList.<br />
    /// GetIconIndex will query CShellItem.IconIndexNormal or CShellItem.IconIndexOpen to obtain the base IconIndex. This
    /// query will force CShellItem to do the system call to obtain that icon index (if needed) and set the correct CShellItem Property.<br />
    /// GetIconIndex will then determine what, if any, Overlays should be applied and, if not already obtained,
    /// obtain the Icon and place it in the per process ImageList and save the true IconIndex into the
    /// HashTable and return the correct IconIndex to the caller.<br />
    /// 
    /// Incorporates ExtraLarge and Jumbo Icon code from Jens Madsen as of 5/11/2013 which is a modification of Calum's ExtraLarge code
    /// </remarks>
    public class SystemImageListManager
    {
        #region        ImageList Related Constants
        // For ImageList manipulation
        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETIMAGELIST = LVM_FIRST + 3;

        private const int LVSIL_NORMAL = 0;
        private const int LVSIL_SMALL = 1;
        private const int LVSIL_STATE = 2;
        private const int LVSIL_GROUPHEADER = 3;

        private const int TV_FIRST = 0x1100;
        private const int TVM_SETIMAGELIST = TV_FIRST + 9;

        private const int TVSIL_NORMAL = 0;
        private const int TVSIL_STATE = 2;
        #endregion

        #region    Private Fields
        private static bool m_Initialized = false;
        private static IntPtr m_smImgList = IntPtr.Zero; // Handle to System Small ImageList
        private static IntPtr m_lgImgList = IntPtr.Zero; // Handle to System Large ImageList
        private static IntPtr m_xlgImgList = IntPtr.Zero; // Handle to System XtraLarge ImageList
        private static IntPtr m_jumboImgList = IntPtr.Zero; // Handle to System Jumbo ImageList
        private static readonly Hashtable m_Table = new Hashtable(128);
        private static readonly object SILMLock = new object();
        // Private Shared m_Mutex As New Mutex()

        public enum LVSIL
        {
            Normal = 0,
            Small = 1,
            State = 2,
            GroupHeader = 3
        }

        public enum SHIL
        {
            Small = 1,
            Large = 0,
            XLarge = 2,
            Jumbo = 4
        }

        #endregion

        #region    New
        /// <summary>
        /// Summary of Initializer.
        /// </summary>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        private static void Initialize()
        {
            if (m_Initialized)
            {
                return;
            }

            int dwFlag = (int)(SHGFI.USEFILEATTRIBUTES | SHGFI.SYSICONINDEX | SHGFI.SMALLICON);

            var shfi = new SHFILEINFO();
            string argpszPath = ".txt";
            m_smImgList = ShellAPI.SHGetFileInfo(argpszPath, FILE_ATTRIBUTE_NORMAL, ref shfi, cbFileInfo, dwFlag);

            Debug.Assert(!m_smImgList.Equals(IntPtr.Zero), "Failed to create Image Small ImageList");
            if (m_smImgList.Equals(IntPtr.Zero))
            {
                throw new Exception("Failed to create Small ImageList");
            }

            dwFlag = (int)(SHGFI.USEFILEATTRIBUTES | SHGFI.SYSICONINDEX | SHGFI.LARGEICON);

            string argpszPath1 = ".txt";
            m_lgImgList = ShellAPI.SHGetFileInfo(argpszPath1, FILE_ATTRIBUTE_NORMAL, ref shfi, cbFileInfo, dwFlag);



            Debug.Assert(!m_lgImgList.Equals(IntPtr.Zero), "Failed to create Image Small ImageList");
            if (m_lgImgList.Equals(IntPtr.Zero))
            {
                throw new Exception("Failed to create Large ImageList");
            }
            if (IsXpOrAbove())   // Lower versions do not support XLarge Icons
            {
                // UPDATE: Get the System IImageList object from the Shell for XLarge Icons:
                var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                SHGetImageListHandle(2, ref iidImageList, ref m_xlgImgList);
                Debug.Assert(!m_xlgImgList.Equals(IntPtr.Zero), "Failed to create Image XLarge ImageList");
                if (m_xlgImgList.Equals(IntPtr.Zero))
                {
                    throw new Exception("Failed to create XLarge ImageList");
                }
                if (IsVistaOrAbove())
                {
                    SHGetImageListHandle(4, ref iidImageList, ref m_jumboImgList);
                    Debug.Assert(!m_jumboImgList.Equals(IntPtr.Zero), "Failed to create Image Jumbo ImageList");
                    if (m_jumboImgList.Equals(IntPtr.Zero))
                    {
                        throw new Exception("Failed to create Jumbo ImageList");
                    }
                }
            }
            m_Initialized = true;
            // Call here; SHGetIconOverlayIndex requies that the System ImageList is initialized...
            GetOverlayIndices();
        }
        #endregion

        #region    Public Properties
        /// <summary>
    /// The Handle (as IntPtr) of the per process System Image List containing Small Icons.
    /// </summary>
        public static IntPtr hSmallImageList
        {
            get
            {
                return m_smImgList;
            }
        }
        /// <summary>
    /// The Handle (as IntPtr) of the per process System Image List containing Large Icons.
    /// </summary>
        public static IntPtr hLargeImageList
        {
            get
            {
                return m_lgImgList;
            }
        }
        /// <summary>
    /// The Handle (as IntPtr) of the per process System Image List containing Extra Large Icons.
    /// </summary>
        public static IntPtr hXLargeImageList
        {
            get
            {
                return m_xlgImgList;
            }
        }

        /// <summary>
    /// The Handle (as IntPtr) of the per process System Image List containing Jumbo Icons.
    /// </summary>
        public static IntPtr hJumboImageList
        {
            get
            {
                return m_jumboImgList;
            }
        }
        #endregion

        #region    Public Methods
        #region        GetIconIndex
        private static int mCnt;
        private static int bCnt;
        /// <summary>
        /// Location of the SHIL's overlay icons.
        /// </summary>
        /// <remarks>http://msdn.microsoft.com/en-us/library/windows/desktop/bb762183(v=vs.85).aspx </remarks>
        public static int ovlShare, ovlLink, ovlSlow, ovlDefault;

        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        private static void GetOverlayIndices()
        {
            ovlLink = SHGetIconOverlayIndex(null, (int)IDO_SHGIOI.IDO_SHGIOI_LINK);
            ovlShare = SHGetIconOverlayIndex(null, (int)IDO_SHGIOI.IDO_SHGIOI_SHARE);
            ovlSlow = SHGetIconOverlayIndex(null, (int)IDO_SHGIOI.IDO_SHGIOI_SLOWFILE);
            ovlDefault = SHGetIconOverlayIndex(null, (int)IDO_SHGIOI.IDO_SHGIOI_DEFAULT);
        }

        /// <summary>
        /// Queries the internal Hashtable of IConIndexes and returns the IconIndex for the requested CShellItem.
        /// </summary>
        /// <param name="item">The CShellItem for which the IconIndex is requested</param>
        /// <param name="GetOpenIcon">True if the "open" IconIndex is requested</param>
        /// <param name="GetSelectedIcon">True if the "Selected" Icon is requested</param>
        /// <returns>The true IConIndex into the per process ImageList for the CShellItem given as a parameter</returns>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static int GetIconIndex(CShellItem item, bool GetOpenIcon = false, bool GetSelectedIcon = false)
        {
            if (item is null) return 0;

            Initialize();
            bool HasOverlay = false;  // true if it's an overlay
            int rVal;     // The returned Index

            var dwflag = SHGFI.SYSICONINDEX | SHGFI.PIDL | SHGFI.ICON;
            int dwAttr = 0;
            // build Key into HashTable for this Item
            int Key = Convert.ToInt32(!GetOpenIcon ? item.IconIndexNormalOrig * 256 : item.IconIndexOpenOrig * 256);
            {
                ref var withBlock = ref item;
                if (withBlock.IsLink)
                {
                    Key = Key | 1;
                    dwflag = dwflag | SHGFI.LINKOVERLAY;
                    HasOverlay = true;
                }
                if (withBlock.IsShared)
                {
                    Key = Key | 2;
                    dwflag = dwflag | SHGFI.ADDOVERLAYS;
                    HasOverlay = true;
                }
                if (GetSelectedIcon)
                {
                    Key = Key | 4;
                    dwflag = dwflag | SHGFI.SELECTED;
                    HasOverlay = true;   // not really an overlay, but handled the same
                }
                if (m_Table.ContainsKey(Key))
                {
                    rVal = Convert.ToInt32(m_Table[Key]);
                    mCnt += 1;
                }
                else if (!HasOverlay)  // for non-overlay icons, we already have
                {
                    rVal = Key / 256;        // the right index -- put in table
                    m_Table[Key] = rVal;
                    bCnt += 1;
                }
                else // don't have iconindex for an overlay, get it. 
                {
                    // This is the tricky part -- add overlaid Icon to systemimagelist
                    // use of SmallImageList from Calum McLellan
                    var shfi = new SHFILEINFO();
                    var shfi_small = new SHFILEINFO();
                    IntPtr HR;
                    IntPtr HR_SMALL;
                    if (withBlock.IsFileSystem & !withBlock.IsDisk & !withBlock.IsFolder)
                    {
                        dwflag = dwflag | SHGFI.USEFILEATTRIBUTES;
                        dwAttr = FILE_ATTRIBUTE_NORMAL;
                    }
                    // UPDATE: OpenIcon with overlay
                    if (GetOpenIcon)
                    {
                        dwflag = dwflag | SHGFI.OPENICON;
                    }
                    //todo: i think these flags are already in CShellItem, no need to fetch them again
                    HR = SHGetFileInfo(withBlock.PIDL, dwAttr, ref shfi, cbFileInfo, dwflag);
                    HR_SMALL = SHGetFileInfo(withBlock.PIDL, dwAttr, ref shfi_small, cbFileInfo, dwflag | SHGFI.SMALLICON);
                    // m_Mutex.WaitOne()
                    int rVal2;
                    lock (SILMLock)
                    {
                        rVal = ImageList_ReplaceIcon(m_smImgList, -1, shfi_small.hIcon);
                        Debug.Assert(rVal > -1, "Failed to add overlaid small icon");
                        rVal2 = ImageList_ReplaceIcon(m_lgImgList, -1, shfi.hIcon);
                        Debug.Assert(rVal2 > -1, "Failed to add overlaid large icon");
                        Debug.Assert(rVal == rVal2, "Small & Large IconIndices are Different");

                        if (m_xlgImgList != IntPtr.Zero)  // Not set on Windows earlier than XP
                        {
                            var flags = ILD.NORMAL; // = 0    '5/9/2013 - JDP
                            if (item.IsLink)
                                flags = (ILD)((int)flags | INDEXTOOVERLAYMASK(ovlLink));
                            if (item.IsShared)
                                flags = (ILD)((int)flags | INDEXTOOVERLAYMASK(ovlShare));
                            var hIcon = IntPtr.Zero;
                            rVal = GetNonOverlayIndex(ref item, GetOpenIcon);
                            hIcon = ImageList_GetIcon(m_xlgImgList, rVal, flags);
                            rVal = ImageList_ReplaceIcon(m_xlgImgList, -1, hIcon);
                            int rCnt = ImageList_GetImageCount(m_jumboImgList);
                            Debug.Assert(rVal > -1, "Failed to add overlaid xl icon");
                            DestroyIcon(hIcon);
                            Debug.Assert(rVal == rVal2, "XL & Large Icon Indices are Different");
                        }
                        // This fails at rVal = ImageList_ReplaceIcon (incomplete implementation of interface??)
                        if (m_jumboImgList != IntPtr.Zero)  // Not set on Windows earlier than XP
                        {
                            var hIcon = IntPtr.Zero;
                            rVal = GetNonOverlayIndex(ref item, GetOpenIcon);

                            var flags = ILD.NORMAL;
                            if (item.IsLink)
                                flags = (ILD)((int)flags | INDEXTOOVERLAYMASK(ovlLink));
                            if (item.IsShared)
                                flags = (ILD)((int)flags | INDEXTOOVERLAYMASK(ovlShare));
                            hIcon = ImageList_GetIcon(m_jumboImgList, rVal, flags);
                            rVal = ImageList_ReplaceIcon(m_jumboImgList, -1, hIcon);
                            if (rVal < 0)        // 5/11/2013 - JDP
                            {
                                rVal = rVal2;        // 5/11/2013 - JDP
                            }                  // 5/11/2013 - JDP
                            Debug.Assert(rVal > -1, "Failed to add overlaid Jumbo icon");
                            DestroyIcon(hIcon);
                            Debug.Assert(rVal == rVal2, "Jumbo & Large Icon Indices are Different");
                            // END UPDATE
                        }

                    }
                    // m_Mutex.ReleaseMutex()
                    DestroyIcon(shfi.hIcon);
                    DestroyIcon(shfi_small.hIcon);
                    if (rVal < 0 || rVal != rVal2)
                    {
                        throw new ApplicationException("Failed to add Icon for " + item.DisplayName);
                    }
                    m_Table[Key] = rVal;
                }
            }
            return rVal;
        }

        // UPDATE: Add GetNonOverlayIndex
        // Returns the normal non-overlay Icon for XL overlay Icons
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static int GetNonOverlayIndex(ref CShellItem item, bool GetOpenIcon = false)
        {
            Initialize();
            int rVal;     // The returned Index

            // build Key into HashTable for this Item
            int Key = Convert.ToInt32(!GetOpenIcon ? item.IconIndexNormalOrig * 256 : item.IconIndexOpenOrig * 256);

            if (m_Table.ContainsKey(Key))
            {
                rVal = Convert.ToInt32(m_Table[Key]);
                mCnt += 1;
            }
            else                        // for non-overlay icons, we already have
            {
                rVal = Key / 256;        // the right index -- put in table
                m_Table[Key] = rVal;
                bCnt += 1;
            }
            return rVal;
        }

        /// <summary>
        /// Returns the index of the overlay icon in the system image list.
        /// OBS! The System ImageList must be instantiated for this method to work!
        /// </summary>
        /// <param name="pszIconPath">A pointer to a null-terminated string of maximum length MAX_PATH that contains the fully qualified path of the file that contains the icon, or NOTHING to retrieve one of then standard overlay icons.</param>
        /// <param name="iIconIndex">The icon's index in the file pointed to by pszIconPath. To request a standard overlay icon, set pszIconPath to NULL, and iIconIndex to one of the <seealso cref="SystemImageListManager.IDO_SHGIOI "/> flags.</param>
        /// <returns>Returns the index of the overlay icon in the system image list if successful, or -1 otherwise.</returns>
        /// <remarks>Icon overlays are part of the system image list. They have two identifiers. The first is a one-based overlay index that identifies the overlay relative to other overlays in the image list. The other is an image index that identifies the actual image. These two indexes are equivalent to the values that you assign to the iOverlay and iImage parameters, respectively, when you add an icon overlay to a private image list with ImageList_SetOverlayImage. SHGetIconOverlayIndex returns the overlay index. To convert an overlay index to its equivalent image index, call <seealso  cref= "INDEXTOOVERLAYMASK " />. 
        /// Note: After the image has been loaded into the system image list during initialization, it cannot be changed. The file name and index specified by pszIconPath and iIconIndex are used only to identify the icon overlay. SHGetIconOverlayIndex cannot be used to modify the system image list.
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/bb762183(v=vs.85).aspx </remarks>
        [DllImport("Shell32.dll", EntryPoint = "SHGetIconOverlayIndex")]
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static extern int SHGetIconOverlayIndex([In()][MarshalAs(UnmanagedType.LPTStr)] string pszIconPath, int iIconIndex);

        // Private Shared Function INDEXTOOVERLAYMASK(ByVal i As Integer) As Integer
        /// <summary>
        /// Mockup of Shell Macros.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        /// <remarks>Prepares the index of an overlay mask so that ImageList_GetIcon and ImageList_Draw can use it. </remarks>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static int INDEXTOOVERLAYMASK(int i)
        {
            return i << 8;
        }
        public static int INDEXTOSTATEIMAGEMASK(int i)
        {
            return i << 12;
        }

        /// <summary>
        /// Used by <see cref="SHGetIconOverlayIndex "/> to request a standard overlay icon: 
        /// Set pszIconPath to NULL, and iIconIndex to one of the following values:
        /// </summary>
        /// <remarks></remarks>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public enum IDO_SHGIOI
        {
            IDO_SHGIOI_SHARE = 0xFFFFFFF,
            IDO_SHGIOI_LINK = 0xFFFFFFE,
            IDO_SHGIOI_SLOWFILE = unchecked((int)0xFFFFFFFD),
            IDO_SHGIOI_DEFAULT = unchecked((int)0xFFFFFFFC)
        }


        // Private Shared Sub DebugShowImages(ByVal useSmall As Boolean, ByVal iFrom As Integer, ByVal iTo As Integer)
        // Dim RightIcon As Icon = GetIcon(iFrom, Not useSmall)
        // Dim rightIndex As Integer = iFrom
        // Do While iFrom <= iTo
        // Dim ico As Icon = GetIcon(iFrom, useSmall)
        // Dim fShow As New frmDebugShowImage(rightIndex, RightIcon, ico, IIf(useSmall, "Small ImageList", "Large ImageList"), iFrom)
        // fShow.ShowDialog()
        // fShow.Dispose()
        // iFrom += 1
        // Loop
        // End Sub
        #endregion

        #region        GetIcon
        /// <summary>
        /// Returns a GDI+ copy of a Large or Small icon from the ImageList
        /// at the specified index.</summary>
        /// <param name="Index">The IconIndex of the desired Icon</param>
        /// <param name="smallIcon">Optional, default = False. If True, return the
        ///   icon from the Small ImageList rather than the Large.</param>
        /// <returns>The specified Icon or Nothing</returns>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static Icon GetIcon(int Index, bool smallIcon = false)

        {
            Initialize();
            Icon icon = null;
            IntPtr hIcon;
            // Customisation to return a small image
            if (smallIcon)
            {
                hIcon = ImageList_GetIcon(m_smImgList, Index, 0);
            }
            else
            {
                hIcon = ImageList_GetIcon(m_lgImgList, Index, 0);
            }
            if (!(hIcon == null))
            {
                icon = Icon.FromHandle(hIcon);
            }
            return icon;
        }

        /// <summary>
        /// Returns a GDI+ copy of an Extra Large Icon from the ImageList 
        /// </summary>
        /// <param name="index"></param>
        /// <returns>The desired Icon or Nothing</returns>
        /// <remarks></remarks>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static Icon GetXLIcon(int index)
        {
            Initialize();
            Icon icon = null;
            if (m_xlgImgList != IntPtr.Zero)
            {
                IntPtr hIcon;
                hIcon = ImageList_GetIcon(m_xlgImgList, index, 0);
                if (!(hIcon == null))
                {
                    icon = Icon.FromHandle(hIcon);
                }
            }
            return icon;
        }

        /// <summary>
        /// Returns a GDI+ copy of an Jumbo Icon from the ImageList 
        /// </summary>
        /// <param name="index"></param>
        /// <returns>The desired Icon or Nothing</returns>
        /// <remarks></remarks>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static Icon GetJumboIcon(int index)
        {
            Initialize();
            Icon icon = null;
            if (m_jumboImgList != IntPtr.Zero)
            {
                IntPtr hIcon;
                hIcon = ImageList_GetIcon(m_jumboImgList, index, 0);
                if (!(hIcon == null))
                {
                    icon = Icon.FromHandle(hIcon);
                }
            }
            return icon;
        }

        #endregion

        #region        SetListViewImageList
        ///<summary>
        ///Associates a SysImageList with a ListView control
        ///</summary>
        ///<param name="listView">ListView control to associate ImageList with</param>
        ///<param name="forLargeIcons">True=Set Large Icon List
        ///<param name="forStateImages">Whether to add ImageList as StateImageList</param>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static void SetListViewImageList(ListView listView, bool forLargeIcons, bool forStateImages)
        {
            Initialize();
            int wParam = LVSIL_NORMAL;
            var HImageList = m_lgImgList;
            if (!forLargeIcons)
            {
                wParam = LVSIL_SMALL;
                HImageList = m_smImgList;
            }
            if (forStateImages)
            {
                wParam = LVSIL_STATE;
            }
            SendMessage(listView.Handle, LVM_SETIMAGELIST, wParam, HImageList);
        }

        ///<summary>
        /// Associates a SysImageList with a ListView control
        ///</summary>
        ///<param name="listView">ListView control to associate ImageList with</param>
        ///<param name="Usage">State, Group, Normal, Small</param>
        ///<param name="IIlSize">Size of Images</param>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static void SetListViewImageList(ListView listView, LVSIL Usage, SHIL IIlSize)
        {

            Initialize();
            int wParam = (int)Usage;
            var HImageList = m_lgImgList;
            if (IIlSize == SHIL.Small)
            {
                HImageList = m_smImgList;
            }
            else if (IIlSize == SHIL.Jumbo)
            {
                HImageList = m_jumboImgList;
            }
            else if (IIlSize == SHIL.XLarge)
            {
                HImageList = m_xlgImgList;
            }
            SendMessage(listView.Handle, LVM_SETIMAGELIST, wParam, HImageList);
        }
        #endregion

        #region        SetTreeViewImageList
        /// <summary>
        /// Associates a SysImageList with a TreeView control
        /// </summary>
        /// <param name="treeView">TreeView control to associate the ImageList with</param>
        /// <param name="forStateImages">Whether to add ImageList as StateImageList</param>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static void SetTreeViewImageList(TreeView treeView, bool forStateImages)
        {
            Initialize();
            int wParam = LVSIL_NORMAL;
            if (forStateImages)
            {
                wParam = LVSIL_STATE;
            }
            // Dim HR As Integer                      '12/31/2013
            // HR = SendMessage(treeView.Handle, _    '12/31/2013
            SendMessage(treeView.Handle, TVM_SETIMAGELIST, wParam, m_smImgList);
        }

        #endregion

        #endregion
    }
}