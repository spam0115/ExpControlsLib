using ExpControlsLib;
using NUnit.Framework;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WindowsApiLib.Shell;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ThumbnailProviderTests
    {
        private string _testImagePath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Find the test image in the Resources folder
            string baseDir = AppContext.BaseDirectory;
            // Depending on how tests are run, we might need to look up a few levels
            _testImagePath = Path.Combine(baseDir, "Resources", "Lena Forsén.jpg");
            
            if (!File.Exists(_testImagePath))
            {
                // Try to find it in the project structure if not in output dir
                _testImagePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Resources", "Lena Forsén.jpg"));
            }

            if (!File.Exists(_testImagePath))
            {
                 // Fallback for different build structures
                _testImagePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "ExpControlsLibTest", "Resources", "Lena Forsén.jpg"));
            }

            Assert.IsTrue(File.Exists(_testImagePath), $"Test image not found at: {_testImagePath}");
        }

        [Test]
        public void TestGetThumbnailFromOS_FilePath()
        {
            using var provider = new ThumbnailProvider();
            int size = 96;
            using var thumb = provider.GetThumbnailFromOS(_testImagePath, size) as Bitmap;

            Assert.IsNotNull(thumb, "Thumbnail should not be null.");
            Assert.AreEqual(size, thumb.Width);
            Assert.AreEqual(size, thumb.Height);
        }

        [Test]
        public void TestGetThumbnailFromOS_Pidl()
        {
            using var provider = new ThumbnailProvider();
            var csi = CShellItemFactory.CreateCShItem(_testImagePath);
            Assert.IsNotNull(csi, "CShellItem should be created.");

            int size = 128;
            using var thumb = provider.GetThumbnailFromOS(csi.PIDL, size);

            Assert.IsNotNull(thumb, "Thumbnail should not be null.");
            Assert.AreEqual(size, thumb.Width);
            Assert.AreEqual(size, thumb.Height);
        }

        [Test]
        public async Task TestEnqueueThumbnailRequest()
        {
            using var provider = new ThumbnailProvider();
            var csi = CShellItemFactory.CreateCShItem(_testImagePath);
            int size = 64;
            var tcs = new TaskCompletionSource<Image>();

            provider.ThumbnailReady += (s, e) =>
            {
                if (e.Item.FullPath == _testImagePath && e.Size == size)
                {
                    tcs.TrySetResult(e.Thumbnail);
                }
            };

            var reqArgs = new ThumbnailRequestArgs { Item = csi, Size = size, Index = -1 };
            provider.EnqueueThumbnailRequest(size, reqArgs);

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsNotNull(result, "Thumbnail should be returned via event.");
            Assert.AreEqual(size, result.Width);
            Assert.AreEqual(size, result.Height);
            
            result.Dispose();
        }

        [Test]
        public void TestThumbnailCaching()
        {
            using var provider = new ThumbnailProvider();
            int size = 96;

            // Initially not in cache
            bool found = provider.TryGetCachedThumbnail(_testImagePath, size, out var thumb);
            Assert.IsFalse(found, "Should not be in cache initially.");

            // Generate it (this populates the cache in the background usually, 
            // but GetThumbnailFromOS in this provider doesn't seem to populate cache? 
            // Wait, looking at ThumbnailProvider.cs... 
            // GenerateThumbnailAndNotify populates it. GetThumbnailFromOS does NOT.
            // Let's use EnqueueThumbnailRequest to populate cache.)
            
            var csi = CShellItemFactory.CreateCShItem(_testImagePath);
            var reqArgs = new ThumbnailRequestArgs { Item = csi, Size = size, Index = -1 };
            
            // We need to wait for it to be processed
            var tcs = new TaskCompletionSource<bool>();
            provider.ThumbnailReady += (s, e) => tcs.TrySetResult(true);
            
            provider.EnqueueThumbnailRequest(size, reqArgs);
            tcs.Task.Wait(5000);

            found = provider.TryGetCachedThumbnail(_testImagePath, size, out thumb);
            Assert.IsTrue(found, "Should be in cache after generation.");
            Assert.IsNotNull(thumb);
            thumb.Dispose();

            // Different size should not be found
            found = provider.TryGetCachedThumbnail(_testImagePath, size + 1, out thumb);
            Assert.IsFalse(found, "Different size should not be in cache.");
        }

        [Test]
        public void TestClearCache()
        {
            using var provider = new ThumbnailProvider();
            int size = 96;
            var csi = CShellItemFactory.CreateCShItem(_testImagePath);
            var reqArgs = new ThumbnailRequestArgs { Item = csi, Size = size, Index = -1 };
            
            var tcs = new TaskCompletionSource<bool>();
            provider.ThumbnailReady += (s, e) => tcs.TrySetResult(true);
            provider.EnqueueThumbnailRequest(size, reqArgs);
            tcs.Task.Wait(5000);

            Assert.IsTrue(provider.TryGetCachedThumbnail(_testImagePath, size, out _), "Should be in cache.");

            provider.ClearCache();

            Assert.IsFalse(provider.TryGetCachedThumbnail(_testImagePath, size, out _), "Cache should be empty after ClearCache.");
        }

        [Test]
        public void TestGetThumbnail_InvalidPath()
        {
            using var provider = new ThumbnailProvider();
            string invalidPath = @"C:\ThisFileDoesNotExist_12345.jpg";
            var thumb = provider.GetThumbnailFromOS(invalidPath, 96);

            Assert.IsNull(thumb, "Thumbnail should be null for invalid path.");
        }
    }
}
