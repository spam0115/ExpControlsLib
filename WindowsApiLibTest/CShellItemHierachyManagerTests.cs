using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ExpControlsLib;

using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemHierachyManagerTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;


        [TestMethod]
        public void TestRootEqualsDesktop()
        {
            Assert.IsTrue(object.ReferenceEquals(ShellController.Instance.HierachyManager.Root, ShellController.Instance.HierachyManager.DesktopCSI),
                "ExpTree.SelectedItem path should match the test path");

        }

            [TestMethod]
        public async Task TestIsAncestorOf()
        {
            await Runner.EnqueueWork(() =>
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string sys32 = Path.Combine(windir, "System32");

                using var csiWin = CShellItemFactory.Create(windir);
                using var csiSys32 = CShellItemFactory.Create(sys32);

                // Test static methods
                Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(csiWin, csiSys32, false), "Windows should be ancestor of System32");
                Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(csiWin, csiSys32, true), "Windows should be immediate parent of System32");
                Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(csiWin, csiWin, false), "Item should be ancestor of itself (fParent=false)");
                Assert.IsFalse(CShellItemHierachyManager.IsAncestorOf(csiWin, csiWin, true), "Item should NOT be parent of itself");

                // Test cross-ancestry
                Assert.IsFalse(CShellItemHierachyManager.IsAncestorOf(csiSys32, csiWin, false), "System32 should not be ancestor of Windows");
            });
        }

        [TestMethod]
        public async Task TestFindExistingItem()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var csiWin = manager.FindAndAllowExpansion(windir);
                
                // Note: In some restricted environments, finding C:\Windows from Desktop might fail.
                // If it fails, we try a more robust path like Temp.
                if (csiWin == null)
                {
                    windir = Path.GetTempPath();
                    csiWin = manager.FindAndAllowExpansion(windir);
                }

                Assert.IsNotNull(csiWin, "Should be able to find or add a known folder (Windows or Temp)");

                // Find by PIDL
                var foundByPidl = manager.Find(csiWin.PIDL);
                Assert.IsNotNull(foundByPidl, "Should find item by PIDL");
                Assert.AreEqual(csiWin.FullPath, foundByPidl.FullPath, "Found item should have same path");

                // Find by path
                var foundByPath = manager.Find(windir);
                Assert.IsNotNull(foundByPath, "Should find item by path");
                Assert.AreEqual(csiWin.FullPath, foundByPath.FullPath, "Found item should have same path");
            });
        }

        [TestMethod]
        public async Task TestFindOrAddNestedNonProtected()
        {
            await Runner.EnqueueWork((Action)(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                string tempBase = Path.Combine(Path.GetTempPath(), "HierarchyTest_" + Guid.NewGuid().ToString("N"));
                string nestedDir = Path.Combine(tempBase, "Level1", "Level2");
                string nestedFile = Path.Combine(nestedDir, "test.txt");

                try
                {
                    Directory.CreateDirectory(nestedDir);
                    File.WriteAllText(nestedFile, "hello");

                    // Add deep path directly
                    var csiNested = manager.FindAndAllowExpansion(nestedFile);
                    Assert.IsNotNull(csiNested, "Should find or add nested file in temp");
                    Assert.AreEqual(nestedFile, csiNested.FullPath, true);

                    // Verify tree structure
                    var csiL2 = csiNested.Parent;
                    Assert.IsNotNull(csiL2, "Level2 should be parent of file");
                    Assert.AreEqual("Level2", (string)csiL2.DisplayName, true);

                    var csiL1 = csiL2.Parent;
                    Assert.IsNotNull(csiL1, "Level1 should be parent of Level2");
                    Assert.AreEqual("Level1", (string)csiL1.DisplayName, true);
                }
                finally
                {
                    if (Directory.Exists(tempBase))
                        Directory.Delete(tempBase, true);
                }
            }));
        }

        [TestMethod]
        public async Task TestRemoveItem()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                string tempBase = Path.Combine(Path.GetTempPath(), "HierarchyRemoveTest_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(tempBase);
                    var csiTemp = manager.FindAndAllowExpansion(tempBase);
                    Assert.IsNotNull(csiTemp);

                    // Verify it exists
                    Assert.IsNotNull(manager.Find(tempBase));

                    // Remove it
                    bool removed = manager.Remove(csiTemp);
                    Assert.IsTrue(removed, "Remove should return true for existing item");

                    // Verify it's gone
                    Assert.IsNull(manager.Find(tempBase), "Temp folder should no longer be found after removal");
                }
                finally
                {
                    if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
                }
            });
        }

        [TestMethod]
        public async Task TestRemoveRange()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                string tempBase = Path.Combine(Path.GetTempPath(), "HierarchyRangeTest_" + Guid.NewGuid().ToString("N"));
                string file1 = Path.Combine(tempBase, "file1.txt");
                string file2 = Path.Combine(tempBase, "file2.txt");

                try
                {
                    Directory.CreateDirectory(tempBase);
                    File.WriteAllText(file1, "1");
                    File.WriteAllText(file2, "2");

                    var csi1 = manager.FindAndAllowExpansion(file1);
                    var csi2 = manager.FindAndAllowExpansion(file2);

                    Assert.IsNotNull(manager.Find(file1));
                    Assert.IsNotNull(manager.Find(file2));

                    // Remove both
                    bool removed = manager.RemoveRange(new[] { csi1!, csi2! });
                    Assert.IsTrue(removed);

                    Assert.IsNull(manager.Find(file1));
                    Assert.IsNull(manager.Find(file2));
                }
                finally
                {
                    if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
                }
            });
        }

        [TestMethod]
        public async Task TestFindOrAddInvalidPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                string invalidPath = @"C:\ThisPathDoesNotExist_123456789";
                var result = manager.FindAndAllowExpansion(invalidPath);

                Assert.IsNull(result, "Should return null for non-existent path");
            });
        }

        [TestMethod]
        public async Task TestMockHierarchy()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                Assert.IsNotNull(manager, "MockHierarchyManager should be created");
                Assert.IsNotNull(manager.Root, "Root (Desktop) should exist");
                Assert.AreEqual("Desktop", manager.Root.DisplayName, "Root should be Desktop");

                var drives = manager.Root.Directories;
                Assert.IsNotNull(drives, "Desktop should have child directories");
                Assert.IsTrue(drives.Length > 0, "Desktop should have at least one child (DRIVES)");

                var myComputer = drives.FirstOrDefault(d => d.DisplayName.Contains("My Computer"));
                Assert.IsNotNull(myComputer, "My Computer (DRIVES) should exist under Desktop");

                var cDrive = myComputer?.Directories?.FirstOrDefault(d => d.DisplayName == "C:\\");
                Assert.IsNotNull(cDrive, "C: drive should exist under My Computer");
                Assert.IsTrue(cDrive?.IsDisk ?? false, "C: should be marked as a disk");
            });
        }
    }
}
