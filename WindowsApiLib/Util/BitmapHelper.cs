using System.Runtime.InteropServices;


namespace WindowsApiLib
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>
    /// Converts an HBITMAP returned by the shell into a managed
    /// <see cref="Bitmap"/> while preserving its 32-bit alpha channel.
    /// </summary>
    /// <param name="hbm">Handle to the GDI bitmap to convert. Ownership is not transferred.</param>
    /// <returns>A managed top-down 32bpp ARGB bitmap, or <c>null</c> if the conversion fails.</returns>
    public static class BitmapHelper
    {
        /// <summary>
        /// Converts an HBITMAP returned by the shell into a managed
        /// <see cref="Bitmap"/> while preserving its 32-bit alpha channel.
        /// </summary>
        /// <param name="hbm">Handle to the GDI bitmap to convert. Ownership is not transferred.</param>
        /// <returns>A managed top-down 32bpp ARGB bitmap, or <c>null</c> if the conversion fails.</returns>

        public static Bitmap? HBitmapToBitmapWithAlpha(IntPtr hbm)
        {
            var info = new BITMAP();
            if (WinSDK.GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref info) == 0) return null;
            int w = info.bmWidth, h = info.bmHeight;

            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,            // negative => top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,        // BI_RGB
            };

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            IntPtr hdc = WinSDK.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                WinSDK.GetDIBits(hdc, hbm, 0, (uint)h, data.Scan0, ref bi, 0 /*DIB_RGB_COLORS*/);
            }
            finally
            {
                WinSDK.DeleteDC(hdc);
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }



}