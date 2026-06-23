using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public class ShellController
    {
        public static ShellController Instance = null!;

        public CShellItemHierachyManager HierachyManager { get; private set; }
        public CShellItemUpdater ShellUpdater { get; private set; }
        public readonly static int FolderTimeout = 5;
   
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

        public static ShellController Initialize() 
        { 
            if (Instance == null)
            {
                Instance = new ShellController();
            }
            return Instance;
        }

        /// <summary>
        /// Conditionally loads child objects depending on whether or not they are old.
        /// </summary>
        /// <param name="csi"></param>
        /// <param name="flags"></param>
        public void EnsureChildrenPopulated(CShellItem csi, SHCONTF flags)
        {
            bool wantFolders = (flags & SHCONTF.FOLDERS) > 0;
            bool wantFiles = (flags & SHCONTF.NONFOLDERS) > 0;
            bool filesOld = csi.FilesCollectionTimestamp is null || (DateTime.Now - csi.FilesCollectionTimestamp > new TimeSpan(0, 0, FolderTimeout));
            bool foldersOld = csi.DirsCollectionTimestamp is null || (DateTime.Now - csi.DirsCollectionTimestamp > new TimeSpan(0, 0, FolderTimeout));

            if (wantFolders && foldersOld && wantFiles && filesOld)
            {
                LoadFolderContents(csi, SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);
            }
            else if (filesOld && wantFiles) {
                LoadFolderContents(csi, SHCONTF.NONFOLDERS);
            }
            else if (foldersOld && wantFolders)
            {
                LoadFolderContents(csi, SHCONTF.FOLDERS);
            }
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

            var contents = CShellItemFactory.GetContents((CShellItem)csi, flags);

            lock (csi)
            {
                if ((flags & SHCONTF.FOLDERS) > 0)
                {
                    lock (csi._directoriesLock) { 
                        if (!csi.DirectoriesInitialized)
                        {
                            csi.Directories.Clear();
                        }
                        var folders = contents.Where(o => o.IsFolder == true).ToList();
                        csi.Directories = new CShellItemCollection(csi, folders);
                    }
                }

                if ((flags & SHCONTF.NONFOLDERS) > 0)
                {
                    lock (csi._filesLock)
                    {
                        if (!csi.FilesInitialized)
                        {
                            csi.Files.Clear();
                        }

                        var files = contents.Where(o => o.IsFolder == false).ToList();
                        csi.Files = new CShellItemCollection(csi, files);
                    }
                }
            }

            return csi;
        }


    }
}
