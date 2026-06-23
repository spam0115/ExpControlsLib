using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    public class MockShellItemFactory : IShellItemFactoryWrapper
    {
        public static CShellItem CreateMockShellItem(CSIDL csidl, CShellItem? parent = null)
        {
            byte[] pidlBytes = MockPidlFactory.CreateMockPidl(csidl);
            return CreateMockShellItemFromPidlBytes(pidlBytes, parent);
        }

        public static CShellItem CreateMockShellItem(string path, CShellItem? parent = null)
        {
            byte[] pidlBytes = MockPidlFactory.CreateMockPidlFromPath(path);
            return CreateMockShellItemFromPidlBytes(pidlBytes, parent);
        }
        
        public static CShellItem CreateMockShellItemFromPidlBytes(byte[] pidlBytes, CShellItem? parent = null)
        {
            IntPtr pidl = MockPidl.BytesToPidl(pidlBytes);
            string displayPath = MockPidlFactory.GetDisplayPathFromPidl(pidlBytes);
            string displayName = MockPidlFactory.GetDisplayName(pidlBytes);

            var csi = new CShellItem();
            csi.m_Pidl = pidl;
            csi.m_DisplayName = displayName;
            csi.m_FullPath = displayPath;
            csi.Parent = parent;
            csi.m_IsFolder = true;
            csi.m_IsFileSystem = !IsVirtualFolder(pidlBytes);
            csi.m_IsBrowsable = true;
            csi.m_HasSubFolders = true;
            csi.m_IsDisk = IsDrive(pidlBytes);
            csi.m_HasDispType = true;

            if (csi.m_IsDisk)
            {
                csi.m_Length = 500000000000; // mock 500GB disk
            }

            return csi;
        }

        private static bool IsVirtualFolder(byte[] pidlBytes)
        {
            var items = ExtractItems(pidlBytes);
            if (items.Count == 0) return false;

            byte[] firstItem = items[0];
            if (firstItem.Length >= 3)
            {
                return firstItem[2] == 0x1F; // Virtual folder type
            }
            return false;
        }

        private static bool IsDrive(byte[] pidlBytes)
        {
            var items = ExtractItems(pidlBytes);
            if (items.Count == 0) return false;

            byte[] lastItem = items[items.Count - 1];
            if (lastItem.Length >= 3)
            {
                return lastItem[2] == 0x2F; // Drive type
            }
            return false;
        }

        private static List<byte[]> ExtractItems(byte[] pidl)
        {
            var result = new List<byte[]>();
            int offset = 0;

            while (offset + 2 <= pidl.Length)
            {
                ushort cb = (ushort)(pidl[offset] | (pidl[offset + 1] << 8));
                if (cb == 0) break;

                if (offset + cb > pidl.Length) break;

                byte[] item = new byte[cb];
                Buffer.BlockCopy(pidl, offset, item, 0, cb);
                result.Add(item);
                offset += cb;
            }

            return result;
        }

        public static CShellItemHierachyManager CreateMockHierarchyManager()
        {
            var desktop = CreateMockShellItem(CSIDL.DESKTOP);
            desktop.Parent = null;

            var drives = CreateMockShellItem(CSIDL.DRIVES, desktop);
            desktop.Directories = new CShellItemCollection(desktop);
            desktop.DirectoriesList.Append(drives);

            var cDrive = CreateMockShellItem(CSIDL.C_DRIVE, drives);
            drives.Directories = new CShellItemCollection(drives);
            drives.DirectoriesList.Append(cDrive);

            var windows = CreateMockShellItem(CSIDL.WINDOWS, cDrive);
            cDrive.Directories = new CShellItemCollection(cDrive);
            cDrive.DirectoriesList.Append(windows);

            var notepad = CreateMockShellItem("C:\\Windows\\notepad.exe", windows);
            windows.Files = new CShellItemCollection(windows);
            windows.FilesList?.Add(notepad);

            var system = CreateMockShellItem(CSIDL.SYSTEM, windows);
            windows.Directories = new CShellItemCollection(windows);
            windows.DirectoriesList.Append(system);

            var programFiles = CreateMockShellItem(CSIDL.PROGRAM_FILES, cDrive);
            cDrive.DirectoriesList.Append(programFiles);

            var programFilesX86 = CreateMockShellItem(CSIDL.PROGRAM_FILESX86, cDrive);
            cDrive.DirectoriesList.Append(programFilesX86);

            var profile = CreateMockShellItem(CSIDL.PROFILE, cDrive);
            cDrive.DirectoriesList.Append(profile);

            var desktopDirectory = CreateMockShellItem(CSIDL.DESKTOPDIRECTORY, profile);
            profile.Directories = new CShellItemCollection(profile);
            profile.DirectoriesList.Append(desktopDirectory);

            var localAppData = CreateMockShellItem(CSIDL.LOCAL_APPDATA, profile);
            profile.DirectoriesList.Append(localAppData);

            var myDocuments = CreateMockShellItem(CSIDL.MYDOCUMENTS, profile);
            profile.DirectoriesList.Append(myDocuments);

            var myPictures = CreateMockShellItem(CSIDL.MYPICTURES, profile);
            profile.DirectoriesList.Append(myPictures);

            return new CShellItemHierachyManager(desktop);
        }

        public List<IntPtr> Pidls = new List<IntPtr>();

        public List<nint> GetPidlsOfFolder(CShellItem csi, SHCONTF flags)
        {
            return Pidls;
        }

        public CShellItem Create(nint pidl, CShellItem? parent = null)
        {
            var csi = new CShellItem();
            csi.m_Pidl = MockPidl.Clone(pidl);
            csi.Parent = parent;
            return csi;
        }

        public CShellItem FindOrAdd(nint pidl)
        {
            return Create(pidl);
        }

        public string GetFullPath(CShellItem csi)
        {
            return "C:\\MockPath\\" + csi.DisplayName;
        }
    }
}
