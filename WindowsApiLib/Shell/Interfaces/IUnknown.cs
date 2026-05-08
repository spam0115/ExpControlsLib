using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{

    // Not needed in .Net - use Marshal Class
    [ComImport()]
    [Guid("00000000-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUnknown
    {
        [PreserveSig()]
        int QueryInterface(ref Guid riid, ref IntPtr pVoid);
        [PreserveSig()]
        uint AddRef();
        [PreserveSig()]
        uint Release();
    }
}