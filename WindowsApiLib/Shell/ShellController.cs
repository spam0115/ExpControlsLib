using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public class ShellController
    {
        public static ShellController Instance = null!;

        public CShellItemHierachyManager HierachyManager { get; private set; }
        public CShellItemUpdater ShellUpdater { get; private set; }
   
        /// <summary>
        /// the desktop cShellIitem
        /// </summary>
        public static CShellItem? DesktopCSI { get; private set; }

        private ShellController() {

            HierachyManager = new CShellItemHierachyManager();
            CShellItemFactory.Initialize(HierachyManager); //force the constructor to run
            DesktopCSI = CShellItemFactory.GetDesktopRoot();

            HierachyManager.Root = DesktopCSI;
            HierachyManager.DesktopCSI = DesktopCSI;
            ShellUpdater = new CShellItemUpdater(HierachyManager, (uint)SHCNE.DISKEVENTS);
        }

        public static ShellController Initialize() { 
            if (Instance == null)
            {
                Instance = new ShellController();
            }
            return Instance;
        }

        /// <summary>
        /// Loads folder contents for the given CShellItem.  IE, it populates the directories and files
        /// members.
        /// </summary>
        /// <remarks>
        /// the reason this function has a return value is because the CShellItem passed in may be a duplicate of
        /// one that already exists in the hierarchy.  In that case, the original hierarchy version will be returned.
        /// </remarks>
        /// <param name="csi"></param>
        /// <returns>A CShellItem from the hierarchy manager.  It may or may not be the original CShellItem passed in.</returns>
        public CShellItem? LoadFolderContents(CShellItem csi, SHCONTF flags)
        {
            if (csi == null) return null;

            //CShellItem target = HierachyManager.Add(csi); //ensure the item exists in the hierarchy

            //if (target is null)
            //{
            //    Debug.WriteLine("Failed to find or add item to the shell item hierarchy: '" + csi.FullPath + "'");

            //    return null;
            //}

            var target = csi;

            lock (target)
            {
                var contents = CShellItemFactory.GetContents(target, flags);

                if ((flags & SHCONTF.FOLDERS) > 0)
                {
                    if (target.m_directories is null)
                        target.m_directories = new CShellItemCollection(target);
                    else target.m_directories.Clear();
                }

                if ((flags & SHCONTF.NONFOLDERS) > 0)
                {
                    if (target.m_files is null)
                        target.m_files = new CShellItemCollection(target);
                    else target.m_files.Clear();
                }

                foreach (var item in contents.Items)
                {
                    if (item.IsFolder && target.m_directories != null)
                        target.m_directories.Add(item);
                    else if (!item.IsFolder && target.m_files != null)
                        target.m_files.Add(item);
                }
            }

            return target;
        }


    }
}
