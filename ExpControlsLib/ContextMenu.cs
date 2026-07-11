using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning; // Added to annotate platform support
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    /// <summary>
    /// Result of a <see cref="ContextMenu.ShowMenu"/> call.
    /// </summary>
    public readonly struct ContextMenuResult
    {
        /// <summary>True if the user selected a command; false if the menu was cancelled.</summary>
        public bool Success { get; init; }

        /// <summary>The verb string (e.g. "open", "rename") from GetCommandString, or null for custom commands.</summary>
        public string? Verb { get; init; }

        /// <summary>The raw command information from the shell context menu.</summary>
        public CMInvokeCommandInfoEx CommandInfo { get; init; }

        /// <summary>The HRESULT from invoking the command on the original IContextMenu, or 0 if not invoked.</summary>
        public int InvokeHResult { get; init; }
    }

    /// <summary>
    /// A disposable scope that manages the lifetime of a "New" submenu created by
    /// <see cref="ContextMenu.SetUpNewMenu"/>. Disposing this scope releases all
    /// associated COM objects and clears the menu handle.
    /// </summary>
    public sealed class NewMenuScope : IDisposable
    {
        private readonly ContextMenu _owner;
        private bool _disposed;

        internal NewMenuScope(ContextMenu owner)
        {
            _owner = owner;
        }

        /// <summary>The HMENU of the "New" submenu within the parent context menu.</summary>
        public IntPtr MenuHandle => _owner.newMenuPtr;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _owner.ReleaseNewMenu();
            }
        }
    }

    /// <summary>
    /// WindowsContextMenu provides the infrastructure for displaying a Windows Context Menu on a Control
    /// and for Invoking a Command selected by the user from that Context Menu. Cascaded sub-menus are created,
    /// displayed, and responded to as required.
    /// The Context Menu applies to all CShItems
    /// passed to the ShowMenu Function and all CShItems must be in the same Folder. 
    /// </summary>
    /// <remarks>Though specifically designed for ListView and TreeView Controls, this Class will work for any Control which
    ///          is associated with a single Folder and can provide CShItems from that Folder.</remarks>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ContextMenu : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        private IContextMenu cntxMenuBase = null;
        /// <summary>Extends IContextMenu to support owner-drawn items and submenus (HandleMenuMsg).</summary>
        internal IContextMenu2 cntxMenuExtended = null;
        /// <summary>Extends IContextMenu2 to support advanced cascading menus with result return (HandleMenuMsg2).</summary>
        internal IContextMenu3 cntxMenuCascading = null;

        /// <summary>Base interface for the 'New' submenu creation.</summary>
        internal IContextMenu newMenuBase = null;
        /// <summary>Extends newMenu to support owner-drawn items and submenus in the 'New' menu.</summary>
        internal IContextMenu2 newMenuExtended = null;
        /// <summary>Extends newMenu2 to support advanced cascading features in the 'New' menu.</summary>
        internal IContextMenu3 newMenuCascading = null;
        internal IntPtr newMenuPtr = IntPtr.Zero;

        private readonly int min = 1;
        private readonly int max = 100000;
        private bool _disposed;

        // Reentrancy guard: TrackPopupMenuEx runs a modal message loop that pumps
        // pending BeginInvoke callbacks. A second ShowMenu queued before the first
        // returned would re-enter while local COM objects (localBase/localExtended/
        // localCascading) and the menu handle are still live, risking premature
        // release and COM apartment corruption.
        private bool m_IsShowingMenu;

        /// <summary>
        /// Displays a shell context menu for the given items and returns the user's selection.
        /// All COM resources are released before this method returns — callers do NOT need to
        /// call <see cref="ReleaseMenu"/> afterward.
        /// </summary>
        /// <param name="hwnd">The handle to the control to host the ContextMenu</param>
        /// <param name="items">The items for which to show the ContextMenu. These items must be in the same folder.</param>
        /// <param name="pt">The point where the ContextMenu should appear</param>
        /// <param name="allowRename">Set if the ContextMenu should contain the Rename command where appropriate</param>
        /// <param name="minimal">If true, uses CMF.VERBSONLY to filter out most 3rd party extensions</param>
        /// <returns>A <see cref="ContextMenuResult"/> indicating what the user selected.</returns>
        public ContextMenuResult ShowMenu(
            IntPtr hwnd,
            CShellItem[] items,
            Point pt,
            bool allowRename,
            bool minimal = false)
        {
            if (m_IsShowingMenu)
            {
                return new ContextMenuResult { Success = false };
            }
            m_IsShowingMenu = true;
            try
            {
                return ShowMenuCore(hwnd, items, pt, allowRename, minimal);
            }
            finally
            {
                m_IsShowingMenu = false;
            }
        }

        private ContextMenuResult ShowMenuCore(
            IntPtr hwnd,
            CShellItem[] items,
            Point pt,
            bool allowRename,
            bool minimal)
        {
            Debug.Assert(items.Length > 0);

            IntPtr comContextMenu = CreatePopupMenu();
            IShellFolder folder = null;
            IContextMenu localBase = null;
            IContextMenu2 localExtended = null;
            IContextMenu3 localCascading = null;
            IntPtr pIcontext = IntPtr.Zero;

            try
            {
                if (items[0] == ShellController.DesktopCSI)
                {
                    folder = items[0].GetIShellFolder();
                }
                else
                {
                    folder = items[0].Parent.GetIShellFolder();
                }

                IntPtr[] pidls = new IntPtr[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    if (!items[i].CanRename) allowRename = false;
                    pidls[i] = CPidl.ILFindLastID(items[i].PIDL);
                }

                int prgf = 0;
                int HR = folder.GetUIObjectOf(IntPtr.Zero, (uint)pidls.Length, pidls, IID_IContextMenu, prgf, out pIcontext);
                Marshal.ReleaseComObject(folder);
                folder = null;

                if (HR != S_OK)
                {
#if DEBUG
                    Marshal.ThrowExceptionForHR(HR);
#endif
                    return new ContextMenuResult { Success = false };
                }

                localBase = (IContextMenu)Marshal.GetObjectForIUnknown(pIcontext);

                IntPtr p = IntPtr.Zero;

                Marshal.QueryInterface(pIcontext, IID_IContextMenu2, out p);
                if (p != IntPtr.Zero)
                {
                    localExtended = (IContextMenu2)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    p = IntPtr.Zero;
                }

                Marshal.QueryInterface(pIcontext, IID_IContextMenu3, out p);
                if (p != IntPtr.Zero)
                {
                    localCascading = (IContextMenu3)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    p = IntPtr.Zero;
                }

                Marshal.Release(pIcontext);
                pIcontext = IntPtr.Zero;

                int startIndex = GetMenuItemCount(comContextMenu.ToInt32());

                int flags = (int)CMF.NORMAL;
                if (items != null && items.Length > 0) flags |= (int)CMF.ITEMMENU;
                if (allowRename) flags |= (int)CMF.CANRENAME;
                if (minimal) flags |= (int)CMF.NOVERBS;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) flags |= (int)CMF.EXTENDEDVERBS;

                int idCount = localBase.QueryContextMenu(comContextMenu, startIndex, min, max, flags);

                AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);
                uint moveCmdId = (uint)(max + 1);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, moveCmdId, "Move");
                uint copyToFolderCmdId = (uint)(max + 2);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, copyToFolderCmdId, "Copy to Folder");
                uint newFolderCmdId = (uint)(max + 3);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, newFolderCmdId, "New Folder");
                uint refreshCmdId = (uint)(max + 4);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, refreshCmdId, "Refresh");

                int cmd = TrackPopupMenuEx(comContextMenu, (int)TPM.RETURNCMD, pt.X, pt.Y, hwnd, IntPtr.Zero);

                if (cmd == (int)moveCmdId || cmd == (int)copyToFolderCmdId || cmd == (int)newFolderCmdId || cmd == (int)refreshCmdId)
                {
                    return new ContextMenuResult
                    {
                        Success = true,
                        Verb = null,
                        CommandInfo = new CMInvokeCommandInfoEx
                        {
                            cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                            lpVerb = (IntPtr)(cmd == (int)moveCmdId ? 99999 : cmd == (int)copyToFolderCmdId ? 99998 : cmd == (int)newFolderCmdId ? 99997 : 99996),
                            lpVerbW = (IntPtr)(cmd == (int)moveCmdId ? 99999 : cmd == (int)copyToFolderCmdId ? 99998 : cmd == (int)newFolderCmdId ? 99997 : 99996),
                            nShow = (int)SW.SHOWNORMAL,
                            fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                            ptInvoke = new Point(pt.X, pt.Y)
                        }
                    };
                }

                if (cmd >= min && cmd <= idCount)
                {
                    string verb = GetVerbString(localBase, cmd - min);

                    var invokeInfo = new CMInvokeCommandInfoEx
                    {
                        cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                        hwnd = hwnd,
                        lpVerb = (IntPtr)(cmd - min),
                        lpVerbW = (IntPtr)(cmd - min),
                        nShow = (int)SW.SHOWNORMAL,
                        fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                        ptInvoke = new Point(pt.X, pt.Y)
                    };

                    int invokeHr = localBase.InvokeCommand(invokeInfo);
                    if (invokeHr != S_OK)
                        Debug.WriteLine($"ContextMenu.ShowMenu InvokeCommand for '{verb}' failed with HRESULT: {invokeHr:X}");

                    return new ContextMenuResult
                    {
                        Success = true,
                        Verb = verb,
                        CommandInfo = invokeInfo,
                        InvokeHResult = invokeHr
                    };
                }

                return new ContextMenuResult { Success = false };
            }
            finally
            {
                if (localCascading != null) Marshal.ReleaseComObject(localCascading);
                if (localExtended != null) Marshal.ReleaseComObject(localExtended);
                if (localBase != null) Marshal.ReleaseComObject(localBase);
                if (pIcontext != IntPtr.Zero) Marshal.Release(pIcontext);
                if (folder != null) Marshal.ReleaseComObject(folder);
                if (comContextMenu != IntPtr.Zero) DestroyMenu(comContextMenu);
            }
        }

        private static string GetVerbString(IContextMenu contextMenu, int verbId)
        {
            try
            {
                var cmdBytes = new byte[257];
                contextMenu.GetCommandString(verbId, (int)GCS.VERBA, 0, cmdBytes, 256);
                return ShellHelper.SzToString(cmdBytes).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Invokes a specific command from an IContextMenu.
        /// </summary>
        /// <param name="iContextMenu">the IContextMenu containing the item</param>
        /// <param name="cmd">the index of the command to invoke</param>
        /// <param name="parentDir">the parent directory from where to invoke</param>
        /// <param name="ptInvoke">the point (in screen coordinates) from which to invoke</param>
        public static void InvokeCommand(IContextMenu iContextMenu, uint cmd, string parentDir, Point ptInvoke)
        {
            var invoke = new ShellAPI.CMInvokeCommandInfoEx
            {
                cbSize = ShellAPI.CMInvokeCommandInfoEx_size,
                lpVerb = (IntPtr)cmd,
                lpDirectory = parentDir,
                lpVerbW = (IntPtr)cmd,
                lpDirectoryW = parentDir,
                fMask = (int)(ShellAPI.CMIC.UNICODE | ShellAPI.CMIC.PTINVOKE)
            };

            if ((Control.ModifierKeys & Keys.Control) != 0)
                invoke.fMask |= (int)ShellAPI.CMIC.CONTROL_DOWN;

            if ((Control.ModifierKeys & Keys.Shift) != 0)
                invoke.fMask |= (int)ShellAPI.CMIC.SHIFT_DOWN;

            invoke.ptInvoke = new Point(ptInvoke.X, ptInvoke.Y);
            invoke.nShow = (int)ShellAPI.SW.SHOWNORMAL;

            iContextMenu.InvokeCommand(invoke);
        }

        /// <summary>
        /// Creates and initializes a "New" submenu for the given folder.
        /// Returns a <see cref="NewMenuScope"/> that must be disposed by the caller
        /// (typically via a <c>using</c> statement) when the menu is no longer needed.
        /// </summary>
        /// <param name="itm">The folder for which to create the "New" submenu.</param>
        /// <param name="contextMenu">The parent popup menu handle to attach the submenu to.</param>
        /// <param name="index">The position index within the parent menu.</param>
        /// <returns>A <see cref="NewMenuScope"/> that manages the submenu lifetime.</returns>
        public NewMenuScope SetUpNewMenu(CShellItem itm, IntPtr contextMenu, int index)
        {
            int HR;

            newMenuPtr = IntPtr.Zero;
            var CLSID_NewMenu = ShellAPI.CLSID_NewMenu;
            var IID_IContextMenu = ShellAPI.IID_IContextMenu;
            HR = CoCreateInstance(ref CLSID_NewMenu, IntPtr.Zero, CLSCTX.INPROC_SERVER, ref IID_IContextMenu, out newMenuPtr);

            if (HR == S_OK)
            {
                newMenuBase = (IContextMenu)Marshal.GetObjectForIUnknown(newMenuPtr);

                IntPtr p = IntPtr.Zero;
                Marshal.QueryInterface(newMenuPtr, IID_IContextMenu2, out p);
                if (p != IntPtr.Zero)
                {
                    newMenuExtended = (IContextMenu2)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    p = IntPtr.Zero;
                }

                Marshal.QueryInterface(newMenuPtr, IID_IContextMenu3, out p);
                if (p != IntPtr.Zero)
                {
                    newMenuCascading = (IContextMenu3)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    p = IntPtr.Zero;
                }

                IntPtr iShellExtInitPtr;
                HR = Marshal.QueryInterface(newMenuPtr, IID_IShellExtInit, out iShellExtInitPtr);
                if (HR == S_OK)
                {
                    IShellExtInit shellExtInit = (IShellExtInit)Marshal.GetObjectForIUnknown(iShellExtInitPtr);
                    shellExtInit.Initialize(itm.PIDL, IntPtr.Zero, IntPtr.Zero);

                    Marshal.ReleaseComObject(shellExtInit);
                    Marshal.Release(iShellExtInitPtr);
                }

                if (newMenuPtr != IntPtr.Zero)
                {
                    Marshal.Release(newMenuPtr);
                    newMenuPtr = IntPtr.Zero;
                }
            }

            if (HR != S_OK)
            {
                ReleaseNewMenu();
#if DEBUG
                Marshal.ThrowExceptionForHR(HR);
#endif
                return new NewMenuScope(this); // empty scope, safe to dispose
            }

            newMenuBase.QueryContextMenu(contextMenu, index, min, max, (int)CMF.NORMAL);
            newMenuPtr = GetSubMenu(contextMenu, index);

            return new NewMenuScope(this);
        }

        internal void ReleaseNewMenu()
        {
            if (newMenuBase != null)
            {
                Marshal.ReleaseComObject(newMenuBase);
                newMenuBase = null;
            }

            if (newMenuExtended != null)
            {
                Marshal.ReleaseComObject(newMenuExtended);
                newMenuExtended = null;
            }

            if (newMenuCascading != null)
            {
                Marshal.ReleaseComObject(newMenuCascading);
                newMenuCascading = null;
            }

            // CRITICAL: Do NOT release newMenuPtr after GetSubMenu() has been called!
            // GetSubMenu() returns a HMENU (window menu handle), NOT a COM object pointer.
            // Attempting to Marshal.Release() a HMENU causes access violations.
            if (newMenuPtr != IntPtr.Zero)
            {
                newMenuPtr = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (cntxMenuBase != null)
                {
                    Marshal.ReleaseComObject(cntxMenuBase);
                    cntxMenuBase = null;
                }

                if (cntxMenuExtended != null)
                {
                    Marshal.ReleaseComObject(cntxMenuExtended);
                    cntxMenuExtended = null;
                }

                if (cntxMenuCascading != null)
                {
                    Marshal.ReleaseComObject(cntxMenuCascading);
                    cntxMenuCascading = null;
                }

                ReleaseNewMenu();
            }
        }
    }
}
