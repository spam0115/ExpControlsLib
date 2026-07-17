using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public class ShellController : IDisposable
    {
        private static ShellController _instance = null;
        private bool _disposed;

        public static ShellController Instance
        {
            get
            {
                if (_instance is null)
                {
                    _instance = new ShellController(useSharedDesktopRoot: true);
                }
                
                return _instance;
            }
        }

        public CShellItemHierachyManager HierachyManager { get; private set; }
        public CShellItemUpdater ShellUpdater { get; private set; }
        public readonly static int FolderTimeout = 5; //seconds
   
        /// <summary>
        /// the desktop cShellIitem
        /// </summary>
        public static CShellItem? DesktopCSI { get; internal set; }

        /// <summary>
        /// Creates a controller with its own shell hierarchy and update service.
        /// Callers that construct a controller are responsible for disposing it.
        /// </summary>
        public ShellController() : this(useSharedDesktopRoot: false)
        {
        }

        private ShellController(bool useSharedDesktopRoot) {
            CShellItemFactory.Initialize(); //force the constructor to run
            DesktopCSI = CShellItemFactory.DesktopCSI;
            var hierarchyDesktop = useSharedDesktopRoot
                ? DesktopCSI
                : CShellItemFactory.Create(CSIDL.DESKTOP);
            HierachyManager = new CShellItemHierachyManager(hierarchyDesktop, hierarchyDesktop);
            ShellUpdater = new CShellItemUpdater(HierachyManager, (uint)SHCNE.DISKEVENTS);
        }

        public static ShellController Initialize() 
        { 
            if (_instance == null)
            {
                _instance = new ShellController(useSharedDesktopRoot: true);
            }
            return _instance;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ShellUpdater.Dispose();
        }

    }
}
