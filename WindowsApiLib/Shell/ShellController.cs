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
            CShellItemFactory.HierachyManager = HierachyManager;    

            //ShellUpdater = new CShellItemUpdater(HierachyManager.Root);

        }

        public static void LoadFolderContents(CShellItem csi) //move: to shellcontroller
        {
            var contents = csi.GetContents(SHCONTF.INCLUDEHIDDEN | SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);

            if (csi.m_Directories is null) csi.m_Directories = new CShellItemCollection(csi);
            if (csi.m_Files is null) csi.m_Files = new CShellItemCollection(csi);

            foreach (var item in contents.Items)
            {
                if (item.IsFolder)
                    csi.m_Directories.Add(item);
                else
                    csi.m_Files.Add(item);
            }
        }

    }
}
