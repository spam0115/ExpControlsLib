using ExpControlsLib;
using WindowsApiLib.Shell;
using WindowsApiLibTest;

namespace ExpControlsLibTest
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ThumbnailImageListManagerTests
    {
        private ExpList _expList;
        private ShellController _shellController;
        private Form _form;
        private string _testImagePath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string baseDir = AppContext.BaseDirectory;
            _testImagePath = Path.Combine(baseDir, "Resources", "Lena Forsén.jpg");
            if (!File.Exists(_testImagePath))
            {
                _testImagePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Resources", "Lena Forsén.jpg"));
            }
            Assert.IsTrue(File.Exists(_testImagePath), $"Test image not found at: {_testImagePath}");
        }

        [SetUp]
        public void SetUp()
        {
            _shellController = ShellController.Instance;
            _expList = new ExpList();
            _expList.Initialize(_shellController, new MockFileSystem());
            
            _form = new Form();
            _form.Controls.Add(_expList);
            _form.Show();
        }

        [TearDown]
        public void TearDown()
        {
            _form?.Dispose();
            _expList?.Dispose();
        }

        [Test]
        public void TestLRUEviction_FullCache()
        {
            // 1. Create manager with small capacity
            int capacity = 3;
            int size = 96;
            var manager = new ThumbnailImageListManager(_expList, size, capacity);
            manager.SetExpListLargeImageList(size);

            // 2. Prepare CShellItems and Dummy Bitmaps
            // We need 4 different items to trigger eviction
            var items = new List<CShellItem>();
            var tempFiles = new List<string>();

            try
            {
                for (int i = 0; i < capacity + 1; i++)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"test_thumb_evict_{i}.jpg");
                    File.Copy(_testImagePath, tempFile, true);
                    tempFiles.Add(tempFile);
                    items.Add(CShellItemFactory.Create(tempFile));
                }

                // 3. Add 3 thumbnails
                var indices = new List<int>();
                for (int i = 0; i < capacity; i++)
                {
                    using var bmp = new Bitmap(size, size);
                    var reqArgs = new ThumbnailReadyEventArgs(items[i], bmp, size);
                    int idx = manager.AddThumbnail(reqArgs, bmp);
                    indices.Add(idx);
                    Assert.That(idx, Is.AtLeast(0), $"Item {i} should be added.");
                    Assert.That(items[i].ImageIndex, Is.EqualTo(idx));
                }

                // Verify they are in cache
                for (int i = 0; i < capacity; i++)
                {
                    Assert.That(manager.GetThumbnailIndex(items[i], size), Is.EqualTo(indices[i]));
                }

                // 4. Add the 4th thumbnail (triggers eviction of items[0])
                using var bmp4 = new Bitmap(size, size);
                var reqArgs4 = new ThumbnailReadyEventArgs(items[capacity], bmp4, size);
                int idx4 = manager.AddThumbnail(reqArgs4, bmp4);

                // 5. Verify Eviction
                // items[0] should be evicted
                Assert.That(manager.GetThumbnailIndex(items[0], size), Is.EqualTo(-1), "Item 0 should be evicted.");
                Assert.That(items[0].ImageIndex, Is.EqualTo(-1), "Item 0 ImageIndex should be reset.");

                // items[3] should be in cache
                Assert.That(manager.GetThumbnailIndex(items[capacity], size), Is.EqualTo(idx4));
                Assert.That(items[capacity].ImageIndex, Is.EqualTo(idx4));

                // Verify index reuse
                Assert.That(idx4, Is.EqualTo(indices[0]), "The index of evicted Item 0 should be reused for Item 3.");

                // items[1] and items[2] should still be in cache
                Assert.That(manager.GetThumbnailIndex(items[1], size), Is.EqualTo(indices[1]));
                Assert.That(manager.GetThumbnailIndex(items[2], size), Is.EqualTo(indices[2]));
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
        public void TestLRUEviction_Promotion()
        {
            // 1. Create manager with small capacity
            int capacity = 3;
            int size = 96;
            var manager = new ThumbnailImageListManager(_expList, size, capacity);
            manager.SetExpListLargeImageList(size);

            var items = new List<CShellItem>();
            var tempFiles = new List<string>();

            try
            {
                for (int i = 0; i < capacity + 1; i++)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"test_thumb_promo_{i}.jpg");
                    File.Copy(_testImagePath, tempFile, true);
                    tempFiles.Add(tempFile);
                    items.Add(CShellItemFactory.Create(tempFile));
                }

                // Add 3 thumbnails (0, 1, 2)
                var indices = new List<int>();
                for (int i = 0; i < capacity; i++)
                {
                    using var bmp = new Bitmap(size, size);
                    indices.Add(manager.AddThumbnail(new ThumbnailReadyEventArgs(items[i], bmp, size), bmp));
                }

                // Cache order is now [0, 1, 2] (oldest to newest)

                // Access items[0] to promote it
                int idx0 = manager.GetThumbnailIndex(items[0], size);
                Assert.That(idx0, Is.EqualTo(indices[0]));

                // Cache order should now be [1, 2, 0]

                // Add item 3 (should evict items[1], NOT items[0])
                using var bmp3 = new Bitmap(size, size);
                manager.AddThumbnail(new ThumbnailReadyEventArgs(items[3], bmp3, size), bmp3);

                // items[1] should be evicted
                Assert.That(manager.GetThumbnailIndex(items[1], size), Is.EqualTo(-1), "Item 1 should be evicted.");
                
                // items[0] should still be in cache
                Assert.That(manager.GetThumbnailIndex(items[0], size), Is.EqualTo(idx0), "Item 0 should still be in cache.");
                
                // items[2] should still be in cache
                Assert.That(manager.GetThumbnailIndex(items[2], size), Is.EqualTo(indices[2]));
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
        public void TestAddThumbnail_ReplacesExisting()
        {
            int capacity = 5;
            int size = 96;
            var manager = new ThumbnailImageListManager(_expList, size, capacity);
            manager.SetExpListLargeImageList(size);

            string tempFile = Path.Combine(Path.GetTempPath(), "test_thumb_replace.jpg");
            File.Copy(_testImagePath, tempFile, true);
            try
            {
                var csi = CShellItemFactory.Create(tempFile);

                // 1. Add first time
                using var bmp1 = new Bitmap(size, size);
                bmp1.SetPixel(0, 0, Color.Red);
                int idx1 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi, bmp1, size), bmp1);
                
                var imageList = manager.GetOrCreateImageList(size);
                int countBefore = imageList.Images.Count;

                // 2. Add second time for same item
                using var bmp2 = new Bitmap(size, size);
                bmp2.SetPixel(0, 0, Color.Blue);
                int idx2 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi, bmp2, size), bmp2);

                Assert.That(idx2, Is.EqualTo(idx1), "Should reuse the same index when replacing a thumbnail.");
                Assert.That(csi.ImageIndex, Is.EqualTo(idx1));
                Assert.That(imageList.Images.Count, Is.EqualTo(countBefore), "ImageList count should NOT increase when replacing an existing thumbnail.");
                
                // Verify it's still in cache
                Assert.That(manager.GetThumbnailIndex(csi, size), Is.EqualTo(idx1));
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        [Test]
        public void TestAddThumbnail_Success()
        {
            // Verify that adding a valid bitmap returns a valid index and updates the CShellItem.ImageIndex.
            int size = 64;
            var manager = new ThumbnailImageListManager(_expList, size);
            manager.SetExpListLargeImageList(size);

            var csi = CShellItemFactory.Create(_testImagePath);
            using var bmp = new Bitmap(size, size);
            
            var reqArgs = new ThumbnailReadyEventArgs(csi, bmp, size);
            int index = manager.AddThumbnail(reqArgs, bmp);

            Assert.That(index, Is.AtLeast(0), "Should return a valid non-negative index.");
            Assert.That(csi.ImageIndex, Is.EqualTo(index), "CShellItem.ImageIndex should be updated to the returned index.");
            
            // Verify it's in the ImageList
            var imageList = manager.GetOrCreateImageList(size);
            Assert.That(imageList.Images.Count, Is.GreaterThan(index), "ImageList should contain the new image.");
            
            // Verify cache retrieval
            Assert.That(manager.GetThumbnailIndex(csi, size), Is.EqualTo(index), "Should be able to retrieve index from cache.");
        }

        [Test]
        public void TestThumbnailCaching_IsolationBySize()
        {
            // Verify that thumbnails of different sizes for the same file are cached independently.
            var csi = CShellItemFactory.Create(_testImagePath);
            
            int size1 = 64;
            int size2 = 128;

            var manager = new ThumbnailImageListManager(_expList, size1);
            manager.SetExpListLargeImageList(size1);
            using var bmp1 = new Bitmap(size1, size1);
            int idx1 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi, bmp1, size1), bmp1);

            manager.SetExpListLargeImageList(size2);
            using var bmp2 = new Bitmap(size2, size2);
            int idx2 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi, bmp2, size2), bmp2);

            // They should be in their respective size caches
            Assert.That(manager.GetThumbnailIndex(csi, size1), Is.EqualTo(idx1));
            Assert.That(manager.GetThumbnailIndex(csi, size2), Is.EqualTo(idx2));
            
            // ImageIndex on CSI will reflect the last one added
            Assert.That(csi.ImageIndex, Is.EqualTo(idx2));
        }

        [Test]
        public void TestAddThumbnail_MultipleFiles()
        {
            // Verify that multiple files (using different source images) are handled correctly.
            int size = 96;
            var manager = new ThumbnailImageListManager(_expList, size);
            manager.SetExpListLargeImageList(size);

            string path1 = _testImagePath; // Lena
            string path2 = Path.Combine(AppContext.BaseDirectory, "Resources", "fu.jpg");
            
            Assert.IsTrue(File.Exists(path2), $"fu.jpg not found at: {path2}");

            var csi1 = CShellItemFactory.Create(path1);
            var csi2 = CShellItemFactory.Create(path2);

            using var bmp1 = new Bitmap(size, size);
            using var bmp2 = new Bitmap(size, size);

            int idx1 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi1, bmp1, size), bmp1);
            int idx2 = manager.AddThumbnail(new ThumbnailReadyEventArgs(csi2, bmp2, size), bmp2);

            Assert.That(idx1, Is.Not.EqualTo(idx2), "Different files should have different thumbnail indices.");
            Assert.That(manager.GetThumbnailIndex(csi1, size), Is.EqualTo(idx1));
            Assert.That(manager.GetThumbnailIndex(csi2, size), Is.EqualTo(idx2));
        }

        [Test]
        public void TestSlotReuse_Efficiency()
        {
            // Verify that the ImageList does not grow beyond the capacity when thumbnails are evicted.
            int capacity = 5;
            int size = 64;
            var manager = new ThumbnailImageListManager(_expList, size, capacity);
            manager.SetExpListLargeImageList(size);
            var imageList = manager.GetOrCreateImageList(size);

            int totalItemsToAdd = capacity * 2;
            var tempFiles = new List<string>();

            try
            {
                for (int i = 0; i < totalItemsToAdd; i++)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"test_efficiency_{i}.jpg");
                    File.Copy(_testImagePath, tempFile, true);
                    tempFiles.Add(tempFile);

                    var csi = CShellItemFactory.Create(tempFile);
                    using var bmp = new Bitmap(size, size);
                    manager.AddThumbnail(new ThumbnailReadyEventArgs(csi, bmp, size), bmp);

                    Assert.That(imageList.Images.Count, Is.EqualTo(capacity),
                        $"ImageList should remain preallocated at capacity. Index: {i}");
                }
            }
            finally
            {
                foreach (var file in tempFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }
}
