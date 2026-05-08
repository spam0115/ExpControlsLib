using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// COM interface for getting shell item images/thumbnails
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    public interface IShellItemImageFactory
    {
        /// <summary>
        /// Gets an image for a shell item
        /// </summary>
        /// <param name="size">pixel size</param>
        /// <param name="flags">Union of the SIIGBF_* flags</param>
        /// <param name="phbm">Pointer to receive the bitmap handle</param>
        /// <returns>HRESULT</returns>
        //[PreserveSig]
        //int GetImage(
        //    int size,
        //    int flags,
        //    out IntPtr phbm
        //);
        [PreserveSig]
        int GetImage(
            SIZE size,
            uint flags,
            out IntPtr phbm
        );
    }

}
