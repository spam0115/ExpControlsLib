using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsApiLib.Shell
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000122-0000-0000-C000-000000000046")]
    public interface IDropTarget
    {
        // Determines whether a drop can be accepted and its effect if it is accepted
        [PreserveSig]
        int DragEnter(IntPtr pDataObj, ShellAPI.MK grfKeyState, ShellAPI.POINT pt, ref DragDropEffects pdwEffect);

        // Provides target feedback to the user through the DoDragDrop function
        [PreserveSig]
        int DragOver(ShellAPI.MK grfKeyState, ShellAPI.POINT pt, ref DragDropEffects pdwEffect);

        // Causes the drop target to suspend its feedback actions
        [PreserveSig]
        int DragLeave();

        /// <summary>
        /// Drops the data into the target window.
        /// For same-volume moves (file moved within the same drive/volume), the shell performs an 
        /// optimized move: it renames the file (a fast directory-entry update) rather than copy+delete. 
        /// In this case DragDrop returns DROPEFFECT_NONE (= DragDropEffects.None) 
        /// </summary>
        /// <param name="pDataObj"></param>
        /// <param name="grfKeyState"></param>
        /// <param name="pt"></param>
        /// <param name="pdwEffect"></param>
        /// <returns></returns>
        [PreserveSig]
        int DragDrop(IntPtr pDataObj, ShellAPI.MK grfKeyState, ShellAPI.POINT pt, ref DragDropEffects pdwEffect);
    }

}