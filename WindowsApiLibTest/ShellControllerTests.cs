using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.IO;
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
                string tempDir = Path.Combine(Path.GetTempPath(), "ShellControllerCache_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "known-file.txt"), "test");
                try
                {
                    var folder = CShellItemFactory.Create(tempDir);
                    Assert.IsNotNull(folder, "The temporary folder item should not be null.");

                    controller.EnsureChildrenPopulatedAndRecent(folder, SHCONTF.NONFOLDERS);
                    var firstItem = folder.Files.FirstOrDefault(o => string.Equals(o.DisplayName, "known-file.txt", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(firstItem, "The known file should exist after the first load.");

                    controller.EnsureChildrenPopulatedAndRecent(folder, SHCONTF.NONFOLDERS);
                    var secondItem = folder.Files.FirstOrDefault(o => string.Equals(o.DisplayName, "known-file.txt", StringComparison.OrdinalIgnoreCase));
                    Assert.AreSame(firstItem, secondItem, "Within the timeout, cached item references should be reused.");

                    Thread.Sleep(ShellController.FolderTimeout * 1000);
                    controller.EnsureChildrenPopulatedAndRecent(folder, SHCONTF.NONFOLDERS);
                    var thirdItem = folder.Files.FirstOrDefault(o => string.Equals(o.DisplayName, "known-file.txt", StringComparison.OrdinalIgnoreCase));

                    Assert.IsNotNull(thirdItem, "The known file should still exist after cache expiry.");
                    Assert.AreNotSame(firstItem, thirdItem, "After cache expiry, loading should produce new item references.");
                }
                finally
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
            });
        }

    }
}
