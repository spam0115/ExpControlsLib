using ExpControlsLib;
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
            var csi = CShellItemFactory.Create(_testImagePath);
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
            var csi = CShellItemFactory.Create(_testImagePath);
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
            
            var csi = CShellItemFactory.Create(_testImagePath);
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
            var csi = CShellItemFactory.Create(_testImagePath);
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

        [Test]
        public void TestCancelPendingRequests()
        {
            using var provider = new ThumbnailProvider();
            int size = 96;
            int requestCount = 20;
            int eventCount = 0;
            var countdown = new CountdownEvent(requestCount);

            provider.ThumbnailReady += (s, e) =>
            {
                Interlocked.Increment(ref eventCount);
                countdown.Signal();
            };

            var tempFiles = new List<string>();
            try
            {
                for (int i = 0; i < requestCount; i++)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"test_cancel_{i}.jpg");
                    File.Copy(_testImagePath, tempFile, true);
                    tempFiles.Add(tempFile);

                    var csi = CShellItemFactory.Create(tempFile);
                    var reqArgs = new ThumbnailRequestArgs { Item = csi, Size = size, Index = i };
                    provider.EnqueueThumbnailRequest(size, reqArgs);
                }

                // Immediately cancel
                provider.CancelPendingRequests();

                // Wait a bit to see how many events fire. 
                // We expect far fewer than requestCount because of cancellation.
                // However, some might have already started processing.
                countdown.Wait(2000);

                Assert.Less(eventCount, requestCount, "Fewer events should fire than requests made due to cancellation.");
                Console.WriteLine($"Requests: {requestCount}, Events fired: {eventCount}");
            }
            finally
            {
                foreach (var file in tempFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }

        [Test]
        public void TestDuplicateRequestSuppression()
        {
            using var provider = new ThumbnailProvider();
            int size = 96;
            int eventCount = 0;
            var csi = CShellItemFactory.Create(_testImagePath);

            provider.ThumbnailReady += (s, e) =>
            {
                Interlocked.Increment(ref eventCount);
            };

            // Enqueue the same request multiple times
            for (int i = 0; i < 5; i++)
            {
                var reqArgs = new ThumbnailRequestArgs { Item = csi, Size = size, Index = -1 };
                provider.EnqueueThumbnailRequest(size, reqArgs);
            }

            // Wait for processing
            Thread.Sleep(2000);

            // It should only fire once (if the first one is still in _activeTasks when others are enqueued)
            // Or if it finished so fast that it's no longer in _activeTasks, it might fire more, 
            // but for a single file in rapid succession, it should be suppressed.
            Assert.AreEqual(1, eventCount, "Duplicate requests for the same item/size should be suppressed.");
        }
    }
}
