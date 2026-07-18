using ExpControlsLib;
using System.Security.Policy;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static ExpControlsLib.ExpTree;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLibTest
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Nunit and unit tests in general don't use the WindowsFormsSynchronizationContext.
    /// This is a big problem because this file test UI code which MUST run on the WindowsFormsSynchronizationContext.
    /// And what's worse, even if you set the SynchronizationContext to a WindowsFormsSynchronizationContext, 
    /// the SynchronizationContext will be reset to NUnit.Framework.Internal.SafeSynchronizationContext after
    /// your code resumes from an "await".  So, we must set the SynchronizationContext to a 
    /// WindowsFormsSynchronizationContext before Application.Run() is called.
    /// One of the key takeaways from this is that it is possible for a resumption from an await to resume on a 
    /// different thread than the one that started the await.  I think this is caused by the thread saving the resumption
    /// thread id when Application.Run is called and then the thread context is changed after that and then the 
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ExpTreeExpListRaceConditionTests
    {
        private string _testPath;
        private const int _iterations = 10;


        [SetUp]
        public void SetUp()
        {
            var csi = CShellItemFactory.Create(CSIDL.PROFILE);
            _testPath = csi?.FullPath;
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
        [RequiresThread(ApartmentState.STA)]
        [NonParallelizable]
        public void TestExpTreeAndExpListReferenceSameTargetItem()
        {
            if (!Directory.Exists(_testPath))
            {
                Assert.Ignore($"Test path {_testPath} does not exist. Skipping test.");
                return;
            }

            Exception? failure = null;
            var form = new Form();

            form.Shown += async (_, __) =>
            {
                for (int i = 0; i< _iterations; i++)
                { 
                    try
                    {
                        // Ensure a WinForms SynchronizationContext is installed so async
                        // continuations resume on the UI (message-pumping) thread.

                        ShellController.Instance.HierachyManager.Clear();
                        Assert.That(ShellController.Instance.HierachyManager.Root, Is.SameAs(ShellController.Instance.HierachyManager.DesktopCSI),
                            "HierachyManager.Root and HierachyManager.DesktopCSI should be the same reference.");

                        var expTree = new ExpTree();
                        var expList = new ExpList();
                        expTree.StartUpDirectory = ExpTree.StartDir.Desktop;
                        expTree.Initialize(ShellController.Instance);
                        expList.Initialize(ShellController.Instance);

                        CShellItem targetItem = ShellController.Instance.HierachyManager.FindAndAllowExpansion(_testPath);
                        Assert.IsNotNull(targetItem, $"'{_testPath}' item should be found or added");

                        form.Controls.Add(expTree);
                        form.Controls.Add(expList);

                        await WaitForCondition(() => expTree.Nodes.Count > 0, "ExpTree root node to load");

                        Assert.That(expTree.Root, Is.SameAs(ShellController.Instance.HierachyManager.DesktopCSI),
                            "ExpTree.Root and HierachyManager.DesktopCSI should be the same reference");

                        Task<bool> treeLoadTask = expTree.ExpandANodeAsync(targetItem);
                        Task listLoadTask = expList.LoadDirectoryAsync(targetItem);

                        await Task.WhenAll(treeLoadTask, listLoadTask);

                        Assert.True(treeLoadTask.Result, "ExpTree failed to expand to the target item");

                        await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                        Assert.IsNotNull(expTree.SelectedItem, "ExpTree.SelectedItem should not be null");
                        Assert.IsNotNull(expList.CurrentFolderCsi, "ExpList.CurrentFolderCsi should not be null");

                        Assert.True(expTree.SelectedItem.DisplayName == expList.CurrentFolderCsi.DisplayName,
                            "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should have the same display name");

                        Assert.That(expTree.SelectedItem, Is.SameAs(expList.CurrentFolderCsi),
                            "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be the same reference");

                        CShellItem? foundItem = ShellController.Instance.HierachyManager.Find(_testPath);
                        Assert.IsNotNull(foundItem, $"CShellItemHierachyManager.Find should find the '{_testPath}' folder");

                        Assert.That(foundItem, Is.SameAs(expList.CurrentFolderCsi),
                            $"CShellItemHierachyManager.Find should return the same reference as ExpList.CurrentFolderCsi for '{_testPath}'");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        form.Close();
                    }
                }
            };

            //change from Nunit's default NUnit.Framework.Internal.SafeSynchronizationContext to a WinForms SynchronizationContext to ensure that async continuations resume on the UI thread.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Application.Run(form);

            if (failure is not null)
            {
                throw failure;
            }
        }
        
        /// <summary>
                 /// Tests that when ExpTree and ExpList concurrently load the same folder (TestPath),
                 /// they both receive the same reference to the same CShellItem from CShellItemHierachyManager.
                 /// This test verifies that race conditions during startup do not cause the controls to
                 /// create or use different instances of the same folder item.
                 /// </summary>
                 /// <remarks>
                 /// This test simulates the race condition scenario where:
                 /// 1. ExpTree.ExpandANodeAsync is called to expand TestPath
                 /// 2. ExpList.LoadDirectoryAsync is called to load the same folder
                 /// Both operations run concurrently via Task.WhenAll.
                 /// The test then verifies:
                 /// - ExpTree.SelectedItem and ExpList.CurrentFolderCsi are the exact same object reference
                 /// - CShellItemHierachyManager.Find returns the same reference as both controls
                 /// </remarks>
        [Test]
        public async Task TestExpTreeAndExpListSameReferenceAfterConcurrentLoad()
        {
            if (!Directory.Exists(_testPath))
            {
                Assert.Ignore($"Test path {_testPath} does not exist. Skipping test.");
                return;
            }

            Exception? failure = null;
            var form = new Form();

            form.Shown += async (_, __) =>
            {
                for (int i = 0; i < _iterations; i++)
                {
                    try
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

                        Task<bool> treeLoadTask = expTree.ExpandANodeAsync(_testPath);
                        Task listLoadTask = expList.LoadDirectoryAsync(_testPath);

                        await Task.WhenAll(treeLoadTask, listLoadTask);

                        await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                        Assert.IsNotNull(expTree.SelectedItem, "ExpTree.SelectedItem should not be null after expansion");
                        Assert.IsNotNull(expList.CurrentFolderCsi, "ExpList.CurrentFolderCsi should not be null after load");

                        TestContext.Progress.WriteLine($"ExpTree.SelectedItem path: {expTree.SelectedItem?.FullPath}");
                        TestContext.Progress.WriteLine($"ExpList.CurrentFolderCsi path: {expList.CurrentFolderCsi?.FullPath}");

                        Assert.That(expTree.SelectedItem?.FullPath, Is.EqualTo(_testPath).IgnoreCase,
                            "ExpTree.SelectedItem path should match the test path");
                        Assert.That(expList.CurrentFolderCsi?.FullPath, Is.EqualTo(_testPath).IgnoreCase,
                            "ExpList.CurrentFolderCsi path should match the test path");

                        Assert.That(expTree.SelectedItem, Is.SameAs(expList.CurrentFolderCsi),
                            "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be the same reference");

                        CShellItem? foundItem = ShellController.Instance.HierachyManager.Find(_testPath);
                        Assert.IsNotNull(foundItem, $"CShellItemHierachyManager.Find({_testPath}) should not return null");

                        TestContext.Progress.WriteLine($"HierachyManager.Find result path: {foundItem?.FullPath}");

                        Assert.That(foundItem, Is.SameAs(expList.CurrentFolderCsi),
                            "CShellItemHierachyManager.Find should return the same reference as ExpList.CurrentFolderCsi");

                        Assert.That(foundItem?.FullPath, Is.EqualTo(expList.CurrentFolderCsi?.FullPath).IgnoreCase,
                            "CShellItemHierachyManager.Find should return an item with the same path as ExpList.CurrentFolderCsi");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        form.Close();
                    }
                }
            };

            //change from Nunit's default NUnit.Framework.Internal.SafeSynchronizationContext to a WinForms SynchronizationContext to ensure that async continuations resume on the UI thread.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Application.Run(form);

            if (failure is not null)
            {
                throw failure;
            }
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
            if (!Directory.Exists(_testPath))
            {
                Assert.Ignore($"Test path {_testPath} does not exist. Skipping test.");
                return;
            }

            Exception? failure = null;
            var form = new Form();

            form.Shown += async (_, __) =>
            {
                for (int i = 0; i < _iterations; i++)
                {
                    try
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

                        await expTree.ExpandANodeAsync(_testPath);
                        await expList.LoadDirectoryAsync(_testPath);

                        await WaitForCondition(() => expList.CurrentFolderCsi != null, "ExpList CurrentFolderCsi to be set");

                        CShellItem? treeItem = expTree.SelectedItem;
                        CShellItem? listItem = expList.CurrentFolderCsi;

                        Assert.IsNotNull(treeItem);
                        Assert.IsNotNull(listItem);

                        CShellItem? foundByPath = ShellController.Instance.HierachyManager.Find(_testPath);
                        Assert.IsNotNull(foundByPath, "Find by path should return an item");

                        Assert.That(treeItem, Is.SameAs(listItem),
                            "ExpTree.SelectedItem and ExpList.CurrentFolderCsi should be identical references");
                        Assert.That(listItem, Is.SameAs(foundByPath),
                            "ExpList.CurrentFolderCsi should be the same reference as HierachyManager.Find result");
                        Assert.That(treeItem, Is.SameAs(foundByPath),
                            "ExpTree.SelectedItem should be the same reference as HierachyManager.Find result");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        form.Close();
                    }
                }
            };

            //change from Nunit's default NUnit.Framework.Internal.SafeSynchronizationContext to a WinForms SynchronizationContext to ensure that async continuations resume on the UI thread.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Application.Run(form);

            if (failure is not null)
            {
                throw failure;
            }
        }

    }
}
