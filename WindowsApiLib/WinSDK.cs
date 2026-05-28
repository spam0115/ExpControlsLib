using System.Runtime.InteropServices;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    public static class WinSDK
    {
        public const int COINIT_APARTMENTTHREADED = 0;

        public static bool SUCCEEDED(int hr)
        { return (hr > 0); }

        // if OS is Vista or Above
        //public static readonly bool VistaOrAbove = ShellAPI.IsVistaOrAbove();

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern void CoUninitialize();

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan,
            uint cScanLines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr ptr);


        public const int MAX_PATH = 260; // must be 260 because of struct size expectations by Windows
        public const int MAX_PATH_NT = 32767; // new nt limit but requires registry edit

        // Thread-safe pool for CoTaskMem allocations to reduce allocation overhead
        //internal static readonly CoTaskMemPool s_memPool_MaxPath = new CoTaskMemPool(MAX_PATH * 2 + 4);
        internal static readonly CoTaskMemPool s_memPool_MaxName = new CoTaskMemPool(MAX_PATH_NT * 2 + 4);



    }


}
