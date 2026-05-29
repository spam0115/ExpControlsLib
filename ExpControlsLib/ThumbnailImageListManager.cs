using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static System.Windows.Forms.ListView;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ExpControlsLib
{
    /// <summary>
    /// Manages ImageLists for thumbnail display modes in the ListView control.
    /// Creates and maintains separate ImageLists for different thumbnail sizes.
    /// </summary>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ThumbnailImageListManager : IDisposable
    {
        private readonly ConcurrentDictionary<int, ImageList> _imageLists = new();
        private readonly ThumbnailProvider _thumbnailProvider;
        private readonly ExpList _expList;
        private int _activeSize;
        private int _generation = 0;
        private readonly ConcurrentDictionary<string, int> _imageIndexByKey = new();
        private bool _addingImage = false;
        private readonly HashSet<ImageList> _corruptImageLists = new HashSet<ImageList>();

        public ThumbnailImageListManager(ExpList expList)
        {
            _expList = expList;
            _thumbnailProvider = new ThumbnailProvider();
            _thumbnailProvider.ThumbnailReady += OnThumbnailReady;
        }

        public int GetThumbnailIndex(string filePath, int requestedSize)
        {
            if (_imageIndexByKey.TryGetValue($"{filePath}|{requestedSize}", out int index))
                return index;
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
            _imageIndexByKey.Clear();

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
            if (_imageIndexByKey.TryGetValue(key, out int index))
            {
                csi.ImageIndex = index;
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
            if (_expList._listView.IsDisposed || _expList._listView.Disposing || !_expList._listView.IsHandleCreated) return;

            //if (tag.Generation != _generation)
            //    return;

            if (e.Size != _activeSize) //this can happen if we switch display modes while thumbnail requests are outstanding
                return;

            //// safety: ensure item still points to same shell object/path
            //if (!(tag.Item.Tag is CShellItem csi) || !string.Equals(csi.FullPath, tag.FilePath, StringComparison.OrdinalIgnoreCase))
            //    return;

            Bitmap? square = null;

            if (e.Thumbnail != null)
            {
                // Image manipulation on background thread
                // Use Format32bppPArgb to match what the shell produced (premultiplied alpha)
                square = new Bitmap(e.Size, e.Size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                using (var g = Graphics.FromImage(square))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                    g.DrawImage(e.Thumbnail, new Rectangle(0, 0, e.Size, e.Size));
                }
            }

            if (square != null && !_expList._listView.IsDisposed && _expList._listView.IsHandleCreated)
            {
                _expList._listView.BeginInvoke(new Action(() => ApplyThumbnailToUI(e, square)));
            }
            else
            {
                square?.Dispose();
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

            ImageList imageList = null;
            try
            {
                imageList = GetImageList(_activeSize);
                if (_expList._listView.LargeImageList != imageList)
                    _expList._listView.LargeImageList = imageList;

                string key = CreateKey(reqArgs.Item.FullPath, reqArgs.Size);
                if (!_imageIndexByKey.TryGetValue(key, out int index))
                {
                    _addingImage = true;
                    try { 
                        imageList.Images.Add(square);
                        index = imageList.Images.Count - 1;
                        _imageIndexByKey[key] = index;
                    }
                    finally {
                        square?.Dispose(); 
                        _addingImage = false; 
                    }
                }
                else
                {
                    var oldImage = imageList.Images[index];
                    _addingImage = true;
                    try { imageList.Images[index] = square; }
                    finally { 
                        square?.Dispose(); 
                        _addingImage = false; 
                    }
                    //oldImage.Dispose(); //do not do this.  causes internal imageList state corruption
                }

                reqArgs.Item.ImageIndex = index;

                if (_expList.VirtualMode)
                {
                    if (reqArgs.Index < 0)
                    {
                        var location_index = _expList.GetIndexFromFullPath(reqArgs.Item.FullPath);
                        if (location_index > -1 && location_index < _expList.Count)
                            _expList._listView.RedrawItems(reqArgs.Index, reqArgs.Index, false);
                    }
                    else if (reqArgs.Index >= 0 && reqArgs.Index < _expList._listView.Items.Count)
                    {
                        var location_index = _expList.GetIndexFromFullPath(reqArgs.Item.FullPath);
                        if (location_index == reqArgs.Index && location_index > -1 && location_index < _expList.Count)
                            _expList._listView.RedrawItems(reqArgs.Index, reqArgs.Index, false);
                        // If the index doesn't match, it means the item has likely been removed or the list has changed, so we should ignore this thumbnail.
                    }
                }
                else
                {
                    if (reqArgs.Index == -1)
                    {
                        var lvi = _expList.FindItemByPath(reqArgs.Item.FullPath);
                        if (lvi != null) lvi.ImageIndex = index;
                    }
                    else if (reqArgs.Index < _expList._listView.Items.Count)
                    {
                        var lvi = _expList.FindItemByPath(reqArgs.Item.FullPath);
                        if (lvi == null) return;
                        if (lvi.Index == reqArgs.Index)
                        {
                            lvi.ImageIndex = index;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                square?.Dispose();
#if DEBUG
                Console.WriteLine("Error applying thumbnail to UI: " + ex.Message);
                throw;
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
