using System;
using System.Collections.Generic;
using System.Text;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    public class MockShellApi : IShellApiWrapper
    {
        public delegate IntPtr LockDelegate(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent);
        public LockDelegate OnLock;

        public delegate int GetRealIDLDelegate(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal);
        public GetRealIDLDelegate OnGetRealIDL;

        public int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne) => 0;
        public bool SHChangeNotifyDeregister(int hNotify) => true;
        public IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent)
            => OnLock?.Invoke(hChange, dwProcId, ref pppidl, ref plEvent) ?? IntPtr.Zero;
        public int SHChangeNotification_Unlock(IntPtr hLock) => 1;

        public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam) => true;

    }

}
