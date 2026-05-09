using System.Runtime.InteropServices;
using WindowsApiLib.Shell;

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

        // It is also useful to know if the OS is XP or above.  
        public static readonly bool XPorAbove = ShellAPI.IsXpOrAbove();
        // Likewise if OS is Win2K or Above
        public static readonly bool Win2KOrAbove = ShellAPI.Is2KOrAbove();
        // Likewise if OS is Vista or Above
        public static readonly bool VistaOrAbove = ShellAPI.IsVistaOrAbove();

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


        public const int MAX_NAME = 255;
        public const int MAX_PATH = 32767; //new nt limit

        // Thread-safe pool for CoTaskMem allocations to reduce allocation overhead
        //internal static readonly CoTaskMemPool s_memPool_MaxPath = new CoTaskMemPool(MAX_PATH * 2 + 4);
        internal static readonly CoTaskMemPool s_memPool_MaxName = new CoTaskMemPool(MAX_NAME * 2 + 4);


    }


}
