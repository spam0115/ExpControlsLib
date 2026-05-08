using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(
            SIGDN sigdnName,
            out IntPtr ppszName);

        void GetAttributes(
            uint sfgaoMask,
            out uint psfgaoAttribs);

        void Compare(
            IShellItem psi,
            uint hint,
            out int piOrder);
    }
}
