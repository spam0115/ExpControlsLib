using ExpControlsLib;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
                var controller = ShellController.Instance;
                var windowsDir = CShellItemFactory.Create(CSIDL.WINDOWS);
                Assert.IsNotNull(windowsDir, "Failed to create windowsDir");

                //first load
                windowsDir.LoadFolderContents(true, false);
                Assert.IsTrue(windowsDir.FileCount > 0, "First load should find items");
                CShellItem? firstNotepad = null;
                firstNotepad = windowsDir.Files?.Where(o => string.Equals(o.DisplayName, "notepad.exe", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                Assert.IsNotNull(firstNotepad, "notepad.exe should exist in C:\\Windows.");

                //second load
                windowsDir.LoadFolderContents(true, false);
                Assert.IsTrue(windowsDir.FileCount > 0, "Second load should find items");
                CShellItem? secondNotepad = null;
                secondNotepad = windowsDir.Files?.Where(o => string.Equals(o.DisplayName, "notepad.exe", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                Assert.IsNotNull(secondNotepad, "notepad.exe should exist in C:\\Windows.");

                //compared loads
                Assert.AreNotEqual(firstNotepad, secondNotepad, "First and second fetches of notepad.exe should be different.");

            });
        }

        [TestMethod]
        public async Task LoadFolderContents_FolderItemsAreBinnedCorrectly()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var myComputer = CShellItemFactory.Create(CSIDL.WINDOWS);

                myComputer.LoadFolderContents(true, true);

                Assert.IsNotNull(myComputer.DirectoriesList, "Failed to fetch directories list.");
                foreach (CShellItem item in myComputer.Directories)
                {
                    Assert.IsTrue(item.IsFolder, "All items in DirectoryList should be folders.");
                }

                Assert.IsNotNull(myComputer.FilesList, "Failed to fetch files list.");
                foreach (CShellItem item in myComputer.FilesList)
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
