using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using C5;
using WindowsApiLib.Shell;
using static System.Windows.Forms.ListView;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

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
        private const int MaxThumbnails = 3000;

        private bool _addingImage = false;
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

        public ThumbnailImageListManager(ExpList expList)
        {
            _expList = expList;
            _thumbnailProvider = new ThumbnailProvider();
            _thumbnailProvider.ThumbnailReady += OnThumbnailReady;
        }

        public int GetThumbnailIndex(string filePath, int requestedSize)
        {
            if (_slotByKey.TryGetValue($"{filePath}|{requestedSize}", out var slot))
            {
                // Update LRU on access
                string key = slot.Key;
                _lruKeys.Remove(key);
                _lruKeys.Add(key);
                return slot.Index;
            }
            return -1;
        }

        public void SetImageListSize(int thumbnailSize)
        {
            if (_addingImage) return;

            _activeSize = thumbnailSize;

            var imageList = GetImageList(thumbnailSize);

            _expList._listView.BeginUpdate();
            _expList._listView.LargeImageList = imageList;
            if (!_expList._listView.VirtualMode)
            {
                foreach (ListViewItem item in _expList._listView.Items)
                {
                    if (item is null) continue;
                    item.ImageIndex = -1;
                }
            }
            _expList._listView.EndUpdate();
        }


        public void BeginSession(int thumbnailSize)
        {
            _generation++;
            _activeSize = thumbnailSize;

            var imageList = GetImageList(thumbnailSize);
            imageList.Images.Clear();
            
            _lruKeys.Clear();
            _slotByKey.Clear();

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
            Console.WriteLine("\tRequesting thumbnail: " + csi.Text);
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
            if (_expList._listView.IsDisposed || _expList._listView.Disposing || !_expList._listView.IsHandleCreated)
            {
                e.Thumbnail?.Dispose();
                return;
            }

            if (e.Size != _activeSize) //this can happen if we switch display modes while thumbnail requests are outstanding
            {
                e.Thumbnail?.Dispose();
                return;
            }

            if (e.Thumbnail != null && !_expList._listView.IsDisposed && _expList._listView.IsHandleCreated)
            {
                // The thumbnail from ThumbnailProvider is already square and in the correct format.
                // We pass it directly to the UI thread. ApplyThumbnailToUI will dispose it.
                _expList._listView.BeginInvoke(new Action(() => ApplyThumbnailToUI(e, (Bitmap)e.Thumbnail)));
            }
            else
            {
                e.Thumbnail?.Dispose();
            }
        }

        /// <summary>
        /// Force thumbnail registration from the UI thread.  The active image list actually belongs to the 
        /// ListView so we can't event read the count without causing an exception unless we are running from 
        /// on the ui thread.
        /// </summary>
        /// <param name="reqArgs"></param>
        /// <param name="square"></param>
        private void ApplyThumbnailToUI(ThumbnailReadyEventArgs reqArgs, Bitmap square)
        {
            if (square == null)
            {
                if (reqArgs.Item != null) reqArgs.Item.ImageIndex = -1;
                return;
            }

            if (reqArgs is null || reqArgs.Item is null)
            {
                square?.Dispose();
                return;
            }

            if (reqArgs.Item.Parent.FullPath != _expList.CurrentPath)
            {
                square?.Dispose();
                return; //orphaned background tasks from before a patch change happened
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

                    _addingImage = true;
                    try { imageList.Images[index] = square; }
                    finally
                    {
                        square?.Dispose();
                        square = null;
                        _addingImage = false;
                    }
                }
                else //new thumbnail
                {
                    bool reused = false;
                    if (_lruKeys.Count >= MaxThumbnails)
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

                            _addingImage = true;
                            try { imageList.Images[index] = square; }
                            finally
                            {
                                square?.Dispose();
                                _addingImage = false;
                            }

                            _lruKeys.Add(key);
                            _slotByKey[key] = oldestSlot;
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        _addingImage = true;
                        try
                        {
                            imageList.Images.Add(square);
                            index = imageList.Images.Count - 1;
                            var newSlot = new ThumbnailSlot(index, reqArgs.Item, key);
                            _lruKeys.Add(key);
                            _slotByKey[key] = newSlot;
                        }
                        finally
                        {
                            square?.Dispose();
                            _addingImage = false;
                        }
                    }
                }

                if (index != -1 && reqArgs.Item != null)
                    reqArgs.Item.ImageIndex = index;

                if (_expList.VirtualMode)
                {
                    if (reqArgs.Index < 0)
                    {
                        var location_index = _expList.GetIndexFromFullPath(reqArgs.Item.FullPath);
                        if (location_index > -1 && location_index < _expList.Count)
                            _expList._listView.RedrawItems(location_index, location_index, false);
                    }
                    else if (reqArgs.Index >= 0 && reqArgs.Index < _expList._listView.VirtualListSize)
                    {
                        var location_index = _expList.GetIndexFromFullPath(reqArgs.Item.FullPath);
                        if (location_index == reqArgs.Index)
                            _expList._listView.RedrawItems(reqArgs.Index, reqArgs.Index, false);
                    }
                }
                else
                {
                    var lvi = _expList.FindItemByPath(reqArgs.Item.FullPath);
                    if (lvi != null) lvi.ImageIndex = index;
                }
            }
            catch (Exception ex)
            {
                square?.Dispose();
#if DEBUG
                Console.WriteLine("Error applying thumbnail to UI: " + ex.Message);
#endif
                if (imageList != null)
                    _corruptImageLists.Add(imageList);
            }
        }

        /// <summary>
        /// Clears all ImageLists and resets the ListView
        /// </summary>
        public void Clear()
        {
            foreach (var imageList in _imageLists.Values)
            {
                imageList?.Dispose();
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

        public void Dispose()
        {
            Clear();
            _thumbnailProvider?.Dispose();
        }

        private string CreateKey(string fullFileName, int size)
        {
            return $"{fullFileName}|{size}";
        }
    }
}
