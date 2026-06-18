using System;
using System.Runtime.InteropServices;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public interface IShellApiWrapper
    {
        int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne);
        bool SHChangeNotifyDeregister(int hNotify);
        IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent);
        int SHChangeNotification_Unlock(IntPtr hLock);
        int SHGetRealIDL(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal);
        bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }

    public class ShellApiWrapper : IShellApiWrapper
    {
        public int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne)
        {
            return ShellAPI.SHChangeNotifyRegister(hwnd, fSources, fEvents, wMsg, cEntries, pfsne);
        }

        public bool SHChangeNotifyDeregister(int hNotify)
        {
            return ShellAPI.SHChangeNotifyDeregister(hNotify);
        }

        public IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent)
        {
            return ShellAPI.SHChangeNotification_Lock(hChange, dwProcId, ref pppidl, ref plEvent);
        }

        public int SHChangeNotification_Unlock(IntPtr hLock)
        {
            return ShellAPI.SHChangeNotification_Unlock(hLock);
        }

        public int SHGetRealIDL(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal)
        {
            return ShellAPI.SHGetRealIDL(psf, pidlSimple, out ppidlReal);
        }

        public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam)
        {
            return ShellAPI.PostMessage(hWnd, Msg, wParam, lParam);
        }
    }
}
