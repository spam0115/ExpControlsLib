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

        /// <summary>
        /// test the conditional loading logic which considers age time out for items before loading.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task EnsureChildrenPopulated_NonFolders_CachesWithinTimeout()
        {
            await Runner.EnqueueWork(() =>
            {
                var controller = ShellController.Instance;
                var windowsDir = CShellItemFactory.Create(CSIDL.WINDOWS);
                Assert.IsNotNull(windowsDir, "windowsDir should not be null.");

                controller.EnsureChildrenPopulatedAndRecent(windowsDir, SHCONTF.NONFOLDERS);

                Assert.IsTrue(windowsDir.FilesInitialized, "Files should be initialized after first call.");
                Assert.IsTrue(windowsDir.Files?.Count > 0, "Windows directory should contain some items.");

                CShellItem? firstNotepad = null;
                firstNotepad = windowsDir.Files.Where(o => string.Equals(o.DisplayName, "notepad.exe", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                Assert.IsNotNull(firstNotepad, "notepad.exe should exist in C:\\Windows.");

                controller.EnsureChildrenPopulatedAndRecent(windowsDir, SHCONTF.NONFOLDERS);

                CShellItem? secondNotepad = null;
                secondNotepad = windowsDir.Files.Where(o => string.Equals(o.DisplayName, "notepad.exe", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                Assert.IsNotNull(secondNotepad, "notepad.exe should still exist after second call.");
                Assert.AreSame(firstNotepad, secondNotepad, "Within timeout, EnsureChildrenPopulated should return the same cached item references.");

                Thread.Sleep(ShellController.FolderTimeout * 1000);

                controller.EnsureChildrenPopulatedAndRecent(windowsDir, SHCONTF.NONFOLDERS);

                CShellItem? thirdNotepad = null;
                thirdNotepad = windowsDir.Files.Where(o => string.Equals(o.DisplayName, "notepad.exe", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                Assert.IsNotNull(thirdNotepad, "notepad.exe should still exist after third call.");
                Assert.AreNotSame(firstNotepad, thirdNotepad, "After cache expiry, EnsureChildrenPopulated should reload and produce new item references.");
            });
        }

    }
}
