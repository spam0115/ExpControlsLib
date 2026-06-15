using ExpControlsLib;
using NUnit.Framework;
using System;
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
    }
}
