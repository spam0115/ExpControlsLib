using ExpControlsLib;
using NUnit.Framework;
using System.Security.Policy;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ExpTreeExpListRaceConditionTests
    {
        private const string TestPath = @"C:\Downloads";
        private StaThreadRunner Runner => AssemblyInitializer.Runner;


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

        private async Task WaitForCondition(Func<bool> condition, string message, int timeoutMs = 15000)
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

        /// <summary>
        /// Tests that when ExpTree and ExpList concurrently load the same folder (C:\Downloads),
        /// they both receive the same reference to the same CShellItem from CShellItemHierachyManager.
        /// This test verifies that race conditions during startup do not cause the controls to
        /// create or use different instances of the same folder item.
        /// </summary>
        /// <remarks>
        /// This test simulates the race condition scenario where:
        /// 1. ExpTree.ExpandANodeAsync is called to expand "C:\Downloads"
        /// 2. ExpList.LoadDirectoryAsync is called to load the same folder
        /// Both operations run concurrently via Task.WhenAll.
        /// The test then verifies:
        /// - ExpTree.SelectedItem and ExpList.CurrentFolderCsi are the exact same object reference
        /// - CShellItemHierachyManager.Find returns the same reference as both controls
        /// </remarks>
        [Test]
        public async Task TestExpTreeAndExpListSameReferenceAfterConcurrentLoad()
        {
            if (!Directory.Exists(TestPath))
            {
                Assert.Ignore($"Test path {TestPath} does not exist. Skipping test.");
                return;
            }

            bool done = false;

            await Runner.EnqueueWork(async () =>
            {
                var expTree = new ExpTree();
                var expList = new ExpList();
                expTree.StartUpDirectory = ExpTree.StartDir.Desktop;
                expList.Initialize(ShellController.Instance);
                expTree.Initialize(ShellController.Instance);
                
                using var form = new Form();
                form.Controls.Add(expTree);
                form.Controls.Add(expList);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "ExpTree root node to load"); //is this wait needed?  there isn't a wait like this in the main form

                Task<bool> treeLoadTask = expTree.ExpandANodeAsync(TestPath);
                Task listLoadTask = expList.LoadDirectoryAsync(TestPath);

                await Task.WhenAll(treeLoadTask, listLoadTask);

                await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                Assert.IsNotNull(expTree.SelectedItem, "ExpTree.SelectedItem should not be null after expansion");
                Assert.IsNotNull(expList.CurrentFolderCsi, "ExpList.CurrentFolderCsi should not be null after load");

                TestContext.Progress.WriteLine($"ExpTree.SelectedItem path: {expTree.SelectedItem?.FullPath}");
                TestContext.Progress.WriteLine($"ExpList.CurrentFolderCsi path: {expList.CurrentFolderCsi?.FullPath}");

                Assert.That(expTree.SelectedItem?.FullPath, Is.EqualTo(TestPath).IgnoreCase,
                    "ExpTree.SelectedItem path should match the test path");
                Assert.That(expList.CurrentFolderCsi?.FullPath, Is.EqualTo(TestPath).IgnoreCase,
                    "ExpList.CurrentFolderCsi path should match the test path");

                Assert.That(expTree.SelectedItem, Is.SameAs(expList.CurrentFolderCsi),
                    "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be the same reference");

                CShellItem? foundItem = ShellController.Instance.HierachyManager.Find(TestPath);
                Assert.IsNotNull(foundItem, $"CShellItemHierachyManager.Find({TestPath}) should not return null");

                TestContext.Progress.WriteLine($"HierachyManager.Find result path: {foundItem?.FullPath}");

                Assert.That(foundItem, Is.SameAs(expList.CurrentFolderCsi),
                    "CShellItemHierachyManager.Find should return the same reference as ExpList.CurrentFolderCsi");

                Assert.That(foundItem?.FullPath, Is.EqualTo(expList.CurrentFolderCsi?.FullPath).IgnoreCase,
                    "CShellItemHierachyManager.Find should return an item with the same path as ExpList.CurrentFolderCsi");
            });

            await WaitForCondition(() => done == true, "test did not finish within the time limit", 30000);

        }

        /// <summary>
        /// Tests that when ExpTree and ExpList concurrently load the same folder using a pre-obtained
        /// CShellItem from CShellItemHierachyManager.FindOrAdd, they both use the same reference.
        /// This variant pre-fetches the CShellItem before starting the concurrent load operations.
        /// </summary>
        /// <remarks>
        /// This test verifies that even when the CShellItem is pre-existing in the hierarchy manager
        /// before the concurrent load operations begin, both ExpTree and ExpList still reference
        /// the exact same object. This helps isolate whether the race condition occurs when
        /// items are pre-existing vs. being created during the concurrent operations.
        /// </remarks>
        [Test]
        public async Task TestExpTreeAndExpListReferenceWithExistingDownloadsItem()
        {
            if (!Directory.Exists(TestPath))
            {
                Assert.Ignore($"Test path {TestPath} does not exist. Skipping test.");
                return;
            }

            //var name = CShellItemFactory.GetFullPath(ShellController.Instance.HierachyManager.DesktopCSI);
            bool done = false;

            await Runner.EnqueueWork(async () =>
            {
                for (int n = 0; n < 1; n++)
                {
                    ShellController.Instance.HierachyManager.Clear();
                    Assert.That(ShellController.Instance.HierachyManager.Root, Is.SameAs(ShellController.Instance.HierachyManager.DesktopCSI),
                        "HierachyManager.Root and HierachyManager.DesktopCSI should be the same reference.");

                    CShellItem downloadsItem = ShellController.Instance.HierachyManager.FindAndAllowExpansion(TestPath);
                    Assert.IsNotNull(downloadsItem, "Downloads item should be found or added");

                    var expTree = new ExpTree();
                    var expList = new ExpList();
                    expTree.StartUpDirectory = ExpTree.StartDir.Desktop;
                    expTree.Initialize(ShellController.Instance);
                    expList.Initialize(ShellController.Instance);

                    using var form = new Form();
                    form.Controls.Add(expTree);
                    form.Controls.Add(expList);
                    form.Show();

                    await WaitForCondition(() => expTree.Nodes.Count > 0, "ExpTree root node to load");

                    Assert.That(expTree.Root, Is.SameAs(ShellController.Instance.HierachyManager.DesktopCSI),
                        "ExpTree.Root and HierachyManager.DesktopCSI should be the same reference");

                    Task<bool> treeLoadTask = expTree.ExpandANodeAsync(downloadsItem);
                    Task listLoadTask = expList.LoadDirectoryAsync(downloadsItem);

                    await Task.WhenAll(treeLoadTask, listLoadTask);

                    await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                    Assert.IsNotNull(expTree.SelectedItem, "ExpTree.SelectedItem should not be null");
                    Assert.IsNotNull(expList.CurrentFolderCsi, "ExpList.CurrentFolderCsi should not be null");

                    Assert.That(expTree.SelectedItem, Is.SameAs(expList.CurrentFolderCsi),
                        "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be the same reference");

                    CShellItem? foundItem = ShellController.Instance.HierachyManager.Find(TestPath);
                    Assert.IsNotNull(foundItem, "CShellItemHierachyManager.Find should find the Downloads folder");

                    Assert.That(foundItem, Is.SameAs(expList.CurrentFolderCsi),
                        "CShellItemHierachyManager.Find should return the same reference as ExpList.CurrentFolderCsi");

                    done = true;
                }
            });

            await WaitForCondition(() => done == true, "test did not finish within the time limit", 30000);

            //
        }

        /// <summary>
        /// Tests that all three references to the same folder item are identical:
        /// ExpTree.SelectedItem, ExpList.CurrentFolderCsi, and CShellItemHierachyManager.Find result.
        /// This test runs the load operations sequentially rather than concurrently to verify
        /// that even without race conditions, all three references point to the same object.
        /// </summary>
        /// <remarks>
        /// This is a comprehensive reference equality test that verifies:
        /// - ExpTree.SelectedItem is the same reference as ExpList.CurrentFolderCsi
        /// - ExpList.CurrentFolderCsi is the same reference as HierachyManager.Find result
        /// - ExpTree.SelectedItem is the same reference as HierachyManager.Find result
        /// Unlike the concurrent test, this runs operations sequentially to isolate
        /// whether the issue is specifically race-condition related or a more fundamental
        /// reference management problem.
        /// </remarks>
        [Test]
        public async Task TestHierachyManagerFindReturnsSameReferenceAsLoadedItems()
        {
            if (!Directory.Exists(TestPath))
            {
                Assert.Ignore($"Test path {TestPath} does not exist. Skipping test.");
                return;
            }

            bool done = false;

            await Runner.EnqueueWork(async () =>
            {
                var expTree = new ExpTree();
                var expList = new ExpList();
                expTree.StartUpDirectory = ExpTree.StartDir.Desktop;
                expList.Initialize(ShellController.Instance);
                expTree.Initialize(ShellController.Instance);

                using var form = new Form();
                form.Controls.Add(expTree);
                form.Controls.Add(expList);
                form.Show();

                await WaitForCondition(() => expTree.Nodes.Count > 0, "ExpTree root node to load");

                await expTree.ExpandANodeAsync(TestPath);
                await expList.LoadDirectoryAsync(TestPath);

                await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                CShellItem? treeItem = expTree.SelectedItem;
                CShellItem? listItem = expList.CurrentFolderCsi;

                Assert.IsNotNull(treeItem);
                Assert.IsNotNull(listItem);

                CShellItem? foundByPath = ShellController.Instance.HierachyManager.Find(TestPath);
                Assert.IsNotNull(foundByPath, "Find by path should return an item");

                Assert.That(treeItem, Is.SameAs(listItem),
                    "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be identical references");
                Assert.That(listItem, Is.SameAs(foundByPath),
                    "ExpList.CurrentFolderCsi should be the same reference as HierachyManager.Find result");
                Assert.That(treeItem, Is.SameAs(foundByPath),
                    "ExpTree.SelectedItem should be the same reference as HierachyManager.Find result");
            });

            await WaitForCondition(() => done == true, "test did not finish within the time limit", 30000);

        }
    }
}
