using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    [ComImport]
    [Guid("DE5BF786-477A-11d2-839D-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDragSourceHelper
    {
        // Initializes the drag-image manager for a windowless control
        [PreserveSig]
        int InitializeFromBitmap(ref ShellAPI.SHDRAGIMAGE pshdi, IntPtr pDataObject);

        // Initializes the drag-image manager for a control with a window
        [PreserveSig]
        int InitializeFromWindow(IntPtr hwnd, ref ShellAPI.POINT ppt, IntPtr pDataObject);
    }
}