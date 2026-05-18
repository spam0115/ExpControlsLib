using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.Threading.Tasks;
using ExpControlsLib;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemFactoryTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        [TestMethod]
        public async Task TestInitializeAndProperties()
        {
            await Runner.InvokeAsync(() =>
            {
                // Assert that Instance is correctly initialized
                Assert.IsNotNull(CShellItemFactory.Instance, "Instance should not be null after initialization.");

                // Verify basic properties are populated
                Assert.IsNotNull(CShellItemFactory.DesktopPidl, "DesktopPidl should not be null.");
                Assert.AreNotEqual(IntPtr.Zero, CShellItemFactory.DesktopPidl, "DesktopPidl should not be zero.");

                Assert.IsNotNull(CShellItemFactory.EmptyPidl, "EmptyPidl should not be null.");
                Assert.AreNotEqual(IntPtr.Zero, CShellItemFactory.EmptyPidl, "EmptyPidl should not be zero.");

                Assert.IsNotNull(CShellItemFactory.SystemName, "SystemName should not be null.");
                Assert.IsFalse(string.IsNullOrEmpty(CShellItemFactory.SystemName), "SystemName should not be empty.");

                Assert.IsNotNull(CShellItemFactory.RecycleBin, "RecycleBin should not be null.");
                Assert.IsNotNull(CShellItemFactory.DeskTopDirectory, "DeskTopDirectory should not be null.");

                Assert.IsNotNull(CShellItemFactory.StrSystemFolder, "StrSystemFolder should not be null.");
                Assert.IsNotNull(CShellItemFactory.StrMyComputer, "StrMyComputer should not be null.");
            });
        }

        [TestMethod]
        public async Task TestCreateCShItemFromSpecialFolder()
        {
            await Runner.InvokeAsync(() =>
            {
                // Test creation from CSIDL
                var myComputer = CShellItemFactory.CreateCShItem(CSIDL.DRIVES);
                Assert.IsNotNull(myComputer, "Should be able to create CShellItem for My Computer (DRIVES).");
                Assert.AreNotEqual(IntPtr.Zero, myComputer.PIDL, "PIDL should not be zero.");

                var windows = CShellItemFactory.CreateCShItem(CSIDL.WINDOWS);
                Assert.IsNotNull(windows, "Should be able to create CShellItem for Windows folder.");
                Assert.IsTrue(windows.IsFolder, "Windows item should be a folder.");
            });
        }

        [TestMethod]
        public async Task TestCreateCShItemFromPath()
        {
            await Runner.InvokeAsync(() =>
            {
                // Test creation from a common path
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var csi = CShellItemFactory.CreateCShItem(windir);

                Assert.IsNotNull(csi, $"Should be able to create CShellItem for {windir}.");
                Assert.AreEqual(windir, csi.FullPath, true, "FullPath should match the input path.");
                Assert.IsTrue(csi.IsFolder, "Windows directory should be marked as a folder.");
            });
        }

        [TestMethod]
        public async Task TestCreateDesktopItem()
        {
            await Runner.InvokeAsync(() =>
            {
                // Test creation of Desktop item
                var desktop = CShellItemFactory.CreateCShItem(CSIDL.DESKTOP);
                Assert.IsNotNull(desktop, "Desktop CShellItem should not be null.");
                Assert.IsTrue(desktop.IsFolder, "Desktop should be a folder.");
                Assert.IsNull(desktop.Parent, "Desktop should not have a parent.");
                
                // Verify it's the root
                Assert.IsTrue(CPidl.IsShellNamespaceRoot(desktop.PIDL), "Desktop PIDL should be the namespace root.");
            });
        }
    }
}
