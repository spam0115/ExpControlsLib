using ExpControlsLib;
using NUnit.Framework;
using System;
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
            var csi = CShellItemFactory.CreateCShItem((CSIDL)startDir);
            await expList.LoadDirectory(csi);

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
            var windowsCsi = CShellItemFactory.CreateCShItem(CSIDL.WINDOWS);
            await expList.LoadDirectory(windowsCsi);
            
            Assert.That(expList.CurrentPath, Is.EqualTo(windowsCsi.FullPath), "First folder should be loaded.");
            Assert.IsFalse(expList.CanGoBack, "CanGoBack should be false after first load.");
            Assert.IsFalse(expList.CanGoForward, "CanGoForward should be false after first load.");

            // 2. Load second folder
            var systemCsi = CShellItemFactory.CreateCShItem(CSIDL.SYSTEM);
            await expList.LoadDirectory(systemCsi);

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
    }
}
