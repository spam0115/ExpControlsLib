using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    [ComImport()]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    public interface IPersistFile
    {

        // Inheirited from Ipersist
        void GetClassID(out Guid pClassID);

        // IPersistFile Interfaces
        [PreserveSig()]
        int IsDirty();

        int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);


        int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);


        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);

    }
}