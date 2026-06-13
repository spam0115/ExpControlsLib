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
            TestContext.Progress.WriteLine($"--- Test Started: {TestContext.CurrentContext.Test.Name} at {DateTime.Now:HH:mm:ss.fff} ---");
        }

        [TearDown]
        public void TearDown()
        {
            TestContext.Progress.WriteLine($"--- Test Finished: {TestContext.CurrentContext.Test.Name} at {DateTime.Now:HH:mm:ss.fff} ---");
        }

        [Test]
        public async Task TestInitialLoad_Desktop()
        {
            var expTree = new ExpTree();
            
            // Host it in a form to ensure handle is created
            using var form = new Form();
            form.Controls.Add(expTree);
            form.Show();

            // Set root to Desktop
            expTree.StartUpDirectory = ExpTree.StartDir.Desktop;

            // Wait for nodes to load. 
            // The loading happens on a background STA thread and then updates UI.
            bool loaded = false;
            for (int i = 0; i < 50; i++) // 5 seconds timeout
            {
                if (expTree.Nodes.Count > 0)
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(100);
                Application.DoEvents(); // Keep UI alive to allow BeginInvoke/Invoke to process
            }

            Assert.IsTrue(loaded, "Tree nodes should be loaded.");
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

        [Test]
        public async Task TestInitialLoad_MyComputer()
        {
            var expTree = new ExpTree();
            using var form = new Form();
            form.Controls.Add(expTree);
            form.Show();

            expTree.StartUpDirectory = ExpTree.StartDir.MyComputer;

            bool loaded = false;
            for (int i = 0; i < 50; i++)
            {
                if (expTree.Nodes.Count > 0)
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(100);
                Application.DoEvents();
            }

            Assert.IsTrue(loaded, "Tree nodes should be loaded for MyComputer.");
            Assert.That(expTree.Nodes.Count, Is.EqualTo(1));
            Assert.That(expTree.Nodes[0].Nodes.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task TestInitialLoad_WindowsPath()
        {
            var expTree = new ExpTree();
            using var form = new Form();
            form.Controls.Add(expTree);
            form.Show();

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var csi = CShellItemFactory.CreateCShItem(winDir);
            
            // Set root via RootItem property
            expTree.RootItem = csi;

            bool loaded = false;
            for (int i = 0; i < 50; i++)
            {
                if (expTree.Nodes.Count > 0)
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(100);
                Application.DoEvents();
            }

            Assert.IsTrue(loaded, "Tree nodes should be loaded for Windows path.");
            Assert.That(expTree.Nodes.Count, Is.EqualTo(1));
            // The display name might vary (localized), but it's usually "Windows" or the folder name.
            Assert.That(expTree.Nodes[0].Text, Is.Not.Null.And.Not.Empty);
            
            // Verify it represents the right item
            var rootCsi = expTree.Nodes[0].Tag as CShellItem;
            Assert.That(rootCsi, Is.Not.Null);
            Assert.That(rootCsi!.FullPath, Is.EqualTo(winDir).IgnoreCase);
        }
    }
}
