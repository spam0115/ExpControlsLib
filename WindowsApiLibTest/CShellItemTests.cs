using ExpControlsLib;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        [TestMethod]
        public async Task LoadFolderContents_FoldersFlag_PopulatesDirectories()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.DRIVES);
                Assert.IsNotNull(myComputer, "myComputer should not be null.");

                myComputer.LoadFolderContents(false, true);

                Assert.IsTrue(myComputer.DirectoriesInitialized, "Directories collection should be initialized after loading with FOLDERS flag.");
                Assert.IsTrue(myComputer.DirCount > 0, "My Computer should contain at least one subfolder (drives).");
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
                Assert.IsNotNull(profile, "profile should not be null.");

                profile.LoadFolderContents(true, false);

                Assert.IsTrue(profile.FilesInitialized, "Files collection should be initialized after loading with NONFOLDERS flag.");
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
                Assert.IsNotNull(profile, "profile should not be null.");

                profile.LoadFolderContents(true, true);

                Assert.IsTrue(profile.DirectoriesInitialized, "Directories collection should be initialized.");
                Assert.IsTrue(profile.FilesInitialized, "Files collection should be initialized.");
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_CalledTwice_ClearsAndReloads()
        {
            await Runner.EnqueueWork(() =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ShellItemReload_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "known-file.txt"), "test");
                try
                {
                    var folder = CShellItemFactory.Create(tempDir);
                    Assert.IsNotNull(folder, "Failed to create the temporary folder item.");

                    folder.LoadFolderContents(true, false);
                    var firstItem = folder.Files?.FirstOrDefault(o => string.Equals(o.DisplayName, "known-file.txt", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(firstItem, "The first load should find the known file.");

                    folder.LoadFolderContents(true, false);
                    var secondItem = folder.Files?.FirstOrDefault(o => string.Equals(o.DisplayName, "known-file.txt", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(secondItem, "The second load should find the known file.");

                    Assert.AreNotEqual(firstItem, secondItem, "Reloading should produce a new shell-item instance.");
                }
                finally
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
            });
        }

        [TestMethod]
        public async Task LoadFolderContents_FolderItemsAreBinnedCorrectly()
        {
            await Runner.EnqueueWork(() =>
            {
                var profile = CShellItemFactory.Create(CSIDL.PROFILE);

                profile.LoadFolderContents(true, true);

                Assert.IsNotNull(profile.DirectoriesList, "Failed to fetch directories list.");
                foreach (CShellItem item in profile.Directories)
                {
                    Assert.IsTrue(item.IsFolder, "All items in DirectoryList should be folders.");
                }

                Assert.IsNotNull(profile.FilesList, "Failed to fetch files list.");
                foreach (CShellItem item in profile.FilesList)
                {
                    Assert.IsFalse(item.IsFolder, "All items in FilesList should be files.");
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
                Assert.IsNotNull(profile, "failed to create profile folder shell item.");

                profile.LoadFolderContents(true, true);

                Assert.IsTrue(profile.DirectoriesInitialized || profile.FilesInitialized, "Profile folder should have some contents.");
            });
        }


    }
}
