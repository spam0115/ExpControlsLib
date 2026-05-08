using System.Runtime.InteropServices;

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

    }


}
