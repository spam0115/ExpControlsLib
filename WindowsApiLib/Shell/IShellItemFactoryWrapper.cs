using System;
using System.Collections.Generic;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    public interface IShellItemFactoryWrapper
    {
        List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags);
        CShellItem Create(IntPtr pidl, CShellItem parent = null);
        string GetFullPath(CShellItem csi);
    }

    public class ShellItemFactoryWrapper : IShellItemFactoryWrapper
    {
        public List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags)
        {
            return CShellItemFactory.GetChildPidls(csi, flags);
        }

        public CShellItem Create(IntPtr pidl, CShellItem? parent = null)
        {
            return CShellItemFactory.Create(pidl, parent);
        }


        public string GetFullPath(CShellItem csi)
        {
            return CShellItemFactory.GetFullPath(csi);
        }
    }
}
