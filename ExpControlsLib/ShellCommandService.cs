using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib;

/// <summary>
/// Executes shell verbs and context-menu commands on the control's dedicated STA
/// runner. The caller transfers ownership of all cloned PIDLs passed to this class;
/// they are released after the shell call completes.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ShellCommandService
{
    private readonly StaThreadRunner _staRunner;

    public ShellCommandService(StaThreadRunner staRunner)
    {
        _staRunner = staRunner ?? throw new ArgumentNullException(nameof(staRunner));
    }

    public Task<int> InvokeVerbAsync(
        string verb,
        nint capturedParentPidl,
        IReadOnlyList<nint> capturedRelativePidls,
        Point invokePoint = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(verb);
        ArgumentNullException.ThrowIfNull(capturedRelativePidls);

        return _staRunner.EnqueueWork(_ => InvokeVerb(
            verb,
            capturedParentPidl,
            capturedRelativePidls,
            invokePoint));
    }

    public Task<int> InvokeContextMenuAsync(
        nint capturedParentPidl,
        IReadOnlyList<nint> capturedRelativePidls,
        CMInvokeCommandInfoEx commandInfo)
    {
        ArgumentNullException.ThrowIfNull(capturedRelativePidls);

        return _staRunner.EnqueueWork(_ => InvokeContextMenu(
            capturedParentPidl,
            capturedRelativePidls,
            commandInfo));
    }

    private static int InvokeVerb(
        string verb,
        nint capturedParentPidl,
        IReadOnlyList<nint> capturedRelativePidls,
        Point invokePoint)
    {
        IntPtr lpVerbAnsi = IntPtr.Zero;
        IntPtr lpVerbUni = IntPtr.Zero;

        try
        {
            lpVerbAnsi = Marshal.StringToHGlobalAnsi(verb);
            lpVerbUni = Marshal.StringToHGlobalUni(verb);

            var commandInfo = new CMInvokeCommandInfoEx
            {
                cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                nShow = (int)SW.SHOWNORMAL,
                fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                ptInvoke = invokePoint,
                lpVerb = lpVerbAnsi,
                lpVerbW = lpVerbUni
            };

            return InvokeContextMenuCore(
                capturedParentPidl,
                capturedRelativePidls,
                commandInfo,
                verb);
        }
        finally
        {
            if (lpVerbAnsi != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbAnsi);
            if (lpVerbUni != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbUni);
            FreePidls(capturedParentPidl, capturedRelativePidls);
        }
    }

    private static int InvokeContextMenu(
        nint capturedParentPidl,
        IReadOnlyList<nint> capturedRelativePidls,
        CMInvokeCommandInfoEx commandInfo)
    {
        try
        {
            return InvokeContextMenuCore(capturedParentPidl, capturedRelativePidls, commandInfo, "context-menu");
        }
        finally
        {
            FreePidls(capturedParentPidl, capturedRelativePidls);
        }
    }

    private static int InvokeContextMenuCore(
        nint capturedParentPidl,
        IReadOnlyList<nint> capturedRelativePidls,
        CMInvokeCommandInfoEx commandInfo,
        string operation)
    {
        IShellFolder? desktop = null;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        IntPtr iUnknownOut = IntPtr.Zero;

        using (Control dummy = new Control())
        {
            try
            {
                commandInfo.hwnd = dummy.Handle;

                int hr = SHGetDesktopFolder(ref desktop);
                if (hr != S_OK || desktop == null)
                {
                    Debug.WriteLine($"Shell command failed: HRESULT=0x{hr:X8}, operation='{operation}'");
                    return hr;
                }

                if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                {
                    parentFolder = desktop;
                }
                else
                {
                    IntPtr folderPtr = IntPtr.Zero;
                    hr = desktop.BindToObject(
                        capturedParentPidl,
                        IntPtr.Zero,
                        ShellAPI.IID_IShellFolder,
                        ref folderPtr);
                    if (hr != S_OK || folderPtr == IntPtr.Zero)
                    {
                        Debug.WriteLine($"Shell command failed: HRESULT=0x{hr:X8}, operation='{operation}'");
                        return hr;
                    }

                    try
                    {
                        parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(
                            folderPtr,
                            typeof(IShellFolder));
                    }
                    finally
                    {
                        Marshal.Release(folderPtr);
                    }
                }

                hr = parentFolder.GetUIObjectOf(
                    IntPtr.Zero,
                    (uint)capturedRelativePidls.Count,
                    capturedRelativePidls.ToArray(),
                    IID_IContextMenu,
                    IntPtr.Zero,
                    out iUnknownOut);
                if (hr != S_OK || iUnknownOut == IntPtr.Zero)
                {
                    Debug.WriteLine($"Shell command failed: HRESULT=0x{hr:X8}, operation='{operation}'");
                    return hr;
                }

                contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(
                    iUnknownOut,
                    typeof(IContextMenu));

                hr = contextMenu.InvokeCommand(commandInfo);
                if (hr != S_OK && hr != -1)
                {
                    Debug.WriteLine($"Shell command failed: HRESULT=0x{hr:X8}, operation='{operation}'");
                }

                return hr;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in shell command '{operation}': {ex.Message}");
                return -1;
            }
            finally
            {
                if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                if (desktop != null) Marshal.ReleaseComObject(desktop);
            }
        }
    }

    private static void FreePidls(nint parentPidl, IReadOnlyList<nint> relativePidls)
    {
        if (parentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(parentPidl);
        foreach (var pidl in relativePidls)
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }
}
