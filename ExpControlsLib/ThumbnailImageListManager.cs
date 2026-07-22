using C5;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static System.Windows.Forms.ListView;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    /// <summary>
    /// Manages ImageLists for thumbnail display modes in the ListView control.
    /// Creates and maintains separate ImageLists for different thumbnail sizes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ThumbnailImageListManager : IDisposable
    {
        private readonly ConcurrentDictionary<int, ImageList> _imageLists = new();
        private readonly ThumbnailProvider _thumbnailProvider;
        private readonly ExpList _expList;
        private int _activeSize;
        private int _generation = 0;
        
        private readonly HashedLinkedList<string> _lruKeys = new();
        private readonly System.Collections.Generic.Dictionary<string, ThumbnailSlot> _slotByKey = new();
        private readonly int _capacity;

        private class ThumbnailSlot
        {
            public int Index;
            public CShellItem? Item;
            public string Key;

            public ThumbnailSlot(int index, CShellItem? item, string key)
            {
                Index = index;
                Item = item;
                Key = key;
            }
        }

        public event EventHandler<ThumbnailReadyEventArgs>? ThumbnailReady;

        /// <summary>
        /// Initializes a new instance of the ThumbnailImageListManager class.
        /// </summary>
        /// <remarks>
        /// The limit for capacity is ~1658 for 256px thumbnails.  Viewing more images than this causes blank images to be drawn.
        /// The windows image list is stored within a contiguous section of non-virtual memory in a special region of memory
        /// which is also accessible by the gpu.
        /// The list is stored as a horizontal strip (should be vertical but isn't).  Many gpus have a texture size limit of 16384px
        /// due to this being the limit for DirectX 12.  Opengl supports 32768.
        /// The Nvidia 1080 has a limit of 131,072 × 65,536 but only inside cuda code.  Non-cuda code is limited to 16384px.
        /// Although things still kinda work even if you exceed this limit, you will eventually start displaying blank images
        /// once you view more than 1658 thumbnails at 256px.  The limit is higher for smaller thumbnails in a non-linear relationship..
        /// </remarks>
        /// <param name="expList"></param>
        /// <param name="capacity">The capacity of the list
        /// 
        /// </param>
        public ThumbnailImageListManager(ExpList expList, int size, int capacity = -1)
        {
            _activeSize = size;
            if (capacity == -1)
            {
                capacity = 16384/this._activeSize*2; // default capacity based on 16k texture limit 
            }

            _expList = expList;
            _capacity = capacity;
            _thumbnailProvider = new ThumbnailProvider();
            _thumbnailProvider.ThumbnailReady += OnThumbnailReady;
        }

        /// <summary>
        /// Gets the thumbnail index for a given CShellItem and requested size. 
        /// If the thumbnail is not yet available, it initiates a request and returns -1.
        /// </summary>
        /// <param name="csi"></param>
        /// <param name="requestedSize"></param>
        /// <returns></returns>
        public int GetThumbnailIndex(CShellItem csi, int requestedSize)
        {
            if (_slotByKey.TryGetValue($"{csi.FullPath}|{requestedSize}", out var slot))
            {
                // Update LRU on access
                string key = slot.Key;
                _lruKeys.Remove(key);
                _lruKeys.Add(key);
                return slot.Index;
            }
            else
            {
                RequestThumbnail(csi, requestedSize);
                return -1;
            }
        }

        public void SetImageListForSize(int thumbnailSize)
        {
            _activeSize = thumbnailSize;

            var imageList = GetImageList(thumbnailSize);

            _expList._listView.BeginUpdate();
            try
            {
                _expList._listView.LargeImageList = imageList;
                if (!_expList._listView.VirtualMode)
                {
                    foreach (ListViewItem item in _expList._listView.Items)
                    {
                        if (item is null) continue;
                        item.ImageIndex = -1;
                    }
                }
            }
            finally
            {
                _expList._listView.EndUpdate();
            }
        }

        /// <summary>
        /// Gets or creates an ImageList for the specified thumbnail size
        /// </summary>
        public ImageList GetImageList(int thumbnailSize)
        {
            if (_imageLists.TryGetValue(thumbnailSize, out var imageList))
            {
                return imageList;
            }

            Debug.WriteLine("Creating new image list for thumbnails...");

            imageList = new ImageList
            {
                ImageSize = new Size(thumbnailSize, thumbnailSize),
                ColorDepth = ColorDepth.Depth32Bit
            };
            _imageLists[thumbnailSize] = imageList;
            return imageList;
        }

        /// <summary>
        /// Requests a thumbnail for a file and updates the ListView when ready
        /// </summary>
        public void RequestThumbnail(CShellItem csi, int thumbnailSize, int itemIndex = -1)
        {
#if DEBUG
            Console.WriteLine("Requesting thumbnail: " + csi.Text);
#endif

            var reqObj = new ThumbnailRequestArgs
            {
                Generation = _generation,
                Item = csi,
                Size = thumbnailSize,
                Index = itemIndex
            };

            _thumbnailProvider.EnqueueThumbnailRequest(thumbnailSize, reqObj);
        }

        /// <summary>
        /// Requests a thumbnail for a file and updates the ListView when ready
        /// </summary>
        public void RequestThumbnailFromCache(CShellItem csi, int thumbnailSize, int itemIndex = -1)
        {
#if DEBUG
            Console.WriteLine("Requesting thumbnail: " + csi.Text);
#endif
            if (csi == null) return;

            string key = CreateKey(csi.FullPath, thumbnailSize);
            if (_slotByKey.TryGetValue(key, out var slot))
            {
                // Update LRU
                _lruKeys.Remove(key);
                _lruKeys.Add(key);
                csi.ImageIndex = slot.Index;
            }
            else
            {
                RequestThumbnail(csi, thumbnailSize, itemIndex);
            }
        }

        /// <summary>
        /// Gets a cached thumbnail index or queues a request when the thumbnail is not cached.
        /// Unlike <see cref="GetThumbnailIndex"/>, this method preserves the caller's item index
        /// when a request must be queued.
        /// </summary>
        public int EnsureThumbnail(CShellItem csi, int thumbnailSize, int itemIndex = -1)
        {
            if (csi == null) return -1;

            string key = CreateKey(csi.FullPath, thumbnailSize);
            if (_slotByKey.TryGetValue(key, out var slot))
            {
                _lruKeys.Remove(key);
                _lruKeys.Add(key);
                csi.ImageIndex = slot.Index;
                return slot.Index;
            }

            RequestThumbnail(csi, thumbnailSize, itemIndex);
            return -1;
        }

        internal void CancelPendingRequests()
        {
            _thumbnailProvider.CancelPendingRequests();
        }


        /// <summary>
        /// Handles thumbnail ready events and updates the ListView.
        /// Image manipulation is done on the background thread, while UI updates are marshalled to the UI thread.
        /// 
        /// </summary>
        /// <remarks>
        /// It is essential to remember that since thumbnail requests are lazy loaded, it is possible for the 
        /// results to be invalid due to file deletions or navigating to different folders.  Cases like these
        /// must be detected and thumbnail results should be ignored.
        /// </remarks>
        private void OnThumbnailReady(object? sender, ThumbnailReadyEventArgs e)
        {
            if (ThumbnailReady != null)
                ThumbnailReady(this, e);
            else
                e.Thumbnail?.Dispose();
        }

        /// <summary>
        /// Adds a thumbnail to the internal ImageList and updates the item's ImageIndex.
        /// This method must be called on the UI thread as it accesses the ImageList.
        /// </summary>
        /// <param name="reqArgs">The thumbnail ready arguments.</param>
        /// <param name="thumbnail">The square thumbnail bitmap.</param>
        /// <returns>The index of the thumbnail in the ImageList, or -1 if it could not be added.</returns>
        public int AddThumbnail(ThumbnailReadyEventArgs reqArgs, Bitmap thumbnail)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ThumbnailImageListManager: AddThumbnail begin");

            if (thumbnail == null)
            {
                if (reqArgs.Item != null) reqArgs.Item.ImageIndex = -1;
                return -1;
            }

            if (reqArgs is null || reqArgs.Item is null)
            {
                return -1;
            }

            ImageList imageList = null;
            try
            {
                imageList = GetImageList(_activeSize);
                if (_expList._listView.LargeImageList != imageList)
                    _expList._listView.LargeImageList = imageList;

                string key = CreateKey(reqArgs.Item.FullPath, reqArgs.Size);
                int index = -1;

                if (_slotByKey.TryGetValue(key, out var existingSlot)) //replace existing thumbnail
                {
                    index = existingSlot.Index;
                    _lruKeys.Remove(key);
                    _lruKeys.Add(key);

                    lock(imageList)
                    {
                        imageList.Images[index] = thumbnail;
                    }
                }
                else //new thumbnail
                {
                    bool reused = false;
                    if (_lruKeys.Count >= _capacity)
                    {
                        // Evict the least-recently-used slot that is NOT currently visible.
                        // Skipping visible items prevents the user from seeing a thumbnail
                        // briefly replaced by another item's image. If no invisible candidate
                        // can be found (e.g. the list is short enough to all fit on screen),
                        // fall through to append a new slot instead.
                        string? evictedKey = null;
                        ThumbnailSlot? evictedSlot = null;

                        while (_lruKeys.Count > 0)
                        {
                            string candidateKey = _lruKeys.RemoveFirst();
                            if (!_slotByKey.TryGetValue(candidateKey, out var candidateSlot))
                            {
                                // Orphaned LRU entry with no slot; drop it and continue.
                                continue;
                            }

                            int itemIndex = _expList.GetIndexFromFullPath(candidateSlot.Item?.FullPath ?? string.Empty);
                            if (itemIndex >= 0 && _expList.IsItemVisible(itemIndex))
                            {
                                // Visible — put back at the end (most-recently-used) and keep looking.
                                _lruKeys.Add(candidateKey);
                                continue;
                            }

                            evictedKey = candidateKey;
                            evictedSlot = candidateSlot;
                            break;
                        }

                        if (evictedSlot != null && evictedKey != null)
                        {
                            _slotByKey.Remove(evictedKey);
                            if (evictedSlot.Item != null)
                            {
                                evictedSlot.Item.ImageIndex = -1;
                            }

                            index = evictedSlot.Index;
                            evictedSlot.Item = reqArgs.Item;
                            evictedSlot.Key = key;

                            lock (imageList)
                            {
                                imageList.Images[index] = thumbnail;
                                _lruKeys.Add(key);
                                _slotByKey[key] = evictedSlot;
                            }
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        lock (imageList)
                        {
                            imageList.Images.Add(thumbnail);
                            index = imageList.Images.Count - 1;
                            var newSlot = new ThumbnailSlot(index, reqArgs.Item, key);
                            _lruKeys.Add(key);
                            _slotByKey[key] = newSlot;
                        }
                    }
                    //Debug.WriteLine("\tImageList size: " + imageList.Images.Count.ToString());
                }

                if (index != -1 && reqArgs.Item != null)
                    reqArgs.Item.ImageIndex = index;

                return index;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error adding thumbnail: " + ex.Message);
      
                return -1;
            }
        }


        /// <summary>
        /// Disposes all existing ImageLists and creates a fresh one for the current active size.
        /// Called when navigating to a new folder to prevent GDI handle exhaustion from
        /// accumulated thumbnails across many folder navigations.
        /// </summary>
        public void ResetForNewFolder()
        {
            _generation++;
            CancelPendingRequests();

            foreach (var imageList in _imageLists.Values)
            {
                lock (imageList)
                {
                    imageList?.Dispose();
                }
            }
            _imageLists.Clear();
            _lruKeys.Clear();
            _slotByKey.Clear();

            if (_expList != null && _expList._listView != null)
            {
                _expList._listView.LargeImageList = null;
                _expList._listView.SmallImageList = null;
            }

            if (_activeSize > 0)
            {
                var freshList = new ImageList
                {
                    ImageSize = new Size(_activeSize, _activeSize),
                    ColorDepth = ColorDepth.Depth32Bit
                };
                _imageLists[_activeSize] = freshList;
                if (_expList?._listView != null)
                    _expList._listView.LargeImageList = freshList;
            }
        }

        /// <summary>
        /// Clears all ImageLists and resets the ListView
        /// </summary>
        public void Clear()
        {
            foreach (var imageList in _imageLists.Values)
            {
                lock (imageList)
                {
                    imageList?.Dispose();
                }
            }
            _imageLists.Clear();

            _lruKeys.Clear();
            _slotByKey.Clear();

            if (_expList != null && _expList._listView != null)
            {
                _expList._listView.LargeImageList = null;
                _expList._listView.SmallImageList = null;
            }
        }

        /// <summary>
        /// Clears the thumbnail cache
        /// </summary>
        public void ClearCache()
        {
            _thumbnailProvider?.ClearCache();
        }

        private string CreateKey(string fullFileName, int size)
        {
            return $"{fullFileName}|{size}";
        }

        public void Dispose()
        {
            Clear();
            _thumbnailProvider?.Dispose();
        }


    }
}
