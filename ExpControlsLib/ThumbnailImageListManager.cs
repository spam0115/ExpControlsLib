using C5;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Manages ImageLists for thumbnail display modes in the ListView control.
    /// Creates and maintains separate ImageLists for different thumbnail sizes.
    ///
    /// <remarks>
    /// Thumbnail requests are deduplicated by item path and requested size. A
    /// request remains leased until its UI-side result is
    /// consumed, so repeated virtual-list callbacks cannot enqueue duplicate
    /// work while the provider callback is waiting in the UI message queue.
    /// A three-second lease expiration recovers from lost callbacks.
    /// </remarks>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ThumbnailImageListManager : IDisposable
    {
        private readonly ConcurrentDictionary<int, ImageList> _imageLists = new(); //one imagelist for each image size
        private readonly ThumbnailProvider _thumbnailProvider;
        private readonly ExpList _expList;
        private int _activeSize = 0;
        
        private readonly Dictionary<int, HashedLinkedList<string>> _lruKeys = new();
        private readonly Dictionary<string, ThumbnailSlot> _slotByKey = new();
        private readonly Dictionary<string, PendingThumbnail> _pending = new();
        private readonly System.Collections.Generic.HashSet<string> _invalidatedKeys = new();
        private readonly object _pendingLock = new();
        private readonly Dictionary<int, int> _capacities = new();
        // Indices occupied by preallocated dummy images. Real thumbnails
        // replace these entries instead of growing the native image list.
        private readonly Dictionary<int, Queue<int>> _freeIndices = new();
        // ImageList retains the source image until its native handle is
        // created, so keep each dummy bitmap alive for the lifetime of its list.
        private readonly Dictionary<int, Bitmap> _dummyImages = new();
        private static readonly TimeSpan PendingLease = TimeSpan.FromSeconds(3);
        private readonly System.Threading.Timer _pendingLeaseTimer;

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
            if (capacity == -1)
            {
                capacity = 16384/size * 2; // default capacity based on 16k texture limit 
            }

            _activeSize = size;
            _expList = expList;
            _capacities[size] = capacity;
            _thumbnailProvider = new ThumbnailProvider();
            _thumbnailProvider.ThumbnailReady += OnThumbnailReady;
            _pendingLeaseTimer = new System.Threading.Timer(
                _ => ExpirePendingRequests(), null, PendingLease, PendingLease);
        }

        /// <summary>
        /// Gets the thumbnail index for a given CShellItem and requested size. 
        /// If the thumbnail is not yet available, it initiates a request and returns -1.
        /// </summary>
        /// <param name="csi"></param>
        /// <param name="size">the thumbnail size</param>
        /// <returns></returns>
        public int GetThumbnailIndex(CShellItem csi, int size)
        {
            if (_slotByKey.TryGetValue($"{csi.FullPath}|{size}", out var slot))
            {
                // Update LRU on access
                string key = slot.Key;
                _lruKeys[size].Remove(key);
                _lruKeys[size].Add(key);
                return slot.Index;
            }
            else
            {
                RequestThumbnail(csi, size);
                return -1;
            }
        }

        /// <summary>
        /// Sets the active ImageList for the specified thumbnail size. 
        /// If an ImageList for that size does not exist, it creates one.
        /// </summary>
        /// <param name="thumbnailSize"></param>
        public void SetExpListLargeImageList(int thumbnailSize)
        {
            _activeSize = thumbnailSize;

            var imageList = GetOrCreateImageList(thumbnailSize);

            _expList.BeginListViewUpdate();
            try
            {
                _expList.LargeImageList = imageList;
                _expList.ResetListViewItemImageIndices();
            }
            finally
            {
                _expList.EndListViewUpdate();
            }
        }

        /// <summary>
        /// Gets or creates an ImageList for the specified thumbnail size
        /// </summary>
        public ImageList GetOrCreateImageList(int thumbnailSize)
        {
            if (_imageLists.TryGetValue(thumbnailSize, out var imageList))
            {
                return imageList;
            }

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Creating new image list for thumbnails...");

            imageList = new ImageList()
            {
                ImageSize = new Size(thumbnailSize, thumbnailSize),
                ColorDepth = ColorDepth.Depth32Bit
            };

            if (!_capacities.ContainsKey(thumbnailSize))
                _capacities[thumbnailSize] = Math.Max(1, 16384 / thumbnailSize * 4);

            int capacity = _capacities[thumbnailSize];

            // Force handle creation
            IntPtr handle = imageList.Handle;

            // Expand it once to the final logical size.
            if (!ShellAPI.ImageList_SetImageCount(handle, checked((uint)capacity)))
            {
                imageList.Dispose();
                throw new Exception("Failed to set image count.");
            }

            _freeIndices[thumbnailSize] = new Queue<int>(Enumerable.Range(0, capacity));
            _imageLists[thumbnailSize] = imageList;

            return imageList;
        }

        /// <summary>
        /// Requests a thumbnail for a file and updates the ListView when ready.
        /// Duplicate requests for the same generation, path, and size are
        /// ignored while the existing request lease is active.
        /// </summary>
        public void RequestThumbnail(CShellItem csi, int thumbnailSize, int itemIndex = -1)
        {
            if (csi == null) return;

            if (!TryBeginRequest(csi, thumbnailSize, out Guid requestId))
                return;

#if DEBUG
            Console.WriteLine("Requesting thumbnail: " + csi.Text);
#endif

            var reqObj = new ThumbnailRequestArgs
            {
                Item = csi,
                Size = thumbnailSize,
                Index = itemIndex,
                RequestId = requestId
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
            if (_slotByKey.TryGetValue(key, out var slot) && !_invalidatedKeys.Contains(key))
            {
                // Update LRU
                _lruKeys[thumbnailSize].Remove(key);
                _lruKeys[thumbnailSize].Add(key);
                csi.ImageIndex = slot.Index;
            }
            else
            {
                RequestThumbnail(csi, thumbnailSize, itemIndex);
            }
        }

        private sealed class PendingThumbnail
        {
            public Guid RequestId { get; init; }
            public DateTime StartedUtc { get; init; }
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
                _lruKeys[thumbnailSize].Remove(key);
                _lruKeys[thumbnailSize].Add(key);
                csi.ImageIndex = slot.Index;
                return slot.Index;
            }

            RequestThumbnail(csi, thumbnailSize, itemIndex);
            return -1;
        }

        /// <summary>
        /// Marks an item's cached thumbnail as stale after a Shell file-change notification.
        /// The old image remains visible until the replacement arrives, but the next request
        /// bypasses the slot cache and replaces that image-list entry in place.
        /// </summary>
        internal void InvalidateThumbnail(CShellItem item, int thumbnailSize)
        {
            if (item == null) return;
            _invalidatedKeys.Add(CreateKey(item.FullPath, thumbnailSize));
        }

        /// <summary>
        /// Cancels provider work and clears the manager's request leases. This is
        /// used when the folder, display mode, or control lifetime changes.
        /// </summary>
        internal void CancelPendingRequests()
        {
            _thumbnailProvider.CancelPendingRequests();
            lock (_pendingLock)
                _pending.Clear();
        }

        /// <summary>
        /// Admits one request for a deduplication key. A live lease suppresses
        /// repeated ListView probes; an expired lease permits recovery from a
        /// provider callback that was never delivered to the UI.
        /// </summary>
        private bool TryBeginRequest(CShellItem item, int thumbnailSize, out Guid requestId)
        {
            string key = CreateRequestKey(item, thumbnailSize);
            DateTime now = DateTime.UtcNow;

            lock (_pendingLock)
            {
                if (_pending.TryGetValue(key, out var pending))
                {
                    if (now - pending.StartedUtc < PendingLease)
                    {
                        requestId = default;
                        return false;
                    }

                    // Recover from a request whose completion callback was lost.
                    _pending.Remove(key);
                }

                requestId = Guid.NewGuid();
                _pending[key] = new PendingThumbnail
                {
                    RequestId = requestId,
                    StartedUtc = now
                };
                return true;
            }
        }

        /// <summary>
        /// Completes and removes the exact lease that admitted a ready result.
        /// A stale callback cannot consume a newer request for the same file.
        /// </summary>
        private bool TryCompleteRequest(ThumbnailReadyEventArgs args)
        {
            if (args.Item == null || args.RequestId == Guid.Empty)
                return false;

            string key = CreateRequestKey(args.Item, args.Size);
            lock (_pendingLock)
            {
                if (!_pending.TryGetValue(key, out var pending) || pending.RequestId != args.RequestId)
                    return false;

                _pending.Remove(key);
                return true;
            }
        }

        private static string CreateRequestKey(CShellItem item, int thumbnailSize) =>
            $"{item.FullPath}|{thumbnailSize}";

        /// <summary>
        /// Removes leases that exceeded the three-second recovery window.
        /// </summary>
        private void ExpirePendingRequests()
        {
            DateTime cutoff = DateTime.UtcNow - PendingLease;
            lock (_pendingLock)
            {
                var expiredKeys = _pending
                    .Where(pair => pair.Value.StartedUtc <= cutoff)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (string key in expiredKeys)
                    _pending.Remove(key);
            }
        }


        /// <summary>
        /// Handles thumbnail ready events and updates the ListView. Results are
        /// accepted only for the current generation and an active deduplication
        /// lease; duplicate callbacks are discarded before touching the image list.
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

            if (reqArgs is null || reqArgs.Item is null)
                return -1;

            // Provider callbacks always carry a request ID and must match the active
            // lease exactly. Retain the public direct-add path for callers and tests
            // that construct event args themselves without going through the provider.
            if (reqArgs.RequestId != Guid.Empty && !TryCompleteRequest(reqArgs))
                return -1;

            if (thumbnail == null)
            {
                reqArgs.Item.ImageIndex = -1;
                return -1;
            }

            int size = reqArgs.Size;
            ImageList imageList = null;
            try
            {
                if (!_lruKeys.ContainsKey(size)) 
                {
                    _lruKeys.Add(size, new HashedLinkedList<string>());
                }

                imageList = GetOrCreateImageList(size);
                if (_expList.LargeImageList != imageList)
                    _expList.LargeImageList = imageList;

                string key = CreateKey(reqArgs.Item.FullPath, size);
                _invalidatedKeys.Remove(key);
                int index = -1;

                if (_slotByKey.TryGetValue(key, out var existingSlot)) //replace existing thumbnail
                {
                    index = existingSlot.Index;
                    _lruKeys[size].Remove(key);
                    _lruKeys[size].Add(key);

                    lock(imageList)
                    {
                        imageList.Images[index] = thumbnail;
                    }
                }
                else //new thumbnail
                {
                    bool reused = false;
                    if (_freeIndices[size].Count == 0)
                    {
                        // Evict the least-recently-used slot that is NOT currently visible.
                        // Skipping visible items prevents the user from seeing a thumbnail
                        // briefly replaced by another item's image. If no invisible candidate
                        // can be found (e.g. the list is short enough to all fit on screen),
                        // fall through to append a new slot instead.
                        string? evictedKey = null;
                        ThumbnailSlot? evictedSlot = null;

                        int candidatesToCheck = _lruKeys[size].Count;
                        for (int candidateNumber = 0; candidateNumber < candidatesToCheck; candidateNumber++)
                        {
                            string candidateKey = _lruKeys[size].RemoveFirst();
                            if (!_slotByKey.TryGetValue(candidateKey, out var candidateSlot))
                            {
                                // Orphaned LRU entry with no slot; drop it and continue.
                                continue;
                            }

                            int itemIndex = _expList.GetIndexFromFullPath(candidateSlot.Item?.FullPath ?? string.Empty);
                            if (itemIndex >= 0 && _expList.IsItemVisible(itemIndex))
                            {
                                // Visible — put back at the end (most-recently-used) and keep looking.
                                _lruKeys[size].Add(candidateKey);
                                continue;
                            }

                            evictedKey = candidateKey;
                            evictedSlot = candidateSlot;
                            break;
                        }

                        // If every slot is visible, use the most recently added
                        // slot as the bounded fallback rather than appending.
                        if (evictedSlot == null)
                        {
                            foreach (string candidateKey in _lruKeys[size])
                            {
                                if (_slotByKey.TryGetValue(candidateKey, out var candidateSlot))
                                {
                                    evictedKey = candidateKey;
                                    evictedSlot = candidateSlot;
                                }
                            }
                        }

                        if (evictedSlot != null && evictedKey != null)
                        {
                            _lruKeys[size].Remove(evictedKey);
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
                                _lruKeys[size].Add(key);
                                _slotByKey[key] = evictedSlot;
                            }
                            reused = true;
                        }
                    }

                    if (!reused && _freeIndices[size].Count > 0)
                    {
                        index = _freeIndices[size].Dequeue();
                        lock (imageList)
                        {
                            imageList.Images[index] = thumbnail;
                            var newSlot = new ThumbnailSlot(index, reqArgs.Item, key);
                            _lruKeys[size].Add(key);
                            _slotByKey[key] = newSlot;
                        }
                        reused = true;
                    }

                    if (!reused)
                        return -1;
                    //Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]\tImageList size: " + imageList.Images.Count.ToString());
                }

                if (index != -1 && reqArgs.Item != null)
                    reqArgs.Item.ImageIndex = index;

                return index;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error adding thumbnail: " + ex.Message);
      
                return -1;
            }
        }


        /// <summary>
        /// Disposes all existing ImageLists and creates a fresh one for the current active size.
        /// Called when navigating to a new folder to prevent GDI handle exhaustion from
        /// accumulated thumbnails across many folder navigations.
        /// </summary>
        public void Reset()
        {
            CancelPendingRequests();

            foreach (var imageList in _imageLists.Values)
            {
                lock (imageList)
                {
                    imageList?.Dispose();
                }
            }
            foreach (var dummy in _dummyImages.Values)
                dummy.Dispose();
            _imageLists.Clear();
            _dummyImages.Clear();
            _freeIndices.Clear();
            _lruKeys.Clear();
            _slotByKey.Clear();
            _invalidatedKeys.Clear();

            _expList?.ClearListViewImageLists();

            if (_activeSize > 0)
            {
                SetExpListLargeImageList(_activeSize);
            }
        }

        /// <summary>
        /// Clears all ImageLists and resets the ListView
        /// </summary>
        public void Clear()
        {
            lock (_pendingLock)
                _pending.Clear();

            foreach (var imageList in _imageLists.Values)
            {
                lock (imageList)
                {
                    imageList?.Dispose();
                }
            }
            foreach (var dummy in _dummyImages.Values)
                dummy.Dispose();
            _imageLists.Clear();
            _dummyImages.Clear();
            _freeIndices.Clear();

            _lruKeys.Clear();
            _slotByKey.Clear();
            _invalidatedKeys.Clear();

            _expList?.ClearListViewImageLists();
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
            _pendingLeaseTimer.Dispose();
            Clear();
            _thumbnailProvider?.Dispose();
        }


    }
}
