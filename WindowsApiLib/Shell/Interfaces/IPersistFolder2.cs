using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsApiLib.Shell.Interfaces
{
    [ComImport]
    [Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFolder2
    {
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        [PreserveSig]
        int Initialize(IntPtr pidl);

        [PreserveSig]
        int GetCurFolder(out IntPtr ppidl); // PIDLIST_ABSOLUTE
    }
}
