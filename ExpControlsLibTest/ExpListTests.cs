using ExpControlsLib;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ExpListTests
    {

        [SetUp]
        public void SetUp()
        {
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Started : {TestContext.CurrentContext.Test.Name}");
        }

        [TearDown]
        public void TearDown()
        {
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Finished: {TestContext.CurrentContext.Test.Name}");
        }


        [TestCase(ExpTree.StartDir.Desktop)]
        [TestCase(ExpTree.StartDir.MyComputer)]
        [TestCase(ExpTree.StartDir.Windows)]
        public async Task TestInitialLoad(ExpTree.StartDir startDir)
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);
            
            // Host it in a form to ensure handle is created
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            // Set root
            var csi = CShellItemFactory.Create((CSIDL)startDir);
            await expList.LoadDirectoryAsync(csi);

            // Wait for items to load. 
            // Although DisplayFilesAsync is awaited, some updates might be async.
            bool loaded = false;
            for (int i = 0; i < 1000; i++) // 10 seconds timeout
            {
                if (expList.Count > 0)
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(10);
                Application.DoEvents(); 
            }

            Assert.IsTrue(loaded, $"Items should be loaded for {startDir}.");
            Assert.That(expList.Count, Is.GreaterThan(0), "Items should be present.");
        }

        [Test]
        public async Task TestNavigationHistory()
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);

            // Host it in a form to ensure handle is created
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            // 1. Load first folder
            var windowsCsi = CShellItemFactory.Create(CSIDL.WINDOWS);
            await expList.LoadDirectoryAsync(windowsCsi);
            
            Assert.That(expList.CurrentPath, Is.EqualTo(windowsCsi.FullPath), "First folder should be loaded.");
            Assert.IsFalse(expList.CanGoBack, "CanGoBack should be false after first load.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after first load.");

            // 2. Load second folder
            var systemCsi = CShellItemFactory.Create(CSIDL.SYSTEM);
            await expList.LoadDirectoryAsync(systemCsi);

            Assert.That(expList.CurrentPath, Is.EqualTo(systemCsi.FullPath), "Second folder should be loaded.");
            Assert.IsTrue(expList.CanGoBack, "CanGoBack should be true after second load.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after second load.");

            // 3. Go Back
            bool folderChanged = false;
            expList.ExpListCurrentFolderChanged += (newCsi, oldCsi) => folderChanged = true;
            
            expList.GoBack();
            
            // Wait for GoBack (async void) to complete
            for (int i = 0; i < 100; i++)
            {
                if (folderChanged) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.IsTrue(folderChanged, "Folder should have changed back.");
            Assert.That(expList.CurrentPath, Is.EqualTo(windowsCsi.FullPath), "Should be back in the first folder.");
            Assert.IsFalse(expList.CanGoBack, "CanGoBack should be false after going back.");
            Assert.IsTrue(expList.CanGoForward, "CanGoForward should be true after going back.");

            // 4. Go Forward
            folderChanged = false;
            expList.GoForward();

            // Wait for GoForward (async void) to complete
            for (int i = 0; i < 100; i++)
            {
                if (folderChanged) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.IsTrue(folderChanged, "Folder should have changed forward.");
            Assert.That(expList.CurrentPath, Is.EqualTo(systemCsi.FullPath), "Should be back in the second folder.");
            Assert.IsTrue(expList.CanGoBack, "CanGoBack should be true after going forward.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after going forward.");
        }

        [Test]
        public async Task TestExclusionFilter()
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            var windowsCsi = CShellItemFactory.Create(CSIDL.WINDOWS);
            await expList.LoadDirectoryAsync(windowsCsi);

            // Wait for load
            for (int i = 0; i < 200; i++)
            {
                if (expList.Count > 10) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.That(expList.Count, Is.GreaterThan(10), "Windows folder should load.");

            var itemToExclude = expList.GetItem(0);
            Assert.IsNotNull(itemToExclude, "itemToExclude is null.");

            var pathToExclude = itemToExclude.FullPath;

            expList.ExcludedItems.Add(pathToExclude.Trim(':', '{', '}'));
            
            // Reload
            await expList.LoadDirectoryAsync(windowsCsi, reload: true);
            
            // Wait for load
            for (int i = 0; i < 100; i++)
            {
                bool found = false;
                for (int j = 0; j < expList.Count; j++)
                {
                    if (expList.GetItem(j).FullPath == pathToExclude) { found = true; break; }
                }
                if (!found && expList.Count > 0) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            var paths = new List<string>();
            for (int i = 0; i < expList.Count; i++) paths.Add(expList.GetItem(i).FullPath);

            Assert.IsFalse(paths.Contains(pathToExclude), "Item should be excluded.");
        }

        [Test]
        public async Task TestSortingByName()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListTest_Sort_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "C.txt"), "test");
                File.WriteAllText(Path.Combine(tempDir, "A.txt"), "test");
                File.WriteAllText(Path.Combine(tempDir, "B.txt"), "test");

                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);

                // Wait for load
                for (int i = 0; i < 100; i++)
                {
                    if (expList.Count >= 3) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(3));

                // Sort Ascending
                expList.Sort(0, SortOrder.Ascending);
                Application.DoEvents();
                Assert.That(expList.GetItem(0).DisplayName, Is.EqualTo("A.txt"));
                Assert.That(expList.GetItem(1).DisplayName, Is.EqualTo("B.txt"));
                Assert.That(expList.GetItem(2).DisplayName, Is.EqualTo("C.txt"));

                // Sort Descending
                expList.Sort(0, SortOrder.Descending);
                Application.DoEvents();
                
                if (expList.GetItem(0).DisplayName != "C.txt")
                {
                    TestContext.Progress.WriteLine("Sorting Descending Failed!");
                    for (int i = 0; i < expList.Count; i++) TestContext.Progress.WriteLine($"  Item {i}: {expList.GetItem(i).DisplayName}");
                }

                Assert.That(expList.GetItem(0).DisplayName, Is.EqualTo("C.txt"));
                Assert.That(expList.GetItem(1).DisplayName, Is.EqualTo("B.txt"));
                Assert.That(expList.GetItem(2).DisplayName, Is.EqualTo("A.txt"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task TestCustomColumnData()
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            expList.Columns.Add("Score", "Score");
            expList.ExpListGetColumnData += (s, e) =>
            {
                e.Item.ColumnDic["Score"] = new ListViewSubitemData("99.5", 99.5f);
            };

            var windowsCsi = CShellItemFactory.Create(CSIDL.WINDOWS);
            await expList.LoadDirectoryAsync(windowsCsi);

            // Wait for load
            for (int i = 0; i < 200; i++)
            {
                if (expList.Count > 0) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.That(expList.Count, Is.GreaterThan(0), "Should have loaded some items from Windows folder.");

            var item = expList.GetItem(0);
            Assert.IsNotNull(item);
            Assert.IsTrue(item.ColumnDic.ContainsKey("Score"), "Score should be in ColumnDic.");
            Assert.That(item.ColumnDic["Score"].Text, Is.EqualTo("99.5"));
            Assert.That(item.ColumnDic["Score"].Tag, Is.EqualTo(99.5f));
        }


        [Test]
        public async Task TestGoUpNavigation()
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            var systemDir = CShellItemFactory.Create(CSIDL.SYSTEM);
            await expList.LoadDirectoryAsync(systemDir);

            Assert.IsTrue(expList.CanGoUp, "Should be able to go up from System folder.");
            
            bool folderChanged = false;
            expList.ExpListCurrentFolderChanged += (newCsi, oldCsi) => folderChanged = true;

            expList.GoUp();

            // Wait for GoUp (async void) to complete
            for (int i = 0; i < 100; i++)
            {
                if (folderChanged) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.IsTrue(folderChanged, "Folder should have changed up.");
            var windowsDir = CShellItemFactory.Create(CSIDL.WINDOWS);
            Assert.That(expList.CurrentPath, Is.EqualTo(windowsDir.FullPath), "Should be in Windows folder.");
        }

        [Test]
        public async Task TestDynamicUpdate_Deleted()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListTest_Dynamic_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var file1 = Path.Combine(tempDir, "file1.txt");
                File.WriteAllText(file1, "test");

                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);

                // Wait for load
                for (int i = 0; i < 100; i++)
                {
                    if (expList.Count >= 1) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(1));
                var item = expList.GetItem(0);

                // Simulate deletion event
                // Need to use reflection or just access the public static event
                var eventArgs = new ShellItemUpdateEventArgs(item, CShItemUpdateType.Deleted);
                
                // Trigger the event. Since it's static on CShellItemUpdater, 
                // we can't easily fire it if we don't have access to the delegate.
                // But wait, CShellItemUpdater.UpdateEvent is public static.
                // We can't fire it directly because it's an event (only the class can fire it).
                
                // However, ExpList subscribes to it. If I can't fire it, I might need to 
                // invoke the private method DoItemUpdate via reflection for this test.
                
                var method = typeof(ExpList).GetMethod("DoItemUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method.Invoke(expList, new object[] { ShellController.Instance, eventArgs });

                // Wait for update
                for (int i = 0; i < 100; i++)
                {
                    if (expList.Count == 0) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(0), "Item should have been removed from list.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task TestVirtualModeSelection()
        {
            var expList = new ExpList();
            expList.Initialize(ShellController.Instance);
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            expList.VirtualMode = true;
            var windowsCsi = CShellItemFactory.Create(CSIDL.WINDOWS);
            await expList.LoadDirectoryAsync(windowsCsi);

            // Wait for load
            for (int i = 0; i < 200; i++)
            {
                if (expList.Count > 2) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.That(expList.Count, Is.GreaterThan(2), "Should have items in virtual mode.");

            // Access the internal ListView via reflection to simulate selection
            var listViewField = typeof(ExpList).GetField("_listView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var listView = (ListView)listViewField.GetValue(expList);

            listView.SelectedIndices.Add(0);
            listView.SelectedIndices.Add(1);

            Assert.That(expList.SelectedCount, Is.EqualTo(2));
            var selectedItems = expList.SelectedCShellItems.ToList();
            Assert.That(selectedItems.Count, Is.EqualTo(2));
            Assert.That(selectedItems[0].FullPath, Is.EqualTo(expList.GetItem(0).FullPath));
            Assert.That(selectedItems[1].FullPath, Is.EqualTo(expList.GetItem(1).FullPath));
        }

        /// <summary>
        /// Ensures the hierarchy knows about a directory by loading its parent's contents.
        /// </summary>
        private static void EnsurePathInHierarchy(string path)
        {
            var parentPath = Path.GetDirectoryName(path);
            if (parentPath == null) return;

            var parentCsi = ShellController.Instance.HierachyManager.FindAndAllowExpansion(parentPath);
            if (parentCsi != null)
            {
                ShellController.Instance.LoadFolderContents(parentCsi, SHCONTF.FOLDERS);
            }
        }

        [Test]
        public async Task TestShellUpdate_CreatedInCurrentFolder()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListCrIn_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "A"));
            Directory.CreateDirectory(Path.Combine(tempDir, "B"));

            try
            {
                EnsurePathInHierarchy(tempDir);
                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);

                // Wait for items to load
                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 2) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(2), "Should have A and B");

                // Create C on disk and add to hierarchy
                string pathC = Path.Combine(tempDir, "C");
                Directory.CreateDirectory(pathC);
                var rootCsi = ShellController.Instance.HierachyManager.FindAndAllowExpansion(tempDir);
                ShellController.Instance.LoadFolderContents(rootCsi, SHCONTF.FOLDERS);
                var itemC = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathC);
                Assert.IsNotNull(itemC, "C should be in hierarchy");

                // Raise Created event — sender is the parent folder, e.Item is the new child
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    rootCsi, new ShellItemUpdateEventArgs(itemC, CShItemUpdateType.Created));

                // Wait for item to appear
                for (int i = 0; i < 100; i++)
                {
                    bool found = false;
                    for (int j = 0; j < expList.Count; j++)
                    {
                        if (expList.GetItem(j).DisplayName == "C") { found = true; break; }
                    }
                    if (found) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                bool foundC = false;
                for (int j = 0; j < expList.Count; j++)
                {
                    if (expList.GetItem(j).DisplayName == "C") { foundC = true; break; }
                }
                Assert.IsTrue(foundC, "C should appear in the list after Created event");
                Assert.That(expList.Count, Is.EqualTo(3), "Should now have A, B, C");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_DeletedFromCurrentFolder()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListDelIn_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "A"));
            Directory.CreateDirectory(Path.Combine(tempDir, "B"));
            Directory.CreateDirectory(Path.Combine(tempDir, "C"));

            try
            {
                EnsurePathInHierarchy(tempDir);
                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);

                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 3) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(3), "Should have A, B, C");

                // Find B in the list
                CShellItem itemB = null;
                for (int j = 0; j < expList.Count; j++)
                {
                    if (expList.GetItem(j).DisplayName == "B") { itemB = expList.GetItem(j); break; }
                }
                Assert.IsNotNull(itemB, "B should be in the list");

                var rootCsi = ShellController.Instance.HierachyManager.FindAndAllowExpansion(tempDir);

                // Raise Deleted event
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    rootCsi, new ShellItemUpdateEventArgs(itemB, CShItemUpdateType.Deleted));

                // Wait for item to be removed
                for (int i = 0; i < 100; i++)
                {
                    bool found = false;
                    for (int j = 0; j < expList.Count; j++)
                    {
                        if (expList.GetItem(j).DisplayName == "B") { found = true; break; }
                    }
                    if (!found) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                bool foundB = false;
                for (int j = 0; j < expList.Count; j++)
                {
                    if (expList.GetItem(j).DisplayName == "B") { foundB = true; break; }
                }
                Assert.IsFalse(foundB, "B should be removed from the list after Deleted event");
                Assert.That(expList.Count, Is.EqualTo(2), "Should now have A and C");

                // Verify A and C remain
                var names = new List<string>();
                for (int j = 0; j < expList.Count; j++) names.Add(expList.GetItem(j).DisplayName);
                Assert.That(names, Does.Contain("A"), "A should remain");
                Assert.That(names, Does.Contain("C"), "C should remain");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_CreatedInDifferentFolder()
        {
            var tempDir1 = Path.Combine(Path.GetTempPath(), "ExpListCrDiff1_" + Guid.NewGuid().ToString("N"));
            var tempDir2 = Path.Combine(Path.GetTempPath(), "ExpListCrDiff2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(tempDir2);
            Directory.CreateDirectory(Path.Combine(tempDir1, "A"));

            try
            {
                EnsurePathInHierarchy(tempDir1);
                EnsurePathInHierarchy(tempDir2);
                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                // Load tempDir1 — the list shows tempDir1's contents
                await expList.LoadDirectoryAsync(tempDir1);

                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 1) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(1), "Should have A in tempDir1");
                int countBefore = expList.Count;

                // Create B in tempDir2 (a DIFFERENT folder)
                string pathB = Path.Combine(tempDir2, "B");
                Directory.CreateDirectory(pathB);
                var dir2Csi = ShellController.Instance.HierachyManager.FindAndAllowExpansion(tempDir2);
                ShellController.Instance.LoadFolderContents(dir2Csi, SHCONTF.FOLDERS);
                var itemB = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathB);
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Raise Created event for item in tempDir2 — should be ignored by the list
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    dir2Csi, new ShellItemUpdateEventArgs(itemB, CShItemUpdateType.Created));

                await Task.Delay(500);
                Application.DoEvents();

                // List should be unchanged — we're viewing tempDir1, not tempDir2
                Assert.That(expList.Count, Is.EqualTo(countBefore), "List should be unchanged — event was for a different folder");
            }
            finally
            {
                if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
                if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_DeletedFromDifferentFolder()
        {
            var tempDir1 = Path.Combine(Path.GetTempPath(), "ExpListDelDiff1_" + Guid.NewGuid().ToString("N"));
            var tempDir2 = Path.Combine(Path.GetTempPath(), "ExpListDelDiff2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(tempDir2);
            Directory.CreateDirectory(Path.Combine(tempDir1, "A"));
            Directory.CreateDirectory(Path.Combine(tempDir2, "B"));

            try
            {
                EnsurePathInHierarchy(tempDir1);
                EnsurePathInHierarchy(tempDir2);
                var expList = new ExpList();
                expList.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                // Load tempDir1
                await expList.LoadDirectoryAsync(tempDir1);

                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 1) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(1), "Should have A in tempDir1");
                int countBefore = expList.Count;

                // Find B in tempDir2's hierarchy
                var dir2Csi = ShellController.Instance.HierachyManager.FindAndAllowExpansion(tempDir2);
                var itemB = ShellController.Instance.HierachyManager.FindAndAllowExpansion(Path.Combine(tempDir2, "B"));
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Raise Deleted event for item in tempDir2 — should be ignored
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    dir2Csi, new ShellItemUpdateEventArgs(itemB, CShItemUpdateType.Deleted));

                await Task.Delay(500);
                Application.DoEvents();

                // List should be unchanged
                Assert.That(expList.Count, Is.EqualTo(countBefore), "List should be unchanged — event was for a different folder");
            }
            finally
            {
                if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
                if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
            }
        }
    }
}

