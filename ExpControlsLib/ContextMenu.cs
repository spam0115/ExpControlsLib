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
    /// WindowsContextMenu provides the infrastucture for displaying a Windows Context Menu on a Control
    /// and for Invoking a Command selected by the user from that Context Menu. Cascaded sub-menus are created,
    /// displayed, and responded to as required.
    /// The Context Menu applies to all CShItems
    /// passed to the ShowMenu Function and all CShItems must be in the same Folder. 
    /// </summary>
    /// <remarks>Though specifically designed for ListView and TreeView Controls, this Class will work for any Control which
    ///          is associated with a single Folder and can provide CShItems from that Folder.</remarks>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ContextMenu
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        /// <summary>Base interface for basic context menu commands (Open, Copy, etc.).</summary>
        public IContextMenu cntxMenuBase = null;
        /// <summary>Extends IContextMenu to support owner-drawn items and submenus (HandleMenuMsg).</summary>
        public IContextMenu2 cntxMenuExtended = null;
        /// <summary>Extends IContextMenu2 to support advanced cascading menus with result return (HandleMenuMsg2).</summary>
        public IContextMenu3 cntxMenuCascading = null;

        /// <summary>Base interface for the 'New' submenu creation.</summary>
        public IContextMenu newMenuBase = null;
        /// <summary>Extends newMenu to support owner-drawn items and submenus in the 'New' menu.</summary>
        public IContextMenu2 newMenuExtended = null;
        /// <summary>Extends newMenu2 to support advanced cascading features in the 'New' menu.</summary>
        public IContextMenu3 newMenuCascading = null;
        public IntPtr newMenuPtr = IntPtr.Zero;

        private readonly int min = 1;
        private readonly int max = 100000;

        /// <summary>
        /// If this method returns true then the caller must call ReleaseMenu
        /// </summary>
        /// <param name="hwnd">The handle to the control to host the ContextMenu</param>
        /// <param name="items">The items for which to show the ContextMenu. These items must be in the same folder.</param>
        /// <param name="pt">The point where the ContextMenu should appear</param>
        /// <param name="allowrename">Set if the ContextMenu should contain the Rename command where appropriate</param>
        /// <param name="cmi">The command information for the users selection</param>
        /// <param name="minimal">If true, uses CMF.VERBSONLY to filter out most 3rd party extensions</param>
        /// <returns></returns>
        /// <remarks></remarks>
        public bool ShowMenu(
            IntPtr hwnd,
            CShellItem[] items,
            Point pt,
            bool allowRename,
            [Out] out CMInvokeCommandInfoEx cmi,
            bool minimal = false)
        {
            cmi = default;
            Debug.Assert(items.Length > 0);

            IntPtr comContextMenu = CreatePopupMenu();
            IntPtr[] pidls = new IntPtr[items.Length];
            IShellFolder folder = null;

            if (items[0] == ShellController.DesktopCSI)
            {
                folder = items[0].GetIShellFolder();
            }
            else
            {
                folder = items[0].Parent.GetIShellFolder();
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (!items[i].CanRename) allowRename = false;
                pidls[i] = CPidl.ILFindLastID(items[i].PIDL);
            }

            int prgf = 0;
            IntPtr pIcontext = IntPtr.Zero;
            int HR = folder.GetUIObjectOf(IntPtr.Zero, (uint)pidls.Length, pidls, IID_IContextMenu, prgf, out pIcontext);
            Marshal.ReleaseComObject(folder);
            folder = null;

            if (HR != S_OK)
            {
#if DEBUG
                Marshal.ThrowExceptionForHR(HR);
#endif
                goto FAIL;
            }

            cntxMenuBase = (IContextMenu)Marshal.GetObjectForIUnknown(pIcontext);

            IntPtr p = IntPtr.Zero;

            // Depending on how IID_IContextMenu2/3 are defined, you may need local Guid vars with ref.
            Marshal.QueryInterface(pIcontext, IID_IContextMenu2, out p);
            if (p != IntPtr.Zero)
            {
                cntxMenuExtended = (IContextMenu2)Marshal.GetObjectForIUnknown(p);
                Marshal.Release(p);
                p = IntPtr.Zero;
            }

            Marshal.QueryInterface(pIcontext, IID_IContextMenu3, out p);
            if (p != IntPtr.Zero)
            {
                cntxMenuCascading = (IContextMenu3)Marshal.GetObjectForIUnknown(p);
                Marshal.Release(p);
                p = IntPtr.Zero;
            }

            Marshal.Release(pIcontext);
            pIcontext = IntPtr.Zero;

            // Check item count - should always be 0 but check just in case
            int startIndex = GetMenuItemCount(comContextMenu.ToInt32());

            // Fill the context menu
            int flags = (int)CMF.NORMAL;
            if (items != null && items.Length > 0) flags |= (int)CMF.ITEMMENU;
            if (allowRename) flags |= (int)CMF.CANRENAME;
            if (minimal) flags |= (int)CMF.NOVERBS; //.VERBSONLY;
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) flags |= (int)CMF.EXTENDEDVERBS;

            int idCount = cntxMenuBase.QueryContextMenu(comContextMenu, startIndex, min, max, flags);

            // Append separator and custom "Move" and "Copy to Folder" menu items
            AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);
            uint moveCmdId = (uint)(max + 1);
            AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, moveCmdId, "Move");
            uint copyToFolderCmdId = (uint)(max + 2);
            AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, copyToFolderCmdId, "Copy to Folder");

            int cmd = TrackPopupMenuEx(comContextMenu, (int)TPM.RETURNCMD, pt.X, pt.Y, hwnd, IntPtr.Zero);

            if (cmd == (int)moveCmdId || cmd == (int)copyToFolderCmdId)
            {
                cmi = new CMInvokeCommandInfoEx
                {
                    cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                    lpVerb = (IntPtr)(cmd == (int)moveCmdId ? 99999 : 99998),
                    lpVerbW = (IntPtr)(cmd == (int)moveCmdId ? 99999 : 99998),
                    nShow = (int)SW.SHOWNORMAL,
                    fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                    ptInvoke = new Point(pt.X, pt.Y)
                };

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

                if (comContextMenu != IntPtr.Zero)
                {
                    DestroyMenu(comContextMenu);
                    comContextMenu = IntPtr.Zero;
                }

                return true;
            }

            if (cmd >= min && cmd <= idCount)
            {
                cmi = new CMInvokeCommandInfoEx
                {
                    cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                    lpVerb = (IntPtr)(cmd - min),
                    lpVerbW = (IntPtr)(cmd - min),
                    nShow = (int)SW.SHOWNORMAL,
                    fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                    ptInvoke = new Point(pt.X, pt.Y)
                };

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

                if (comContextMenu != IntPtr.Zero)
                {
                    DestroyMenu(comContextMenu);
                    comContextMenu = IntPtr.Zero;
                }

                return true;
            }

        FAIL:
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

            if (comContextMenu != IntPtr.Zero)
            {
                DestroyMenu(comContextMenu);
                comContextMenu = IntPtr.Zero;
            }

            return false;
        }

        public void ReleaseMenu()
        {
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
        }

        /// <summary>
        /// Invokes a specific command from an IContextMenu
        /// </summary>
	     /// <param name="iContextMenu">the IContextMenu containing the item</param>
	     /// <param name="cmd">the index of the command to invoke</param>
	     /// <param name="parentDir">the parent directory from where to invoke</param>
	     /// <param name="ptInvoke">the point (in screen co�rdinates) from which to invoke</param>
        public void InvokeCommand(IContextMenu iContextMenu, uint cmd, string parentDir, Point ptInvoke)
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
        /// If this method returns true then the caller must call ReleaseNewMenu
        /// </summary>
        /// <param name="itm"></param>
        /// <param name="contextMenu"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        public bool SetUpNewMenu(CShellItem itm, IntPtr contextMenu, int index)
        {
            int HR;
            int idCount;

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
                return false;
            }

            idCount = newMenuBase.QueryContextMenu(contextMenu, index, min, max, (int)CMF.NORMAL);
            newMenuPtr = GetSubMenu(contextMenu, index);
            //Marshal.Release(newMenuPtr);

            return true;
        }

        public void ReleaseNewMenu()
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
            // The HMENU is managed by the parent menu and should NOT be manually released.
            // newMenuPtr should only be released if it was never used with GetSubMenu().
            // In the current flow, newMenuPtr is always used with GetSubMenu(), so we don't release it.
            // The COM object reference (newMenu) handles the cleanup via ReleaseComObject above.
            // Simply clear the reference without releasing the HMENU.
            if (newMenuPtr != IntPtr.Zero)
            {
                newMenuPtr = IntPtr.Zero;
            }
        }
    }
}