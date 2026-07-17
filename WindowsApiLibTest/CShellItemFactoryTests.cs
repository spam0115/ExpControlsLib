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
            await Runner.EnqueueWork(() =>
            {
                CShellItemFactory.Initialize();
                // Assert that Instance is correctly initialized
                Assert.IsNotNull(CShellItemFactory.Instance, "Instance should not be null after initialization.");

                // Verify basic properties are populated
                Assert.IsNotNull(CShellItemFactory.DesktopCSI.PIDL, "DesktopPidl should not be null.");
                Assert.AreNotEqual(IntPtr.Zero, CShellItemFactory.DesktopCSI.PIDL, "DesktopPidl should not be zero.");

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
            await Runner.EnqueueWork(() =>
            {
                // Test creation from CSIDL
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);
                Assert.IsNotNull(myComputer, "Should be able to create CShellItem for My Computer (DRIVES).");
                Assert.AreNotEqual(IntPtr.Zero, myComputer.PIDL, "PIDL should not be zero.");

                var profile = CShellItemFactory.Create(CSIDL.PROFILE);
                Assert.IsNotNull(profile, "Should be able to create a CShellItem for the user profile.");
                Assert.IsTrue(profile.IsFolder, "The user profile item should be a folder.");
            });
        }

        [TestMethod]
        public async Task TestCreateCShItemFromPath()
        {
            await Runner.EnqueueWork(() =>
            {
                // Test creation from a common path
                string profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var csi = CShellItemFactory.Create(profilePath);

                Assert.IsNotNull(csi, $"Should be able to create CShellItem for {profilePath}.");
                Assert.AreEqual(profilePath, csi.FullPath, true, "FullPath should match the input path.");
                Assert.IsTrue(csi.IsFolder, "The user profile should be marked as a folder.");
            });
        }

        [TestMethod]
        public async Task TestCreateDesktopItem()
        {
            await Runner.EnqueueWork(() =>
            {
                // Test creation of Desktop item
                var desktop = CShellItemFactory.Create(CSIDL.DESKTOP);
                Assert.IsNotNull(desktop, "Desktop CShellItem should not be null.");
                Assert.IsTrue(desktop.IsFolder, "Desktop should be a folder.");
                Assert.IsNull(desktop.Parent, "Desktop should not have a parent.");
                
                // Verify it's the root
                Assert.IsTrue(CPidl.IsShellNamespaceRoot(desktop.PIDL), "Desktop PIDL should be the namespace root.");
            });
        }
    }
}
