using System;
using System.Runtime.InteropServices;
using WindowsApiLib;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib;

/// <summary>Owns the native context-menu instance and forwards owner-draw window messages.</summary>
internal sealed class ExpTreeContextMenu : IDisposable
{
    public ContextMenu Menu { get; } = new();

    public bool HandleMessage(int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message == (int)WM.INITMENUPOPUP || message == (int)WM.MEASUREITEM || message == (int)WM.DRAWITEM)
        {
            return Menu.cntxMenuExtended is not null && Menu.cntxMenuExtended.HandleMenuMsg(message, wParam, lParam) == 0;
        }

        if (message == (int)WM.MENUCHAR && Menu.cntxMenuCascading is not null)
        {
            var memory = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                if (Menu.cntxMenuCascading.HandleMenuMsg2(message, wParam, lParam, memory) == 0)
                {
                    result = Marshal.ReadIntPtr(memory);
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        return false;
    }

    public void Dispose() => Menu.Dispose();
}
