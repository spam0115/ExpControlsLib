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

            var CShellItem_Factory =  CShellItemFactory.Instance; //force the constructor to run
            (Desktop, DesktopCSI) = CShellItemFactory.GetDesktopRoot();

            HierachyManager = new CShellItemHierachyManager(DesktopCSI);
            ShellUpdater = new CShellItemUpdater(HierachyManager, (uint)SHCNE.DISKEVENTS);
            //ShellUpdater = new CShellItemUpdater(HierachyManager.Root);

        }
    }
}
