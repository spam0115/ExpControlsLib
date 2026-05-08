using System.Runtime.InteropServices;

namespace WindowsApiLib
{
    public class Kernal32
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllocConsole();
    }
}
