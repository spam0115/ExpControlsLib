using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static System.Windows.Forms.ListView;

namespace ExpControlsLib
{
    /// <summary>
    /// Manages ImageLists for thumbnail display modes in the ListView control.
    /// Creates and maintains separate ImageLists for different thumbnail sizes.
    /// </summary>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ThumbnailImageListManager : IDisposable
    {
        private readonly Dictionary<int, ImageList> _imageLists = new Dictionary<int, ImageList>();
        private readonly ThumbnailProvider _thumbnailProvider;
        private readonly ListView _listView;
        private int _activeSize;
        private int _generation = 0;
        private readonly Dictionary<string, int> _imageIndexByKey = new Dictionary<string, int>();
        private bool _addingImage = false;
        private readonly HashSet<ImageList> _corruptImageLists = new HashSet<ImageList>();

        private sealed class ThumbnailRequestArgs
        {
            public int Generation { get; set; }
            public string FilePath { get; set; }
            public int RequestedSize { get; set; }
            public ListViewItem? Item { get; set; }
            public int ItemIndex { get; set; } = -1;
        }

        public ThumbnailImageListManager(ListView listView)
        {
            _listView = listView;
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

            _listView.BeginUpdate();
            _listView.LargeImageList = imageList;
            if (!_listView.VirtualMode)
            {
                foreach (ListViewItem item in _listView.Items)
                {
                    if (item is null) continue;
                    item.ImageIndex = -1;
                }
            }
            _listView.EndUpdate();
        }


        public void BeginSession(int thumbnailSize)
        {
            _generation++;
            _activeSize = thumbnailSize;

            var imageList = GetImageList(thumbnailSize);
            imageList.Images.Clear();
            _imageIndexByKey.Clear();

            _listView.LargeImageList = imageList;

            if (!_listView.VirtualMode)
            {
                foreach (ListViewItem item in _listView.Items)
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
                _imageLists.Remove(thumbnailSize);
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
        public void RequestThumbnail(ListViewItem? item, string filePath, int thumbnailSize, int itemIndex = -1, CShellItem? csi = null)
        {
#if DEBUG
            if (item != null) Console.WriteLine("\tRequesting thumbnail: " + item.Text);
#endif

            var reqObj = new ThumbnailRequestArgs
            {
                Generation = _generation,
                FilePath = filePath,
                RequestedSize = thumbnailSize,
                Item = item,
                ItemIndex = itemIndex
            };

            csi ??= item?.Tag as CShellItem;
            _thumbnailProvider.EnqueueThumbnailRequest(csi, thumbnailSize, reqObj);
        }

        /// <summary>
        /// Requests a thumbnail for a file and updates the ListView when ready
        /// </summary>
        public void RequestThumbnailFromCache(ListViewItem? item, string filePath, int thumbnailSize, int itemIndex = -1, CShellItem? csi = null)
        {
#if DEBUG
            if (item != null) Console.WriteLine("\tRequesting thumbnail: " + item.Text);
#endif
            var reqObj = new ThumbnailRequestArgs
            {
                Generation = _generation,
                FilePath = filePath,
                RequestedSize = thumbnailSize,
                Item = item,
                ItemIndex = itemIndex
            };

            string key = CreateKey(reqObj);
            if (_imageIndexByKey.TryGetValue(key, out int index))
            {
                if (item != null) item.ImageIndex = index;
            }
            else
            {
                csi ??= item?.Tag as CShellItem;
                _thumbnailProvider.EnqueueThumbnailRequest(csi, thumbnailSize, reqObj);
            }
        }
        
        /// <summary>
         /// Handles thumbnail ready events and updates the ListView.
         /// Image manipulation is done on the background thread, while UI updates
         /// are marshalled to the UI thread.
         /// </summary>
        private void OnThumbnailReady(object sender, ThumbnailReadyEventArgs e)
        {
            if (!(e.Tag is ThumbnailRequestArgs tag)) return;
            if (_listView.IsDisposed || _listView.Disposing || !_listView.IsHandleCreated) return;

            //if (tag.Generation != _generation)
            //    return;

            if (tag.RequestedSize != _activeSize) //this can happen if we switch display modes while thumbnail requests are outstanding
                return;

            if (tag.Item != null && tag.Item.ListView != _listView) return;

            //// safety: ensure item still points to same shell object/path
            //if (!(tag.Item.Tag is CShellItem csi) || !string.Equals(csi.FullPath, tag.FilePath, StringComparison.OrdinalIgnoreCase))
            //    return;

            int index = -1;
            Bitmap? square = null;

            if (e.Thumbnail != null)
            {
                // Image manipulation on background thread
                // Use Format32bppPArgb to match what the shell produced (premultiplied alpha)
                square = new Bitmap(tag.RequestedSize, tag.RequestedSize,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                using (var g = Graphics.FromImage(square))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                    g.DrawImage(e.Thumbnail, new Rectangle(0, 0, tag.RequestedSize, tag.RequestedSize));
                }
            }

            if (!_listView.IsDisposed && _listView.IsHandleCreated)
            {
                _listView.BeginInvoke(new Action(() => ApplyThumbnailToUI(tag, square)));
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
        /// <param name="tag"></param>
        /// <param name="square"></param>
        private void ApplyThumbnailToUI(ThumbnailRequestArgs tag, Bitmap? square)
        {
            if (tag.Item == null && tag.ItemIndex == -1)
            {
                square?.Dispose();
                return;
            }

            if (tag.Item != null && tag.Item.ListView != _listView)
            {
                square?.Dispose();
                return;
            }

            if (square == null)
            {
                if (tag.Item != null) tag.Item.ImageIndex = -1;
                return;
            }

            ImageList imageList = null;
            try
            {
                imageList = GetImageList(_activeSize);
                if (_listView.LargeImageList != imageList)
                    _listView.LargeImageList = imageList;

                string key = CreateKey(tag);
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
                    //oldImage.Dispose(); do not do this.  internal state corruption
                }

                if (tag.Item != null)
                {
                    tag.Item.ImageIndex = index;
                }
                else if (tag.ItemIndex != -1)
                {
                    _listView.RedrawItems(tag.ItemIndex, tag.ItemIndex, false);
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

            if (_listView != null)
            {
                _listView.LargeImageList = null;
                _listView.SmallImageList = null;
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

        private string CreateKey(ThumbnailRequestArgs tag)
        {
            return $"{tag.FilePath}|{tag.RequestedSize}";
        }
    }
}
