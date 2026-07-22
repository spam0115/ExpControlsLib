using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;
using MethodInvoker = System.Windows.Forms.MethodInvoker;

namespace ExpControlsLib
{
    /// <summary>Implements folder navigation history, back/forward navigation, and parent-folder traversal.</summary>
    [SupportedOSPlatform("windows")]
    public partial class ExpList
    {
        #region Navigation

        /// <summary>
        /// Navigates back to the previous folder in the history.
        /// </summary>
        public async Task GoBack()
        {
            Debug.WriteLine("ExpList: GoBack Begin");
            try
            {
                await _navigation.GoBackAsync(item => LoadDirectoryBaseAsync(item, true));
            }
            finally
            {
                Debug.WriteLine("ExpList: GoBack End");
            }
        }

        /// <summary>
        /// Navigates forward to the next folder in the history.
        /// </summary>
        public async Task GoForward()
        {
            Debug.WriteLine("ExpList: GoForward Begin");
            try
            {
                await _navigation.GoForwardAsync(item => LoadDirectoryBaseAsync(item, true));
            }
            finally
            {
                Debug.WriteLine("ExpList: GoForward End");
            }
        }

        /// <summary>
        /// Navigates to the parent folder of the currently loaded folder.
        /// </summary>
        public async Task GoUp()
        {
            Debug.WriteLine("ExpList: GoUp Begin");
            try
            {
                if (_navigation.Current?.Parent is { } parent)
                {
                    await LoadDirectoryAsync(parent, true);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: GoUp End");
            }
        }

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate back to.
        /// </summary>
        public bool CanGoBack => _navigation.CanGoBack;

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate forward to.
        /// </summary>
        public bool CanGoForward => _navigation.CanGoForward;

        /// <summary>
        /// Gets a value indicating whether the current folder has a parent folder to navigate to.
        /// </summary>
        public bool CanGoUp => _navigation.CanGoUp;

        #endregion
    }
}
