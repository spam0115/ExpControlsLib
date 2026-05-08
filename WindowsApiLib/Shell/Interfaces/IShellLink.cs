using System;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    // We define the Ansi version since all Win OSs (95 thru XP) support it
    [ComImport()]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214EE-0000-0000-C000-000000000046")]
    public interface IShellLink
    {

        int GetPath([MarshalAs(UnmanagedType.LPStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATA pfd, SLGP fFlags);




        int GetIDList(ref IntPtr ppidl);

        int SetIDList(IntPtr pidl);

        int GetDescription([MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, int cchMaxName);


        int SetDescription([MarshalAs(UnmanagedType.LPStr)] string pszName);

        int GetWorkingDirectory([MarshalAs(UnmanagedType.LPStr)] StringBuilder pszDir, int cchMaxPath);


        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPStr)] string pszDir);

        int GetArguments([MarshalAs(UnmanagedType.LPStr)] StringBuilder pszArgs, int cchMaxPath);


        int SetArguments([MarshalAs(UnmanagedType.LPStr)] string pszArgs);

        int GetHotkey(ref short pwHotkey);

        int SetHotkey(short wHotkey);

        int GetShowCmd(ref int piShowCmd);

        int SetShowCmd(int iShowCmd);

        int GetIconLocation([MarshalAs(UnmanagedType.LPStr)] StringBuilder pszIconPath, int cchIconPath, ref int piIcon);




        int SetIconLocation([MarshalAs(UnmanagedType.LPStr)] string pszIconPath, int iIcon);



        int SetRelativePath([MarshalAs(UnmanagedType.LPStr)] string pszPathRel, int dwReserved);



        int Resolve(IntPtr hwnd, SLR fFlags);


        int SetPath([MarshalAs(UnmanagedType.LPStr)] string pszFile);


    }
}