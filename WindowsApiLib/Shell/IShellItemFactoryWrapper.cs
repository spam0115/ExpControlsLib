using System;
using System.Collections.Generic;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public interface IShellItemFactoryWrapper
    {
        List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags);
        CShellItem Create(IntPtr pidl, CShellItem parent = null);
        CShellItem FindOrAdd(IntPtr pidl);
        string GetFullPath(CShellItem csi);
    }

    public class ShellItemFactoryWrapper : IShellItemFactoryWrapper
    {
        public List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags)
        {
            return CShellItemFactory.GetPidlsOfFolder(csi, flags);
        }

        public CShellItem Create(IntPtr pidl, CShellItem parent = null)
        {
            return CShellItemFactory.Create(pidl, parent);
        }

        public CShellItem FindOrAdd(IntPtr pidl)
        {
            return CShellItemFactory.FindOrAdd(pidl);
        }

        public string GetFullPath(CShellItem csi)
        {
            return CShellItemFactory.GetFullPath(csi);
        }
    }
}
