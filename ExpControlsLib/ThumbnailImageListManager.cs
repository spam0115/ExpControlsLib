using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

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
        private int _generation;
        private readonly Dictionary<string, int> _imageIndexByKey = new Dictionary<string, int>();

        private sealed class ThumbnailRequestTag
        {
            public int Generation { get; set; }
            public string FilePath { get; set; }
            public int RequestedSize { get; set; }
            public ListViewItem Item { get; set; }
        }

        public ThumbnailImageListManager(ListView listView)
        {
            _listView = listView;
            _thumbnailProvider = new ThumbnailProvider();
            _thumbnailProvider.ThumbnailReady += OnThumbnailReady;
        }

        public void SetImageListSize(int thumbnailSize)
        {
            _activeSize = thumbnailSize;

            var imageList = GetImageList(thumbnailSize);

            _listView.LargeImageList = imageList;

            _listView.BeginUpdate();
            foreach (ListViewItem item in _listView.Items)
                item.ImageIndex = -1;
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

            foreach (ListViewItem item in _listView.Items)
                item.ImageIndex = -1;
        }

        /// <summary>
        /// Gets or creates an ImageList for the specified thumbnail size
        /// </summary>
        public ImageList GetImageList(int thumbnailSize)
        {
            if (!_imageLists.TryGetValue(thumbnailSize, out var imageList))
            {
#if DEBUG
                Console.WriteLine("Creating new image list for thumbnails...");
#endif
                imageList = new ImageList
                {
                    ImageSize = new Size(thumbnailSize, thumbnailSize),
                    ColorDepth = ColorDepth.Depth32Bit
                };
                _imageLists[thumbnailSize] = imageList;
            }
            else
            {

            }

            return imageList;
        }

        /// <summary>
        /// Requests a thumbnail for a file and updates the ListView when ready
        /// </summary>
        public void RequestThumbnail(ListViewItem item, string filePath, int thumbnailSize)
        {
#if DEBUG
            Console.WriteLine("\tRequesting thumbnail: " + item.Text);
#endif

            var reqObj = new ThumbnailRequestTag
            {
                Generation = _generation,
                FilePath = filePath,
                RequestedSize = thumbnailSize,
                Item = item
            };

            var csi = item.Tag as CShellItem;
            _thumbnailProvider.EnqueueThumbnailRequest(csi, thumbnailSize, reqObj);
        }

        /// <summary>
        /// Handles thumbnail ready events and updates the ListView.
        /// Important: this is where the image lists is assigned to the ListView.
        /// </summary>
        private void OnThumbnailReady(object sender, ThumbnailReadyEventArgs e)
        {
            if (!(e.Tag is ThumbnailRequestTag tag))
                return;

            if (_listView.IsDisposed || !_listView.IsHandleCreated)
                return;

            if (_listView.InvokeRequired) //what's this for?
            {
                _listView.BeginInvoke(new EventHandler<ThumbnailReadyEventArgs>(OnThumbnailReady), sender, e);
                return;
            }

#if DEBUG
            Console.WriteLine("Received Thumbnail ready event");
            Console.WriteLine("\tItem: " + tag.Item.Text);
#endif

            if (tag.Generation != _generation) return;
            if (tag.RequestedSize != _activeSize) return;
            if (tag.Item == null || tag.Item.ListView != _listView) return;

            // safety: item still points to same shell object/path
            if (!(tag.Item.Tag is CShellItem csi) || !string.Equals(csi.FullPath, tag.FilePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (e.Thumbnail == null)
            {
                tag.Item.ImageIndex = -1;
                return;
            }

            var imageList = GetImageList(_activeSize);
            if (_listView.LargeImageList != imageList)
                _listView.LargeImageList = imageList;
            
            string key = $"{tag.FilePath}|{tag.RequestedSize}";
            if (!_imageIndexByKey.TryGetValue(key, out int index))
            {
                // Use Format32bppPArgb to match what the shell produced (premultiplied alpha)
                var normalized = new Bitmap(_activeSize, _activeSize,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb); //TODO: do we even need to normalize this anymore?  I think we are already normalizing it in the ThumbnailProvider class now.

                using (var g = Graphics.FromImage(normalized))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; // <-- key
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(e.Thumbnail, new Rectangle(0, 0, _activeSize, _activeSize));
                }

                imageList.Images.Add(normalized);   // pass the Bitmap directly, do NOT Clone() to Image
                index = imageList.Images.Count - 1;
                _imageIndexByKey[key] = index;
            }

            tag.Item.ImageIndex = index;
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
    }
}
