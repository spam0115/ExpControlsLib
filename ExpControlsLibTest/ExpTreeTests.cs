using ExpControlsLib;
using NUnit.Framework;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ExpTreeTests
    {
        [SetUp]
        public void SetUp()
        {
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Started : {TestContext.CurrentContext.Test.Name}");
            ShellController.Instance.ShellUpdater.AllowUpdates = true;
        }

        [TearDown]
        public void TearDown()
        {
            TestContext.Progress.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test Finished: {TestContext.CurrentContext.Test.Name}");
        }

        [TestCase(ExpTree.StartDir.Desktop)]
        [TestCase(ExpTree.StartDir.MyComputer)]
        [TestCase(ExpTree.StartDir.Windows)]
        public async Task TestStartupDirectoryLoad(ExpTree.StartDir startDir)
        {
            var expTree = new ExpTree();
            expTree.StartUpDirectory = startDir;
            expTree.Initialize(ShellController.Instance);

            using var form = new Form();
            form.Controls.Add(expTree);
            form.Show();

            await WaitForCondition(() => expTree.Nodes.Count > 0, $"Tree nodes to load for {startDir}", 15000);
            Assert.That(expTree.Nodes.Count, Is.EqualTo(1), "Root node should be present.");
            
            var rootNode = expTree.Nodes[0];
            Assert.That(rootNode.Text, Is.Not.Null.And.Not.Empty);
            Assert.That(rootNode.Nodes.Count, Is.GreaterThan(0), "Root node should have children.");
            
            foreach (TreeNode node in rootNode.Nodes)
            {
                Assert.That(node.Tag, Is.InstanceOf<CShellItem>());
            }
        }

        /// <summary>
        /// Ensures the hierarchy knows about a directory by loading its parent's contents.
        /// This is needed because FindAndAllowExtension only searches already-populated
        /// hierarchy items — it doesn't scan the filesystem.
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
        public async Task TestDeepAsyncExpansion()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeTest_" + Guid.NewGuid().ToString("N"));
            string deepPath = Path.Combine(tempPath, "A", "B", "C", "D");
            Directory.CreateDirectory(deepPath);

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                bool success = await expTree.ExpandANodeAsync(deepPath);
                
                if (!success)
                {
                    TestContext.Progress.WriteLine($"Expansion failed for {deepPath}");
                    TestContext.Progress.WriteLine($"Root path: {expTree.Root?.FullPath}");
                    TestContext.Progress.WriteLine($"Selected path: {expTree.SelectedItem?.FullPath}");
                }

                Assert.IsTrue(success, "Should successfully expand to deep path");

                Assert.IsNotNull(expTree.SelectedNode, "A node should be selected");
                var selectedItem = (CShellItem)expTree.SelectedNode.Tag;
                Assert.That(selectedItem.FullPath, Is.EqualTo(deepPath).IgnoreCase);

                var node = expTree.SelectedNode;
                while (node != expTree.Nodes[0])
                {
                    Assert.IsNotNull(node.Parent, "Intermediate nodes should have parents");
                    node = node.Parent;
                    Assert.IsTrue(node.IsExpanded, $"Node {node.Text} should be expanded");
                }
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestNavigationHistory()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeNav_" + Guid.NewGuid().ToString("N"));
            string pathA = Path.Combine(tempPath, "FolderA");
            string pathB = Path.Combine(tempPath, "FolderB");
            Directory.CreateDirectory(pathA);
            Directory.CreateDirectory(pathB);

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                // Visit FolderA
                bool successA = await expTree.ExpandANodeAsync(pathA);
                Assert.IsTrue(successA, "Should expand to FolderA");
                await Task.Delay(200);
                Application.DoEvents();

                // Visit FolderB
                bool successB = await expTree.ExpandANodeAsync(pathB);
                Assert.IsTrue(successB, "Should expand to FolderB");
                await Task.Delay(200);
                Application.DoEvents();

                Assert.IsTrue(expTree.CanGoBack, "Should be able to go back");
                
                // Go back to A
                expTree.GoBack();
                await WaitForCondition(() => expTree.SelectedItem != null && expTree.SelectedItem.FullPath.Equals(pathA, StringComparison.OrdinalIgnoreCase), "Back to FolderA");
                
                Assert.IsTrue(expTree.CanGoForward, "Should be able to go forward");

                // Go back to Root
                expTree.GoBack();
                await WaitForCondition(() => expTree.SelectedItem != null && expTree.SelectedItem.FullPath.Equals(tempPath, StringComparison.OrdinalIgnoreCase), "Back to Root");

                // Go forward to A
                expTree.GoForward();
                await WaitForCondition(() => expTree.SelectedItem != null && expTree.SelectedItem.FullPath.Equals(pathA, StringComparison.OrdinalIgnoreCase), "Forward to FolderA");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestExclusionAndFiltering()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeFilter_" + Guid.NewGuid().ToString("N"));
            string pathVisible = Path.Combine(tempPath, "Visible");
            string pathExcluded = Path.Combine(tempPath, "Excluded");
            string pathHidden = Path.Combine(tempPath, "Hidden");
            Directory.CreateDirectory(pathVisible);
            Directory.CreateDirectory(pathExcluded);
            Directory.CreateDirectory(pathHidden);
            
            File.SetAttributes(pathHidden, File.GetAttributes(pathHidden) | FileAttributes.Hidden);

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                expTree.ExcludedItems.Add(pathExcluded);
                expTree.ShowHiddenFolders = false;

                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                bool foundExcluded = false;
                bool foundHidden = false;
                bool foundVisible = false;

                foreach (TreeNode node in expTree.Nodes[0].Nodes)
                {
                    var item = (CShellItem)node.Tag;
                    if (item.FullPath.Equals(pathExcluded, StringComparison.OrdinalIgnoreCase)) foundExcluded = true;
                    if (item.FullPath.Equals(pathHidden, StringComparison.OrdinalIgnoreCase)) foundHidden = true;
                    if (item.FullPath.Equals(pathVisible, StringComparison.OrdinalIgnoreCase)) foundVisible = true;
                }

                Assert.IsFalse(foundExcluded, "Excluded folder should not be in tree");
                Assert.IsFalse(foundHidden, "Hidden folder should not be in tree when ShowHiddenFolders=false");
                Assert.IsTrue(foundVisible, "Visible folder should be in tree");
                Assert.IsTrue(Directory.Exists(pathHidden), "Hidden folder should exist on disk");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestDynamicShellUpdates()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeUpdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                Assert.IsNotNull(expTree.Nodes, $"expTree.Nodes is null.");

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                string newFolderPath = Path.Combine(tempPath, "NewFolder");
                Directory.CreateDirectory(newFolderPath);

                ShellController.Instance.LoadFolderContents(expTree.Root, SHCONTF.FOLDERS);
                var newItem = ShellController.Instance.HierachyManager.FindAndAllowExpansion(newFolderPath);

                Assert.IsNotNull(newItem, $"ShellController failed to find or add '{newFolderPath}'.");

                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(expTree.Root, new ShellItemUpdateEventArgs(newItem, CShItemUpdateType.Created));

                await WaitForCondition(() => expTree.Nodes[0].Nodes.Cast<TreeNode>().Any(n => n.Text == "NewFolder"), "New folder to appear via event");

                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(expTree.Root, new ShellItemUpdateEventArgs(newItem, CShItemUpdateType.Deleted));

                await WaitForCondition(() => !expTree.Nodes[0].Nodes.Cast<TreeNode>().Any(n => n.Text == "NewFolder"), "Folder to disappear via event");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestPendingExpansionRequest()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreePending_" + Guid.NewGuid().ToString("N"));
            string pathA = Path.Combine(tempPath, "FolderA");
            Directory.CreateDirectory(pathA);

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                bool success = await expTree.ExpandANodeAsync(pathA);
                Assert.IsTrue(success, "Should successfully expand to FolderA");

                Assert.IsNotNull(expTree.SelectedItem, "A node should be selected");
                Assert.That(expTree.SelectedItem.FullPath, Is.EqualTo(pathA).IgnoreCase, "Selected item should be FolderA");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_CreatedUnderExpandedParent()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeCrExp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempPath, "A"));
            Directory.CreateDirectory(Path.Combine(tempPath, "B"));
            Directory.CreateDirectory(Path.Combine(tempPath, "C"));

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                var rootNode = expTree.Nodes[0];
                // Root is expanded after SetRootItemAsync — children A, B, C are real nodes
                Assert.IsTrue(rootNode.IsExpanded, "Root should be expanded after load");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(3), "Root should have 3 children");

                // Create new directory D on disk and add to hierarchy
                string pathD = Path.Combine(tempPath, "D");
                Directory.CreateDirectory(pathD);
                ShellController.Instance.LoadFolderContents(expTree.Root, SHCONTF.FOLDERS);
                var itemD = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathD);
                Assert.IsNotNull(itemD, "D should be in hierarchy");

                // Raise Created event
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    expTree.Root, new ShellItemUpdateEventArgs(itemD, CShItemUpdateType.Created));

                // D should appear in the tree immediately (handler runs synchronously on UI thread)
                bool foundD = rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "D");
                Assert.IsTrue(foundD, "D should appear under expanded root after Created event");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(4), "Root should now have 4 children");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_DeletedFromExpandedParent()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeDelExp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempPath, "A"));
            Directory.CreateDirectory(Path.Combine(tempPath, "B"));
            Directory.CreateDirectory(Path.Combine(tempPath, "C"));

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                var rootNode = expTree.Nodes[0];
                Assert.IsTrue(rootNode.IsExpanded, "Root should be expanded after load");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(3), "Root should have 3 children");

                // Find B in the hierarchy
                string pathB = Path.Combine(tempPath, "B");
                var itemB = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathB);
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Verify B is in the tree before the event
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "B"), "B should be in tree before deletion");

                // Raise Deleted event
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    expTree.Root, new ShellItemUpdateEventArgs(itemB, CShItemUpdateType.Deleted));

                // B should be removed
                bool foundB = rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "B");
                Assert.IsFalse(foundB, "B should be removed from expanded root after Deleted event");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(2), "Root should now have 2 children");

                // A and C should still be present
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "A"), "A should remain");
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "C"), "C should remain");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_CreatedUnderCollapsedParent()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeCrCol_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempPath, "A"));
            Directory.CreateDirectory(Path.Combine(tempPath, "B"));

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                var rootNode = expTree.Nodes[0];
                Assert.IsTrue(rootNode.IsExpanded, "Root should be expanded after load");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(2), "Root should have 2 children");

                // Collapse the root — children are still real nodes, just hidden.
                // ShowRootLines must be true or Tv1_BeforeCollapse cancels root collapse.
                expTree.ShowRootLines = true;
                rootNode.Collapse();
                Assert.IsFalse(rootNode.IsExpanded, "Root should be collapsed");
                // Children are still there (just hidden)
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(2), "Collapsed root should still have 2 children");

                // Create C on disk and add to hierarchy
                string pathC = Path.Combine(tempPath, "C");
                Directory.CreateDirectory(pathC);
                ShellController.Instance.LoadFolderContents(expTree.Root, SHCONTF.FOLDERS);
                var itemC = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathC);
                Assert.IsNotNull(itemC, "C should be in hierarchy");

                // Raise Created event
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    expTree.Root, new ShellItemUpdateEventArgs(itemC, CShItemUpdateType.Created));

                // C should be added even though root is collapsed
                bool foundC = rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "C");
                Assert.IsTrue(foundC, "C should appear under collapsed root after Created event");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(3), "Collapsed root should now have 3 children");

                // Expand and verify C is visible
                rootNode.Expand();
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(3), "Expanded root should have 3 children");
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "C"), "C should be visible after expand");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        [Test]
        public async Task TestShellUpdate_DeletedFromCollapsedParent()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeDelCol_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempPath, "A"));
            Directory.CreateDirectory(Path.Combine(tempPath, "B"));
            Directory.CreateDirectory(Path.Combine(tempPath, "C"));

            try
            {
                EnsurePathInHierarchy(tempPath);
                var expTree = new ExpTree(tempPath);
                expTree.Initialize(ShellController.Instance);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                var rootNode = expTree.Nodes[0];
                Assert.IsTrue(rootNode.IsExpanded, "Root should be expanded after load");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(3), "Root should have 3 children");

                // Verify B is present before collapsing
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "B"), "B should be in tree");

                // Collapse the root — ShowRootLines must be true or collapse is cancelled
                expTree.ShowRootLines = true;
                rootNode.Collapse();
                Assert.IsFalse(rootNode.IsExpanded, "Root should be collapsed");

                // Find B in the hierarchy
                string pathB = Path.Combine(tempPath, "B");
                var itemB = ShellController.Instance.HierachyManager.FindAndAllowExpansion(pathB);
                Assert.IsNotNull(itemB, "B should be in hierarchy");

                // Raise Deleted event while collapsed
                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                    expTree.Root, new ShellItemUpdateEventArgs(itemB, CShItemUpdateType.Deleted));

                // B should be removed even though root is collapsed
                bool foundB = rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "B");
                Assert.IsFalse(foundB, "B should be removed from collapsed root after Deleted event");
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(2), "Collapsed root should now have 2 children");

                // Expand and verify only A and C remain
                rootNode.Expand();
                Assert.That(rootNode.Nodes.Count, Is.EqualTo(2), "Expanded root should have 2 children");
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "A"), "A should remain");
                Assert.IsTrue(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "C"), "C should remain");
                Assert.IsFalse(rootNode.Nodes.Cast<TreeNode>().Any(n => n.Text == "B"), "B should not reappear after expand");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }

        private async Task WaitForCondition(Func<bool> condition, string message, int timeoutMs = 10000)
        {
            var start = DateTime.Now;
            while (!condition())
            {
                if ((DateTime.Now - start).TotalMilliseconds > timeoutMs)
                {
                    Assert.Fail($"Timeout waiting for: {message}");
                }
                await Task.Delay(10);
                Application.DoEvents();
            }
        }
    }
}
