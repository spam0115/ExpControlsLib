using System;
using System.Collections.Generic;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public class ShellController
    {
        public CShellItemHierachyManager HierachyManager { get; private set; }
        public CShellItemUpdater ShellUpdater { get; private set; }
   
        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        public static IShellFolder Desktop { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public static CShellItem? DesktopCSI { get; private set; }


        public ShellController() {

            HierachyManager = new CShellItemHierachyManager();
            CShellItemFactory.Initialize(HierachyManager); //force the constructor to run
            (Desktop, DesktopCSI) = CShellItemFactory.GetDesktopRoot();

            HierachyManager.Root = DesktopCSI;
            ShellUpdater = new CShellItemUpdater(HierachyManager, (uint)SHCNE.DISKEVENTS);

        }

        /// <summary>
        /// Loads folder contents
        /// </summary>
        /// <remarks>
        /// the reason this function has a return value is because the CShellItem passed in may be a duplicate of
        /// one that already exists in the hierarchy.  In that case, the original hierarchy version will be returned.
        /// </remarks>
        /// <param name="csi"></param>
        /// <returns></returns>
        public CShellItem LoadFolderContents(CShellItem csi)
        {
            CShellItem target = HierachyManager.AddToHierarchy(csi); //ensure the item exists in the hierarchy

            var contents = target.GetContents(SHCONTF.INCLUDEHIDDEN | SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);

            if (target.m_Directories is null) target.m_Directories = new CShellItemCollection(target);
            if (target.m_Files is null) target.m_Files = new CShellItemCollection(target);

            foreach (var item in contents.Items)
            {
                if (item.IsFolder)
                    target.m_Directories.Add(item);
                else
                    target.m_Files.Add(item);
            }

            return target;
        }

    }
}
