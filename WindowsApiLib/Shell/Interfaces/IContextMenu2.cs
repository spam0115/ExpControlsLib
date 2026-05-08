using System;
using System.Runtime.InteropServices;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    [ComImport()]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    public interface IContextMenu2
    {
        // IContextMenu methods

        [PreserveSig()]
        int QueryContextMenu(IntPtr hmenu, int iMenu, int idCmdFirst, int idCmdLast, int uFlags);




        [PreserveSig()]
        int InvokeCommand(ref CMInvokeCommandInfoEx pici);

        [PreserveSig()]
        int GetCommandString(int idcmd, int uflags, int reserved, [MarshalAs(UnmanagedType.LPArray)] byte[] commandstring, int cch);




        // IContextMenu2 method
        [PreserveSig()]
        int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);




    }

}