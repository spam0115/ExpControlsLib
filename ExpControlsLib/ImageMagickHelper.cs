using ImageMagick;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WindowsApiLib;

namespace ExpControlsLib
{
    /// <summary>
    /// Converts an HBITMAP returned by the shell into a MagickImage
    /// while preserving its 32-bit alpha channel.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class ImageMagickHelper
    {
        /// <summary>
        /// Converts an HBITMAP returned by the shell into a MagickImage
        /// while preserving its 32-bit alpha channel.
        /// </summary>
        /// <param name="hbm">Handle to the GDI bitmap to convert. Ownership is not transferred.</param>
        /// <returns>A MagickImage, or <c>null</c> if the conversion fails.</returns>
        public static MagickImage? HBitmapToMagickImage(IntPtr hbm)
        {
            if (hbm == IntPtr.Zero) return null;

            var bmpInfo = new BITMAP();
            if (WinSDK.GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmpInfo) == 0)
            {
                // Log or handle error if needed
                return null;
            }

            int w = bmpInfo.bmWidth, h = bmpInfo.bmHeight;

            if (bmpInfo.bmBitsPixel != 32)
            {
                // Optionally convert or handle non-32bpp images
                return null;
            }

            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            int byteCount = w * h * 4;
            IntPtr buffer = Marshal.AllocHGlobal(byteCount);
            IntPtr hdc = WinSDK.CreateCompatibleDC(IntPtr.Zero);

            try
            {
                int lines = WinSDK.GetDIBits(hdc, hbm, 0, (uint)h, buffer, ref bi, 0);
                if (lines == 0)
                {
                    // Log or handle error if needed
                    return null;
                }

                byte[] bytes = new byte[byteCount];
                Marshal.Copy(buffer, bytes, 0, byteCount);

                var settings = new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.BGRA);

                var image = new MagickImage();
                image.ReadPixels(bytes, settings);
                return image;
            }
            finally
            {
                WinSDK.DeleteDC(hdc);
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
