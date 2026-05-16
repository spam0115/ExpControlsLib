using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// The purpose of this class is to maintain and manipulate a collection of CShellItems 
    /// and to do so in a hierachical structure.  The hierachical structure is to enable 
    /// navigation and updating of the shell items which have a hierachical relationship
    /// with each other in the Windows Shell namespace.
    /// </summary>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CShellItemHierachyManager
    {
        public CShellItem Root {  get; set; }
        public CShellItem? CurrentFolder { get; set; }
        public string? CurrentPath { get {
                if (CurrentFolder?.PIDL == null) return string.Empty;
                return CPidl.ToString(CurrentFolder.PIDL);
            } }

        public CShellItemHierachyManager(CShellItem root) {
            this.Root = root;

            //todo: move the item hierarchy code from cshellitem to over here.
        }

    }
}
