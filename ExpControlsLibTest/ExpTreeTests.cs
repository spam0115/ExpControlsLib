using ExpControlsLib;
using NUnit.Framework;
using System.Windows.Forms;
using WindowsApiLib.Shell;

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
        public async Task TestInitialLoad(ExpTree.StartDir startDir)
        {
            var expTree = new ExpTree();
            
            // Host it in a form to ensure handle is created
            using var form = new Form();
            form.Controls.Add(expTree);
            form.Show();

            // Set root
            expTree.StartUpDirectory = startDir;

            // Wait for nodes to load. 
            // The loading happens on a background STA thread and then updates UI.
            bool loaded = false;
            for (int i = 0; i < 1000; i++) // 10 seconds timeout
            {
                if (expTree.Nodes.Count > 0)
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(10);
                Application.DoEvents(); // Keep UI alive to allow BeginInvoke/Invoke to process
            }

            Assert.IsTrue(loaded, $"Tree nodes should be loaded for {startDir}.");
            Assert.That(expTree.Nodes.Count, Is.EqualTo(1), "Root node should be present.");
            
            var rootNode = expTree.Nodes[0];
            Assert.That(rootNode.Text, Is.Not.Null.And.Not.Empty);
            
            // Wait for children of root to load if they are loaded async
            // In SetRootItemAsync, it calls BuildTree which adds children to Root.
            // So if Nodes.Count > 0, Root should already have its immediate children if BuildTree was called.
            Assert.That(rootNode.Nodes.Count, Is.GreaterThan(0), "Root node should have children.");
            
            foreach (TreeNode node in rootNode.Nodes)
            {
                Assert.That(node.Tag, Is.InstanceOf<CShellItem>());
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

        [Test]
        public async Task TestDeepAsyncExpansion()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "ExpTreeTest_" + Guid.NewGuid().ToString("N"));
            string deepPath = Path.Combine(tempPath, "A", "B", "C", "D");
            Directory.CreateDirectory(deepPath);

            try
            {
                var expTree = new ExpTree();
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                var rootItem = ShellController.Instance.HierachyManager.FindOrAdd(tempPath);
                expTree.Root = rootItem;

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                // Expand to D
                bool success = await expTree.ExpandANodeAsync(deepPath);
                
                if (!success)
                {
                    TestContext.Progress.WriteLine($"Expansion failed for {deepPath}");
                    TestContext.Progress.WriteLine($"Root path: {expTree.Root?.FullPath}");
                    TestContext.Progress.WriteLine($"Selected path: {expTree.SelectedItem?.FullPath}");
                }

                Assert.IsTrue(success, "Should successfully expand to deep path");

                // Verify selection and expansion
                Assert.IsNotNull(expTree.SelectedNode, "A node should be selected");
                var selectedItem = (CShellItem)expTree.SelectedNode.Tag;
                Assert.That(selectedItem.FullPath, Is.EqualTo(deepPath).IgnoreCase);

                // Verify hierarchy is expanded
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
                var expTree = new ExpTree();
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                expTree.Root = ShellController.Instance.HierachyManager.FindOrAdd(tempPath);
                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                // 1. Visit Root (already done by default selection)
                
                // 2. Visit FolderA
                await expTree.ExpandANodeAsync(pathA);
                await Task.Delay(200); // Allow history to record
                Application.DoEvents();

                // 3. Visit FolderB
                await expTree.ExpandANodeAsync(pathB);
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
            
            // Set Hidden attribute
            File.SetAttributes(pathHidden, File.GetAttributes(pathHidden) | FileAttributes.Hidden);

            try
            {
                var expTree = new ExpTree(tempPath);
                // Set exclusion
                expTree.ExcludedItems.Add(pathExcluded);
                expTree.ShowHiddenFolders = false;

                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                // Verify exclusion and hidden
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

                // Toggle Hidden
                expTree.ShowHiddenFolders = true;
                
                // Refresh is triggered by setter, wait for new root node to have the hidden child
                await WaitForCondition(() => expTree.Nodes.Count > 0 && expTree.Nodes[0].Nodes.Cast<TreeNode>().Any(n => ((CShellItem)n.Tag).FullPath.Equals(pathHidden, StringComparison.OrdinalIgnoreCase)), "Hidden folder to appear");
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
                var expTree = new ExpTree(tempPath);
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                Assert.IsNotNull(expTree.Nodes, $"expTree.Nodes is null.");

                await WaitForCondition(() => expTree.Nodes.Count > 0, "Root node to load");

                // 1. Simulate Created
                string newFolderPath = Path.Combine(tempPath, "NewFolder");
                Directory.CreateDirectory(newFolderPath);

                await WaitForCondition(() => ShellController.Instance.HierachyManager.FindOrAdd(newFolderPath) != null, "New folder to be added to the hierarchy manager");
                var newItem = ShellController.Instance.HierachyManager.FindOrAdd(newFolderPath);

                Assert.IsNotNull(newItem, $"ShellController failed to find or add '{newFolderPath}'.");

                ShellController.Instance.ShellUpdater.RaiseUpdateEvent(expTree.Root, new ShellItemUpdateEventArgs(newItem, CShItemUpdateType.Created));

                await WaitForCondition(() => expTree.Nodes[0].Nodes.Cast<TreeNode>().Any(n => n.Text == "NewFolder"), "New folder to appear via event");

                // 2. Simulate Deleted
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
                var expTree = new ExpTree();
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Show();

                // Trigger root load but don't await
                expTree.StartUpDirectory = ExpTree.StartDir.Desktop; 
                await Task.Delay(50); 

                // Request expansion while loading
                var targetItem = ShellController.Instance.HierachyManager.FindOrAdd(pathA);
                bool queued = expTree.ExpandANode(targetItem);
                Assert.IsTrue(queued, "Expansion should be queued when loading");

                // Now set the root that actually contains pathA so expansion can succeed
                expTree.Root = ShellController.Instance.HierachyManager.FindOrAdd(tempPath);

                await WaitForCondition(() => expTree.SelectedItem != null && expTree.SelectedItem.FullPath.Equals(pathA, StringComparison.OrdinalIgnoreCase), "Pending expansion to complete");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
        }
    }
}
