using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;

namespace WindowsApiLib.Shell
{
    public class CShellItemFactory
    {
        public static CShellItemFactory Instance { get; } = new CShellItemFactory();


        // The DesktopBase is set up via Sub New() (one time only) and
        // disposed of only when DesktopBase is finally disposed of
        private static CShellItem? DesktopCSI = null;


        /// <summary>
        /// Contains a String with the Local representation of "My Computer"
        /// </summary>
        public static string? StrMyComputer { get; private set; }
        /// <summary>
        /// Contains a String with the Local representation of "System Folder".
        /// </summary>
        public static string? StrSystemFolder { get; private set; }

        // To get My Documents sorted first, we need to know the Locale 
        // specific name of that folder.
        public static string? StrMyDocuments { get; private set; }

        /// <summary>
        /// Contains a String with the Full Path of the Desktop Directory
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        public static string? DesktopDirectoryPath { get; private set; }

        /// <summary>
        /// Contains the IShellFolder Interface of the instance if it is a Folder.
        /// </summary>
        /// <returns>The IShellFolder Interface of the instance if it is a Folder</returns>
        public IShellFolder DeskTop { get; private set; }
        public string SystemName { get; }

        private readonly object m_DeskTopDirectory;
        private readonly object m_Recycle;

        private CShellItemFactory() {
            if (DesktopCSI == null)
            {
                int HR;
                // firstly determine what the local machine calls a "System Folder" and "My Computer"
                IntPtr tmpPidl = IntPtr.Zero;
                HR = SHGetSpecialFolderLocation(0, (int)CSIDL.DRIVES, ref tmpPidl);
                var shfi = new SHFILEINFO();
                var dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
                int dwAttr = 0;
                SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
                StrSystemFolder = shfi.szTypeName;
                StrMyComputer = shfi.szDisplayName;
                Marshal.FreeCoTaskMem(tmpPidl);

                // With That done, now set up Desktop CShellItem
                IShellFolder m_Folder = null;
                HR = SHGetDesktopFolder(ref m_Folder);
                DeskTop = m_Folder;
                var m_Pidl = Marshal.AllocCoTaskMem(2);
                Marshal.WriteInt16(m_Pidl, 0, 0);

                //dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.SYSICONINDEX | SHGFI.PIDL;
                //dwAttr = 0;
                //var desktop = SHGetFileInfo(m_Pidl, dwAttr, ref shfi, cbFileInfo, dwflag);
                var csi = new CShellItem(m_Pidl);
                DesktopCSI = csi;


                // also get local name for "My Documents"
                var pchEaten = default(int);
                tmpPidl = IntPtr.Zero;
                int argpdwAttributes = default;
                HR = DeskTop.ParseDisplayName(default, default, "::{" + ShellNamespaceGuids.Documents.ToString() + "}", ref pchEaten, ref tmpPidl, ref argpdwAttributes);
                shfi = new SHFILEINFO();
                dwflag = SHGFI.DISPLAYNAME | SHGFI.TYPENAME | SHGFI.PIDL;
                dwAttr = 0;

                SHGetFileInfo(tmpPidl, dwAttr, ref shfi, cbFileInfo, dwflag);
                StrMyDocuments = shfi.szDisplayName;
                Marshal.FreeCoTaskMem(tmpPidl);

                // Get the SystemName for Remote item testing
                SystemName = Environment.MachineName; 
            }


        }


        /// <summary>
        /// GetFolder returns the IShellFolder interface of the Folder designated by the input Parent and 
        /// relative PIDL.
        /// </summary>
        /// <param name="parent">The CShellItem of the Folder containing the folder for which the 
        /// IShellFolder interface is desired.</param>
        /// <param name="relPidl">The relative Pidl of the folder for which the interface is desired.</param>
        /// <returns>The desired interface or Nothing if error.</returns>
        /// <remarks></remarks>
        public static IShellFolder GetFolder(CShellItem parent, IntPtr relPidl)
        {
            IntPtr ptr = IntPtr.Zero;
            IShellFolder rVal = null;
            int HR = parent.Folder.BindToObject(relPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref ptr);
            if (HR >= S_OK && ptr != IntPtr.Zero)   // New code (12/12/09)
            {
                // The ASUS fix is slightly modified from its' original as per a suggestion from Calum 4/8/2010
                try                                                     // ASUS Fix
                {
                    rVal = (IShellFolder)Marshal.GetTypedObjectForIUnknown(ptr, typeof(IShellFolder));
                }
                catch (Exception ex)                                   // ASUS Fix - modified 11/13/2013 - was InvalidCastException
                {
#if DEBUG
                    Debug.WriteLine("GetFolder: " + ex.Message);         // ASUS Fix
                    throw;                                            // ASUS Fix
#endif
                }
                finally
                {
                    Marshal.Release(ptr); // Must do this in all cases
                }                                                 // ASUS Fix
            }
            else
            {
                if (ptr != IntPtr.Zero)
                    Marshal.Release(ptr); // Added Code (12/12/09)
#if DEBUG
                CPidl.DumpPidl(relPidl);
                Marshal.ThrowExceptionForHR(HR);
#endif
            }    // Removed 10/22/2011 - restored 11/13/2013
            return rVal;
        }

    }
}
