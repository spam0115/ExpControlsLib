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
        private readonly int _maxThumbnails;

        private readonly System.Collections.Generic.HashSet<ImageList> _corruptImageLists = new System.Collections.Generic.HashSet<ImageList>();

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

        public event EventHandler<ThumbnailReadyEventArgs> ThumbnailReady;

        public ThumbnailImageListManager(ExpList expList, int capacity = 1000)
        {
            _expList = expList;
            _maxThumbnails = capacity;
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


        //public void BeginSession(int thumbnailSize)
        //{
        //    _generation++;
        //    _activeSize = thumbnailSize;

        //    var imageList = GetImageList(thumbnailSize);
        //    imageList.Images.Clear();
            
        //    _lruKeys.Clear();
        //    _slotByKey.Clear();

        //    _expList._listView.LargeImageList = imageList;

        //    if (!_expList._listView.VirtualMode)
        //    {
        //        foreach (ListViewItem item in _expList._listView.Items)
        //        {
        //            if (item is null) continue;
        //            item.ImageIndex = -1;
        //        }
        //    }
        //}

        /// <summary>
        /// Gets or creates an ImageList for the specified thumbnail size
        /// </summary>
        public ImageList GetImageList(int thumbnailSize)
        {
            if (_imageLists.TryGetValue(thumbnailSize, out var imageList) && !_corruptImageLists.Contains(imageList))
            {
                return imageList;
            }

            if (imageList != null)
            {
                _corruptImageLists.Remove(imageList);
                imageList.Dispose();
                _imageLists.TryRemove(thumbnailSize, out _);
            }

#if DEBUG
            Console.WriteLine("Creating new image list for thumbnails...");
#endif
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
        private void OnThumbnailReady(object sender, ThumbnailReadyEventArgs e)
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
        /// <param name="square">The square thumbnail bitmap.</param>
        /// <returns>The index of the thumbnail in the ImageList, or -1 if it could not be added.</returns>
        public int AddThumbnail(ThumbnailReadyEventArgs reqArgs, Bitmap square)
        {
            Debug.WriteLine("ThumbnailImageListManager: AddThumbnail begin");

            if (square == null)
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
                        imageList.Images[index] = square;
                    }
                }
                else //new thumbnail
                {
                    bool reused = false;
                    if (_lruKeys.Count >= _maxThumbnails)
                    {
                        string oldestKey = _lruKeys.RemoveFirst();
                        if (_slotByKey.TryGetValue(oldestKey, out var oldestSlot))
                        {
                            _slotByKey.Remove(oldestKey);
                            if (oldestSlot.Item != null)
                            {
                                oldestSlot.Item.ImageIndex = -1;
                            }

                            index = oldestSlot.Index;
                            oldestSlot.Item = reqArgs.Item;
                            oldestSlot.Key = key;

                            lock (imageList)
                            {
                                imageList.Images[index] = square;
                                _lruKeys.Add(key);
                                _slotByKey[key] = oldestSlot;
                            }
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        lock (imageList)
                        {
                            imageList.Images.Add(square);
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
      
                if (imageList != null)
                {
                    Debug.WriteLine("Adding imagelist to the corrupt list.");
                    _corruptImageLists.Add(imageList);
                }

                return -1;
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
