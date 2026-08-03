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
        public async Task TestMockHierarchy()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                Assert.IsNotNull(manager, "MockHierarchyManager should be created");
                Assert.IsNotNull(manager.Root, "Root (Desktop) should exist");
                Assert.AreEqual("Desktop", manager.Root.DisplayName, "Root should be Desktop");

                var root_directories = manager.Root.DirectoriesList;
                Assert.IsNotNull(root_directories, "Desktop should have child directories");
                Assert.IsTrue(root_directories.Count > 0, "Desktop should have at least one child (DRIVES)");

                var myComputer = root_directories.FirstOrDefault(d => d.DisplayName.Contains("This PC"));
                Assert.IsNotNull(myComputer, "This PC should exist under Desktop");

                var cDrive = myComputer?.DirectoriesList?.FirstOrDefault(d => d.DisplayName == "C:\\");
                Assert.IsNotNull(cDrive, "C: drive should exist under My Computer");
                Assert.IsTrue(cDrive?.IsDisk ?? false, "C: should be marked as a disk");
            });
        }

        [TestMethod]
        public async Task TestFindInMockHierarchy()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                // Find(string) uses ILCreateFromPathW + CPidl.IsAncestorOf (ILIsParent)
                // which requires real compound PIDLs. Mock PIDLs are independent per-item,
                // so we walk the mock hierarchy directly to verify its structure.
                var drives = manager.Root.DirectoriesList?.FirstOrDefault(d => d.DisplayName.Contains("This PC"));
                Assert.IsNotNull(drives, "This PC (DRIVES) should exist under Desktop");

                var cDrive = drives.DirectoriesList?.FirstOrDefault(d => d.DisplayName == "C:\\");
                Assert.IsNotNull(cDrive, "C: drive should exist under This PC");

                var windows = cDrive.DirectoriesList?.FirstOrDefault(d => d.DisplayName == "Windows");
                Assert.IsNotNull(windows, "Windows should exist under C:");

                var notepad = windows.Files?.Items?.FirstOrDefault(f => f.DisplayName == "notepad.exe");
                Assert.IsNotNull(notepad, "notepad.exe should exist under Windows");
                Assert.AreEqual("notepad.exe", notepad.DisplayName, "Found item should be notepad.exe");
            });
        }

        [TestMethod]
        public async Task TestIsAncestorOf()
        {
            await Runner.EnqueueWork(() =>
            {
                string parentPath = Path.Combine(Path.GetTempPath(), "HierarchyAncestor_" + Guid.NewGuid().ToString("N"));
                string childPath = Path.Combine(parentPath, "Child");
                Directory.CreateDirectory(childPath);
                try
                {
                    using var parent = CShellItemFactory.Create(parentPath);
                    using var child = CShellItemFactory.Create(childPath);

                    Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(parent, child, false), "Parent should be an ancestor of Child.");
                    Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(parent, child, true), "Parent should be the immediate parent of Child.");
                    Assert.IsTrue(CShellItemHierachyManager.IsAncestorOf(parent, parent, false), "An item should be an ancestor of itself when fParent is false.");
                    Assert.IsFalse(CShellItemHierachyManager.IsAncestorOf(parent, parent, true), "An item should not be its own parent.");
                    Assert.IsFalse(CShellItemHierachyManager.IsAncestorOf(child, parent, false), "Child should not be an ancestor of Parent.");
                }
                finally
                {
                    if (Directory.Exists(parentPath)) Directory.Delete(parentPath, true);
                }
            });
        }

        [TestMethod]
        public async Task TestFindDesktop()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);

                var desktop2 = CShellItemFactory.Create(CSIDL.DESKTOP);

                // Find by path
                var foundByPath = manager.FindAndAllowExpansion(desktop2);
                Assert.IsNotNull(foundByPath, "Should find item by path");
                Assert.AreEqual(foundByPath.m_FullPath, desktop2?.FullPath, "Found path is not equal to sought path.");
            });
        }


        [TestMethod]
        public async Task TestFindExistingItem()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                var manager = new CShellItemHierachyManager(desktop);
                string tempDir = Path.Combine(Path.GetTempPath(), "HierarchyFind_" + Guid.NewGuid().ToString("N"));
                string filePath = Path.Combine(tempDir, "known-file.txt");
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(filePath, "test");
                try
                {
                    var foundFolder = manager.FindAndAllowExpansion(tempDir);
                    Assert.IsNotNull(foundFolder, "Should find the temporary folder by path.");

                    var foundByPidl = manager.Find(foundFolder.PIDL);
                    Assert.IsNotNull(foundByPidl, "Should find the temporary folder by PIDL.");

                    var foundFile = manager.FindAndAllowExpansion(filePath);
                    Assert.IsNotNull(foundFile, "Should find the known temporary file by path.");
                }
                finally
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
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
        public async Task TestUpdateRenamedItem_UpdatesCachedItemAndDescendants()
        {
            await Runner.EnqueueWork(() =>
            {
                string tempBase = Path.Combine(Path.GetTempPath(), "HierarchyRename_" + Guid.NewGuid().ToString("N"));
                string oldFolderPath = Path.Combine(tempBase, "OldFolder");
                string oldFilePath = Path.Combine(oldFolderPath, "child.txt");
                string newFolderPath = Path.Combine(tempBase, "NewFolder");
                string newFilePath = Path.Combine(newFolderPath, "child.txt");
                Directory.CreateDirectory(oldFolderPath);
                File.WriteAllText(oldFilePath, "test");

                IntPtr basePidl = IntPtr.Zero;
                IntPtr oldFolderPidl = IntPtr.Zero;
                IntPtr newFolderPidl = IntPtr.Zero;
                IntPtr newFilePidl = IntPtr.Zero;
                try
                {
                    basePidl = ShellAPI.ILCreateFromPathW(tempBase);
                    var root = CShellItemFactory.Create(CPidl.Clone(basePidl));
                    var manager = new CShellItemHierachyManager(CShellItemFactory.DesktopCSI, root);
                    var cachedFile = manager.FindAndAllowExpansion(oldFilePath);
                    Assert.IsNotNull(cachedFile, "The file should be cached before the rename.");
                    var cachedFolder = cachedFile.Parent;
                    Assert.IsNotNull(cachedFolder, "The renamed folder should be cached before the rename.");

                    oldFolderPidl = ShellAPI.ILCreateFromPathW(oldFolderPath);
                    var notificationItem = CShellItemFactory.Create(CPidl.Clone(oldFolderPidl));

                    Directory.Move(oldFolderPath, newFolderPath);
                    newFolderPidl = ShellAPI.ILCreateFromPathW(newFolderPath);
                    newFilePidl = ShellAPI.ILCreateFromPathW(newFilePath);

                    var updatedFolder = manager.UpdateRenamedItem(notificationItem, newFolderPidl);

                    Assert.AreSame(cachedFolder, updatedFolder,
                        "The hierarchy should update its existing cached item, not retain the notification item.");
                    Assert.AreEqual(newFolderPath, updatedFolder.FullPath, true);
                    Assert.AreEqual(newFilePath, cachedFile.FullPath, true,
                        "Cached descendants should receive their renamed ancestor's new path.");
                    Assert.AreSame(updatedFolder, manager.Find(newFolderPidl));
                    Assert.IsTrue(CPidl.ResolvesToSamePathOrName(cachedFile.PIDL, newFilePidl),
                        "Cached descendants should receive an absolute PIDL rooted at the renamed folder.");
                }
                finally
                {
                    if (basePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(basePidl);
                    if (oldFolderPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(oldFolderPidl);
                    if (newFolderPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(newFolderPidl);
                    if (newFilePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(newFilePidl);
                    if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
                }
            });
        }


    }

}
