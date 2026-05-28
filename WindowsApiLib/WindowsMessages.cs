using System;
using System.Collections.Generic;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{
    public sealed class WindowsMessages
    {
        public const int WM_QUERYENDSESSION = 0x0011;
        public const int WM_ENDSESSION = 0x0016;
        public const int WM_CLOSE = 0x0010;
        public const int WM_NCDESTORY = 0x0082;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_HSCROLL = 0x0114;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_DESTROY_THREAD_WINDOW = (int)WM.USER + 500;
    }
}
