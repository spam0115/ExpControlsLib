using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WindowsApiLib.Util;
using ExpControlsLib;
using System.IO;
using System.Linq;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemUpdateLogicTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        private class MockShellApi : IShellApiWrapper
        {
            public int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne) => 0;
            public bool SHChangeNotifyDeregister(int hNotify) => true;
            public IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent) => IntPtr.Zero;
            public int SHChangeNotification_Unlock(IntPtr hLock) => 1;
            public int SHGetRealIDL(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal)
            {
                ppidlReal = IntPtr.Zero;
                return 0;
            }
            public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam) => true;
        }

        private class MockFileSystem : IFileSystem
        {
            public List<IFileInfo> Files = new List<IFileInfo>();
            public IEnumerable<IFileInfo> GetFiles(string path) => Files;
        }

        private class MockFileInfo : IFileInfo
        {
            public string Name { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        private class MockShellItemFactory : IShellItemFactoryWrapper
        {
            public List<IntPtr> Pidls = new List<IntPtr>();
            public List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags) => Pidls;
            public CShellItem Create(IntPtr pidl, CShellItem parent = null) => CShellItemFactory.Create(pidl, parent);
            public CShellItem FindOrAdd(IntPtr pidl) => CShellItemFactory.FindOrAdd(pidl);
            public string GetFullPath(CShellItem csi) => CShellItemFactory.GetFullPath(csi);
        }

        [TestMethod]
        public async Task TestRemoveItem()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);
                
                string tempFile = Path.GetTempFileName();
                try
                {
                    var csi = manager.FindOrAdd(tempFile);
                    Assert.IsNotNull(csi);
                    Assert.IsNotNull(manager.Find(tempFile));

                    var logic = new CShellItemUpdateLogic(manager);
                    bool removed = logic.RemoveItem(csi!.Parent, csi);
                    
                    Assert.IsTrue(removed);
                    Assert.IsNull(manager.Find(tempFile), "Item should be removed from hierarchy");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [TestMethod]
        public async Task TestDoUpdateDir_DetectsNewFile()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);
                
                string tempDir = Path.Combine(Path.GetTempPath(), "UpdateDirTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string existingFile = Path.Combine(tempDir, "existing.txt");
                File.WriteAllText(existingFile, "old content");
                
                try
                {
                    var csiFolder = manager.FindOrAdd(tempDir);
                    Assert.IsNotNull(csiFolder, "csiFolder should not be null");
                    
                    // Initialize the folder collections if they are null
                    if (csiFolder.m_Files == null) csiFolder.m_Files = new CShellItemCollection(csiFolder);
                    if (csiFolder.m_Directories == null) csiFolder.m_Directories = new CShellItemCollection(csiFolder);

                    csiFolder.m_Files.Clear();
                    var csiExisting = manager.FindOrAdd(existingFile);
                    Assert.IsNotNull(csiExisting, "csiExisting should not be null");
                    
                    // Now simulate a new file appearing via MockShellItemFactory
                    string newFile = Path.Combine(tempDir, "new.txt");
                    File.WriteAllText(newFile, "new content");
                    IntPtr newPidl = CPidl.PathToPidl(newFile);

                    var mockFactory = new MockShellItemFactory();
                    mockFactory.Pidls.Add(newPidl);
                    // Add existing file pidl too so it's not removed
                    mockFactory.Pidls.Add(csiExisting!.PIDL);

                    var logic = new CShellItemUpdateLogic(manager, null, null, mockFactory);
                    
                    int count = logic.DoUpdateDir(csiFolder);
                    
                    Assert.AreEqual(1, count, "Should have detected 1 change (the new file)");
                    Assert.IsNotNull(manager.Find(newFile), "New file should be in hierarchy");
                }
                finally
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
            });
        }
    }
}
