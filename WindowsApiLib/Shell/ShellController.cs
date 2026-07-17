using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public class ShellController
    {
        public static ShellController Instance = null!;

        public CShellItemHierachyManager HierachyManager { get; private set; }
        public CShellItemUpdater ShellUpdater { get; private set; }
        public readonly static int FolderTimeout = 5; //seconds
   
        /// <summary>
        /// the desktop cShellIitem
        /// </summary>
        public static CShellItem? DesktopCSI { get; internal set; }

        private ShellController() {
            CShellItemFactory.Initialize(); //force the constructor to run
            DesktopCSI = CShellItemFactory.DesktopCSI;
            HierachyManager = new CShellItemHierachyManager(DesktopCSI);
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
        public void EnsureChildrenPopulatedAndRecent(CShellItem csi, SHCONTF flags)
        {
            bool wantFolders = (flags & SHCONTF.FOLDERS) > 0;
            bool wantFiles = (flags & SHCONTF.NONFOLDERS) > 0;
            bool filesInvalid = csi.FilesCollectionTimestamp is null || (DateTime.Now - csi.FilesCollectionTimestamp > new TimeSpan(0, 0, FolderTimeout));
            bool foldersInvalid = csi.DirsCollectionTimestamp is null || (DateTime.Now - csi.DirsCollectionTimestamp > new TimeSpan(0, 0, FolderTimeout));

            if (wantFiles && filesInvalid) wantFiles = true;
            else wantFiles = false;

            if (wantFolders && foldersInvalid) wantFolders = true;
            else wantFolders = false;

            if (!wantFiles && !wantFolders) return;

            csi.LoadFolderContents(wantFiles, wantFolders);
        }

    }
}
