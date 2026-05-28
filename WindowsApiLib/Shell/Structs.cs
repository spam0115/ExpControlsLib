using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    public struct ListViewSubitemData
    {
        public readonly static ListViewSubitemData Default = new ListViewSubitemData(null, null);
        public string Text;
        public object Tag;

        public ListViewSubitemData(string text, object? tag)
        {
            Text = text;
            Tag = tag;
        }
    }

}
