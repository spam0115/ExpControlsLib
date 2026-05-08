using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsApiLib.Shell
{
    [ComImport]
    [Guid("4657278B-411B-11d2-839A-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTargetHelper
    {
        // Notifies the drag-image manager that the drop target's IDropTarget::DragEnter method has been called
        [PreserveSig]
        int DragEnter(IntPtr hwndTarget, IntPtr pDataObject, ref ShellAPI.POINT ppt, DragDropEffects dwEffect);

        // Notifies the drag-image manager that the drop target's IDropTarget::DragLeave method has been called
        [PreserveSig]
        int DragLeave();

        // Notifies the drag-image manager that the drop target's IDropTarget::DragOver method has been called
        [PreserveSig]
        int DragOver(ref ShellAPI.POINT ppt, DragDropEffects dwEffect);

        // Notifies the drag-image manager that the drop target's IDropTarget::Drop method has been called
        [PreserveSig]
        int Drop(IntPtr pDataObject, ref ShellAPI.POINT ppt, DragDropEffects dwEffect);

        // Notifies the drag-image manager to show or hide the drag image
        [PreserveSig]
        int Show(bool fShow);
    }
}