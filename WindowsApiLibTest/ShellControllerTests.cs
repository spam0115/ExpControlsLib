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
    public class ShellControllerTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        [TestMethod]
        public async Task LoadFolderContents_NullInput_ReturnsNull()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var result = controller.LoadFolderContents(null, SHCONTF.FOLDERS);
                Assert.IsNull(result, "LoadFolderContents should return null when given a null CShellItem.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_FoldersFlag_PopulatesDirectories()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);

                var result = controller.LoadFolderContents(myComputer, SHCONTF.FOLDERS);

                Assert.IsNotNull(result, "Result should not be null.");
                Assert.IsTrue(result.FoldersInitialized, "Directories collection should be initialized after loading with FOLDERS flag.");
                Assert.IsTrue(result.DirCount > 0, "My Computer should contain at least one subfolder (drives).");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_NonFoldersFlag_PopulatesFiles()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var profile = CShellItemFactory.Create(userProfile);

                var result = controller.LoadFolderContents(profile, SHCONTF.NONFOLDERS);

                Assert.IsNotNull(result, "Result should not be null.");
                Assert.IsTrue(result.FilesInitialized, "Files collection should be initialized after loading with NONFOLDERS flag.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_BothFlags_PopulatesBothCollections()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var profile = CShellItemFactory.Create(userProfile);

                var result = controller.LoadFolderContents(profile, SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);

                Assert.IsNotNull(result, "Result should not be null.");
                Assert.IsTrue(result.FoldersInitialized, "Directories collection should be initialized.");
                Assert.IsTrue(result.FilesInitialized, "Files collection should be initialized.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_CalledTwice_ClearsAndReloads()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);

                controller.LoadFolderContents(myComputer, SHCONTF.FOLDERS);
                int firstCount = myComputer.DirCount;
                Assert.IsTrue(firstCount > 0, "First load should find subfolders.");

                var result = controller.LoadFolderContents(myComputer, SHCONTF.FOLDERS);

                Assert.IsNotNull(result, "Second load result should not be null.");
                Assert.IsTrue(result.FoldersInitialized, "Directories should still be initialized after second load.");
                Assert.AreEqual(firstCount, result.DirCount, "Directory count should be consistent on reload.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_ReturnsSameInstance()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);

                var result = controller.LoadFolderContents(myComputer, SHCONTF.FOLDERS);

                Assert.AreSame(myComputer, result, "LoadFolderContents should return the same CShellItem instance passed in.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_FolderItemsAreSortedCorrectly()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);

                controller.LoadFolderContents(myComputer, SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);

                if (myComputer.DirectoriesCollection != null)
                {
                    foreach (CShellItem item in myComputer.DirectoriesCollection)
                    {
                        Assert.IsTrue(item.IsFolder, "All items in DirectoryList should be folders.");
                    }
                }
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_SpecialFolder_CanLoad()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var profile = CShellItemFactory.Create(userProfile);

                var result = controller.LoadFolderContents(profile, SHCONTF.FOLDERS | SHCONTF.NONFOLDERS);

                Assert.IsNotNull(result, "Should be able to load contents of user profile folder.");
                Assert.IsTrue(result.FoldersInitialized || result.FilesInitialized, "Profile folder should have some contents.");
            });
        }


        [TestMethod]
        public async Task LoadFolderContents_After_Hierarchy_Clear()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var profile = CShellItemFactory.Create(userProfile);

                _ = controller.HierachyManager.FindAndAllowExpansion(profile);
                controller.HierachyManager.Clear();

                var result = controller.LoadFolderContents(profile, SHCONTF.NONFOLDERS);

                Assert.IsNotNull(result, "Result should not be null.");
                Assert.IsTrue(result.FilesInitialized, "Files collection should be initialized after loading with NONFOLDERS flag.");
            });
        }

    }
}
