using System;
using System.Runtime.InteropServices;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    [ComImport()]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    public interface IContextMenu3
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




        // IContextMenu3 method
        [PreserveSig()]
        int HandleMenuMsg2(int uMsg, IntPtr wParam, IntPtr lParam, IntPtr plResult);




    }

}