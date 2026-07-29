using ExpControlsLib;
using WindowsApiLib.Shell;
using WindowsApiLibTest;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ExpListTests
    {
        private ShellController _shellController = null!;

        [SetUp]
        public void SetUp()
        {
            _shellController = new ShellController();
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Started : {TestContext.CurrentContext.Test.Name}");
        }

        [TearDown]
        public void TearDown()
        {
            _shellController.Dispose();
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Finished: {TestContext.CurrentContext.Test.Name}");
        }

        [Test]
        public void ExpListInitializeTwiceThrows()
        {
            using var expList = new ExpList();
            expList.Initialize(_shellController, new MockFileSystem());

            Assert.Throws<InvalidOperationException>(() => expList.Initialize(_shellController, new MockFileSystem()));
        }

        [Test]
        public void GetAvailableNewFolderName_UsesDefaultWhenAvailable()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ExpListNewFolder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                Assert.That(ExpList.GetAvailableNewFolderName(tempDir), Is.EqualTo("New Folder"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void GetAvailableNewFolderName_IncrementsPastExistingFilesAndFolders()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ExpListNewFolder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "New Folder"));
                File.WriteAllText(Path.Combine(tempDir, "New Folder (2)"), "occupied");
                Directory.CreateDirectory(Path.Combine(tempDir, "New Folder (3)"));

                Assert.That(ExpList.GetAvailableNewFolderName(tempDir), Is.EqualTo("New Folder (4)"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void VirtualModeCannotChangeAfterDisplay()
        {
            using var expList = new ExpList();
            expList.Initialize(_shellController, new MockFileSystem());

            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            Assert.Throws<InvalidOperationException>(() => expList.VirtualMode = true);
        }

        [Test]
        public void ExpTreeInitializeTwiceThrows()
        {
            using var expTree = new ExpTree();
            expTree.Initialize(_shellController);

            Assert.Throws<InvalidOperationException>(() => expTree.Initialize(_shellController));
        }


        [TestCase(ExpTree.StartDir.Profile)]
        [TestCase(ExpTree.StartDir.ApplicatationData)]
        public async Task TestInitialLoad(ExpTree.StartDir startDir)
        {
            var expList = new ExpList();
            expList.Initialize(_shellController, new MockFileSystem());
            
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

            Assert.That(expList.CurrentFolderCsi, Is.Not.Null, $"{startDir} should be loaded.");
            Assert.IsTrue(loaded, $"Items should be loaded for {startDir}.");
            Assert.That(expList.Count, Is.GreaterThan(0), "Items should be present.");
        }

        [Test]
        public async Task TestNavigationHistory()
        {
            var expList = new ExpList();
            expList.Initialize(_shellController, new MockFileSystem());

            // Host it in a form to ensure handle is created
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            // 1. Load first folder
            var userProfileCsi = CShellItemFactory.Create(CSIDL.PROFILE);
            await expList.LoadDirectoryAsync(userProfileCsi);
            
            Assert.That(expList.CurrentPath, Is.EqualTo(userProfileCsi.FullPath), "First folder should be loaded.");
            Assert.IsFalse(expList.CanGoBack, "CanGoBack should be false after first load.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after first load.");

            // 2. Load second folder
            var myDocumentsCsi = CShellItemFactory.Create(CSIDL.MYDOCUMENTS);
            await expList.LoadDirectoryAsync(myDocumentsCsi);

            Assert.That(expList.CurrentPath, Is.EqualTo(myDocumentsCsi.FullPath), "Second folder should be loaded.");
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
            Assert.That(expList.CurrentPath, Is.EqualTo(userProfileCsi.FullPath), "Should be back in the first folder.");
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
            Assert.That(expList.CurrentPath, Is.EqualTo(myDocumentsCsi.FullPath), "Should be back in the second folder.");
            Assert.IsTrue(expList.CanGoBack, "CanGoBack should be true after going forward.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after going forward.");
        }

        [Test]
        public async Task TestExclusionFilter()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListExclude_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    File.WriteAllText(Path.Combine(tempDir, $"item-{i:D2}.txt"), "test");
                }

                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);
                Assert.That(expList.Count, Is.EqualTo(12));

                var itemToExclude = expList.GetItem(0);
                Assert.That(itemToExclude, Is.Not.Null);
                var pathToExclude = itemToExclude!.FullPath;

                expList.ExcludedItems.Add(pathToExclude.Trim(':', '{', '}'));
                await expList.LoadDirectoryAsync(tempDir, reload: true);

                var paths = Enumerable.Range(0, expList.Count)
                    .Select(i => expList.GetItem(i).FullPath)
                    .ToArray();

                Assert.That(paths, Does.Not.Contain(pathToExclude));
                Assert.That(expList.Count, Is.EqualTo(11));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
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
                expList.Initialize(_shellController, new MockFileSystem());
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
            expList.Initialize(_shellController, new MockFileSystem());
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            expList.Columns.Add("Score", "Score");
            expList.ExpListGetColumnData += (s, e) =>
            {
                e.Item.ColumnDic["Score"] = new ListViewSubitemData("99.5", 99.5f);
            };

            var userProfileCsi = CShellItemFactory.Create(CSIDL.PROFILE);
            await expList.LoadDirectoryAsync(userProfileCsi);

            // Wait for load
            for (int i = 0; i < 200; i++)
            {
                if (expList.Count > 0) break;
                await Task.Delay(50);
                Application.DoEvents();
            }

            Assert.That(expList.Count, Is.GreaterThan(0), "Should have loaded some items from the user profile folder.");

            var item = expList.GetItem(0);
            Assert.IsNotNull(item);
            Assert.IsTrue(item.ColumnDic.ContainsKey("Score"), "Score should be in ColumnDic.");
            Assert.That(item.ColumnDic["Score"].Text, Is.EqualTo("99.5"));
            Assert.That(item.ColumnDic["Score"].Tag, Is.EqualTo(99.5f));
        }


        [Test]
        public async Task TestGoUpNavigation()
        {
            var parentDir = Path.Combine(Path.GetTempPath(), "ExpListGoUp_" + Guid.NewGuid().ToString("N"));
            var childDir = Path.Combine(parentDir, "Child");
            Directory.CreateDirectory(childDir);
            File.WriteAllText(Path.Combine(parentDir, "parent.txt"), "test");
            File.WriteAllText(Path.Combine(childDir, "child.txt"), "test");

            try
            {
                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(childDir);
                Assert.That(expList.CanGoUp, Is.True);

                bool folderChanged = false;
                expList.ExpListCurrentFolderChanged += (_, _) => folderChanged = true;
                expList.GoUp();

                for (int i = 0; i < 100; i++)
                {
                    if (folderChanged && string.Equals(expList.CurrentPath, parentDir, StringComparison.OrdinalIgnoreCase)) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(folderChanged, Is.True, "Folder should have changed up.");
                Assert.That(expList.CurrentPath, Is.EqualTo(parentDir).IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(parentDir)) Directory.Delete(parentDir, true);
            }
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
                expList.Initialize(_shellController, new MockFileSystem());
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
                Assert.That(item, Is.Not.Null);
                Assert.That(expList.CurrentFolderCsi, Is.Not.Null);

                int itemsChangedCount = 0;
                expList.ExpListItemsChanged += (_, _) => itemsChangedCount++;

                var eventArgs = new ShellItemUpdateEventArgs(item!, CShItemUpdateType.Deleted);
                _shellController.ShellUpdater.RaiseUpdateEvent(expList.CurrentFolderCsi!, eventArgs);

                // Wait for update
                for (int i = 0; i < 100; i++)
                {
                    if (expList.Count == 0) break;
                    await Task.Delay(50);
                    Application.DoEvents();
                }

                Assert.That(expList.Count, Is.EqualTo(0), "Item should have been removed from list.");
                Assert.That(itemsChangedCount, Is.EqualTo(1),
                    "A delete notification for the displayed folder should dispatch one items-changed event.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task TestLatestDirectoryLoadWinsWhenPreviousLoadIsCancelled()
        {
            var firstDir = Path.Combine(Path.GetTempPath(), "ExpListCancelFirst_" + Guid.NewGuid().ToString("N"));
            var finalDir = Path.Combine(Path.GetTempPath(), "ExpListCancelFinal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(firstDir);
            Directory.CreateDirectory(finalDir);

            try
            {
                for (int i = 0; i < 100; i++)
                {
                    File.WriteAllText(Path.Combine(firstDir, $"stale-{i:D3}.txt"), "stale");
                }

                var expectedNames = new[] { "winner-1.txt", "winner-2.txt", "winner-3.txt" };
                foreach (string name in expectedNames)
                {
                    File.WriteAllText(Path.Combine(finalDir, name), "winner");
                }

                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                Task firstLoad = expList.LoadDirectoryAsync(firstDir);
                Task finalLoad = expList.LoadDirectoryAsync(finalDir);
                await Task.WhenAll(firstLoad, finalLoad);

                var actualNames = Enumerable.Range(0, expList.Count)
                    .Select(i => expList.GetItem(i).DisplayName)
                    .ToArray();

                Assert.That(expList.CurrentPath, Is.EqualTo(finalDir));
                Assert.That(actualNames, Is.EquivalentTo(expectedNames));
                Assert.That(actualNames, Has.None.StartsWith("stale-"),
                    "A cancelled load must not overwrite the final directory's items.");
            }
            finally
            {
                if (Directory.Exists(firstDir)) Directory.Delete(firstDir, true);
                if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
            }
        }

        [Test]
        public async Task TestVirtualModeSelection()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListVirtual_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                for (int i = 0; i < 3; i++)
                    File.WriteAllText(Path.Combine(tempDir, $"item-{i}.txt"), "test");

                using var expList = new ExpList { VirtualMode = true };
                expList.Initialize(_shellController, new MockFileSystem());
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);
                Assert.That(expList.Count, Is.EqualTo(3));

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
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Ensures the hierarchy knows about a directory by loading its parent's contents.
        /// </summary>
        private void EnsurePathInHierarchy(string path)
        {
            var parentPath = Path.GetDirectoryName(path);
            if (parentPath == null) return;

            var parentCsi = _shellController.HierachyManager.FindAndAllowExpansion(parentPath);
            if (parentCsi != null)
            {
                parentCsi.LoadFolderContents(false, true);
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
                expList.Initialize(_shellController, new MockFileSystem());
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
                var rootCsi = _shellController.HierachyManager.FindAndAllowExpansion(tempDir);
                rootCsi.LoadFolderContents(false, true);
                var itemC = _shellController.HierachyManager.FindAndAllowExpansion(pathC);
                Assert.IsNotNull(itemC, "C should be in hierarchy");

                // Raise Created event — sender is the parent folder, e.Item is the new child
                _shellController.ShellUpdater.RaiseUpdateEvent(
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
                expList.Initialize(_shellController, new MockFileSystem());
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

                var rootCsi = _shellController.HierachyManager.FindAndAllowExpansion(tempDir);

                // Raise Deleted event
                _shellController.ShellUpdater.RaiseUpdateEvent(
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
                expList.Initialize(_shellController, new MockFileSystem());
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
                var dir2Csi = _shellController.HierachyManager.FindAndAllowExpansion(tempDir2);
                dir2Csi.LoadFolderContents(false, true);
                var itemB = _shellController.HierachyManager.FindAndAllowExpansion(pathB);
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Raise Created event for item in tempDir2 — should be ignored by the list
                _shellController.ShellUpdater.RaiseUpdateEvent(
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
                expList.Initialize(_shellController, new MockFileSystem());
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
                var dir2Csi = _shellController.HierachyManager.FindAndAllowExpansion(tempDir2);
                var itemB = _shellController.HierachyManager.FindAndAllowExpansion(Path.Combine(tempDir2, "B"));
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Raise Deleted event for item in tempDir2 — should be ignored
                _shellController.ShellUpdater.RaiseUpdateEvent(
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

        // ── Checkbox support ─────────────────────────────────────────────────────

        [Test]
        public void CheckBoxes_Property_ForwardsToInnerListView()
        {
            using var expList = new ExpList();
            expList.Initialize(_shellController, new MockFileSystem());
            using var form = new Form();
            form.Controls.Add(expList);
            form.Show();

            Assert.IsFalse(expList.CheckBoxes, "Default should be false");

            expList.CheckBoxes = true;
            Assert.IsTrue(expList.CheckBoxes);

            expList.CheckBoxes = false;
            Assert.IsFalse(expList.CheckBoxes);
        }

        [Test]
        public async Task SetChecked_UpdatesModelAndRaisesEvent()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(tempDir, "b.txt"), "x");
            try
            {
                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                expList.CheckBoxes = true;
                expList.VirtualMode = true;
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);
                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 2) break;
                    await Task.Delay(10);
                    Application.DoEvents();
                }
                Assert.That(expList.Count, Is.EqualTo(2));

                var item = expList.GetItem(0)!;
                ExpListItemCheckedEventArgs? receivedArgs = null;
                expList.ItemChecked += (s, e) => receivedArgs = e;

                expList.SetChecked(item, true);

                Assert.IsTrue(item.Checked, "CShellItem.Checked should be true");
                Assert.IsNotNull(receivedArgs, "ItemChecked event should have fired");
                Assert.That(receivedArgs!.Item, Is.SameAs(item));
                Assert.IsTrue(receivedArgs.Checked);
                Assert.That(expList.CheckedCount, Is.EqualTo(1));
                Assert.That(expList.CheckedShellItems.Single(), Is.SameAs(item));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task CheckAll_UncheckAll_UpdatesAllItems()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListCheckAll_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            for (int i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(tempDir, $"f{i}.txt"), "x");
            try
            {
                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                expList.CheckBoxes = true;
                expList.VirtualMode = true;
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);
                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 5) break;
                    await Task.Delay(10);
                    Application.DoEvents();
                }
                Assert.That(expList.Count, Is.EqualTo(5));

                expList.CheckAll();
                Assert.That(expList.CheckedCount, Is.EqualTo(5), "All 5 should be checked after CheckAll");

                expList.UncheckAll();
                Assert.That(expList.CheckedCount, Is.EqualTo(0), "All should be unchecked after UncheckAll");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task CheckedState_SurvivesSort()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ExpListCheckSort_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "C.txt"), "x");
            File.WriteAllText(Path.Combine(tempDir, "A.txt"), "x");
            File.WriteAllText(Path.Combine(tempDir, "B.txt"), "x");
            try
            {
                using var expList = new ExpList();
                expList.Initialize(_shellController, new MockFileSystem());
                expList.CheckBoxes = true;
                expList.VirtualMode = true;
                using var form = new Form();
                form.Controls.Add(expList);
                form.Show();

                await expList.LoadDirectoryAsync(tempDir);
                for (int i = 0; i < 200; i++)
                {
                    if (expList.Count >= 3) break;
                    await Task.Delay(10);
                    Application.DoEvents();
                }
                Assert.That(expList.Count, Is.EqualTo(3));

                // Find "B.txt" and check it
                CShellItem? itemB = null;
                for (int j = 0; j < expList.Count; j++)
                    if (expList.GetItem(j)?.DisplayName == "B.txt") { itemB = expList.GetItem(j); break; }
                Assert.IsNotNull(itemB, "B.txt should be in the list");
                expList.SetChecked(itemB!, true);
                Assert.That(expList.CheckedCount, Is.EqualTo(1));

                // Sort descending — B.txt should still be checked regardless of its new index
                expList.Sort(0, SortOrder.Descending);
                Application.DoEvents();

                Assert.IsTrue(itemB!.Checked, "B.txt.Checked should survive sort");
                Assert.That(expList.CheckedCount, Is.EqualTo(1));
                Assert.That(expList.CheckedShellItems.Single(), Is.SameAs(itemB));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}

