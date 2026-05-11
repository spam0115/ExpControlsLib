using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using WindowsApiLib;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Provides thumbnail extraction for shell items using Windows Shell APIs
    /// (specifically <c>IShellItemImageFactory</c>). Thumbnails are produced on
    /// background worker tasks, cached in memory, and delivered to consumers via
    /// the <see cref="ThumbnailReady"/> event.
    /// </summary>
    /// <remarks>
    /// This class is Windows-only because it depends on the Windows Shell COM
    /// surface and GDI bitmap interop. Always call <see cref="Dispose"/> when
    /// you are finished with an instance so that the background processor task,
    /// COM apartment, and cached <see cref="Image"/> objects are released.
    /// </remarks>
    [SupportedOSPlatform("windows")] // Added to indicate this class is Windows-only
    public class ThumbnailProvider : IDisposable
    {
        /// <summary>
        /// In-memory cache of previously generated thumbnails keyed by
        /// "path|size". Avoids repeated shell calls and HBITMAP marshaling.
        /// </summary>
        private readonly ConcurrentDictionary<string, Image> _thumbnailCache =
            new ConcurrentDictionary<string, Image>();

        /// <summary>
        /// queue for holding pending thumbnail requests that will be processed by the background worker.
        /// Do NOT use a bounded, blocking queue.  We need the UI to be responsive at all times.
        /// </summary>
        private StaThreadRunner _requestQueueRunner;

        /// <summary>Cancellation source used to stop the background processor on dispose.</summary>
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private List<Task> _activeTasks = new(); //strictly speaking, we don't really need this but just in case //todo: change this to a queue or linked list

        private int _maxThreads = 1;  /// <summary>Maximum number of thumbnails generated concurrently</summary>

        #region Public events

        /// <summary>
        /// Raised on a worker thread when a thumbnail (or null fallback) becomes
        /// available for a previously submitted request. Subscribers that update
        /// UI must marshal back to the UI thread themselves.
        /// </summary>
        public event EventHandler<ThumbnailReadyEventArgs> ThumbnailReady;

        #endregion

        #region Public methods 

        /// <summary>
        /// Initializes a new <see cref="ThumbnailProvider"/> and starts the
        /// background thumbnail processor.
        /// </summary>
        public ThumbnailProvider()
        {
            //InitializeCOM(); // COM initialization is now handled in the background thread to avoid issues with UI thread COM apartments
            //StartBackgroundProcessor();

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

#if DEBUG
            _maxThreads = Environment.ProcessorCount;
#else
            _maxThreads = Environment.ProcessorCount;
#endif
            //in testing, moving from 1 to 2 to 4 provided improvements.  Going from 4 to 6 and to 8 both provided a slowdown.
            if (_maxThreads > 4) _maxThreads = 4; //I don't think the OS can handle more than 4 or 8 requests at a time

            _requestQueueRunner = new StaThreadRunner(staThreadCount: _maxThreads, threadNamePrefix: "StaThreadRunner_");
        }

        /// <summary>
        /// Queues a thumbnail request that will be processed asynchronously for the specified shell
        /// item. If the thumbnail is already cached, <see cref="ThumbnailReady"/>
        /// is raised synchronously on the calling thread; otherwise it will be
        /// raised later on a background thread.
        /// </summary>
        /// <param name="shellItem">The shell item to generate a thumbnail for.</param>
        /// <param name="size">Desired thumbnail size in pixels (e.g., 96, 256).</param>
        /// <param name="tag">Optional caller-supplied object echoed back in the event args (useful for correlation).</param>
        public void EnqueueThumbnailRequest(CShellItem shellItem, int size, object tag = null)
        {
            // Check cache first
            if (TryGetCachedThumbnail(shellItem.FullPath, size, out var cachedImage))
            {
#if DEBUG
                Console.WriteLine("\tFound cached thumbnail: " + shellItem.DisplayName);
#endif
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(shellItem, cachedImage, tag, size));
                return;
            }

            var request = new ThumbnailRequest { ShellItem = shellItem, Size = size, Tag = tag };

#if DEBUG
            Console.WriteLine("\tAttempting to add to thumbnail request queue: " + shellItem.DisplayName);
#endif

            var task = _requestQueueRunner.InvokeAsync(_cancellationToken => { 
                if (_cancellationToken.IsCancellationRequested) return; 
                GenerateThumbnail(request); }
                , _cancellationToken);

            _activeTasks.Add(task);

            PruneActiveTasks(false); //don't need to thouroughly here

        }

        public void CancelAllPendingOperations() { 
            _cancellationTokenSource.Cancel();

            PruneActiveTasks(); 

            _activeTasks.Clear(); //this might be a memory leak because tasks don't get canceled instantaneously
        } 

#endregion

        /// <summary>
        /// Worker routine that produces the thumbnail for a single
        /// <see cref="ThumbnailRequest"/>, populates the cache, and raises
        /// <see cref="ThumbnailReady"/>. A null thumbnail is reported on failure
        /// so consumers can fall back to an icon.
        /// </summary>
        /// <param name="request">The request to process.</param>
        private void GenerateThumbnail(ThumbnailRequest request)
        {
            try
            {
#if DEBUG
                Console.WriteLine("Attempting to generate thumbnail for: " + request.ShellItem.DisplayName);
#endif

                Image thumbnail = GetThumbnailFromOS(request.ShellItem.PIDL, request.Size);

                if (thumbnail != null)
                {
                    _thumbnailCache.TryAdd(ConstructCacheKey(request.ShellItem.FullPath, request.Size), thumbnail);
                }
                else
                {
#if DEBUG
                    // No thumbnail available, return null to indicate fallback to icon
                    Console.WriteLine("\tFailed to generate thumbnail");
#endif
                }

                //send event back to the consumer
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(request.ShellItem, thumbnail, request.Tag, request.Size));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating thumbnail for {request.ShellItem.FullPath}: {ex}");
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(request.ShellItem, null, request.Tag, request.Size));
            }
        }

        /// <summary>
        /// Builds the composite cache key used by <see cref="_thumbnailCache"/>:
        /// the file path and requested pixel size separated by a pipe.
        /// </summary>
        /// <param name="filePath">Full path of the shell item.</param>
        /// <param name="size">Requested square thumbnail size, in pixels.</param>
        /// <returns>The cache key string.</returns>
        private string ConstructCacheKey(string filePath, int size) => $"{filePath}|{size}";

        /// <summary>
        /// Synchronously extracts a thumbnail from a file system path using
        /// <c>IShellItemImageFactory</c>. Falls back to an icon image if no
        /// thumbnail is available.
        /// </summary>
        /// <param name="filePath">Full path to the file or folder.</param>
        /// <param name="size">Desired thumbnail size in pixels (square).</param>
        /// <returns>
        /// A <see cref="Bitmap"/> letterboxed to <paramref name="size"/> x
        /// <paramref name="size"/>, or <c>null</c> if no image could be obtained.
        /// </returns>
        public Image GetThumbnailFromOS(string filePath, int size)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            bool isFile = File.Exists(filePath);
            bool isDir = Directory.Exists(filePath);

            if (!isFile && !isDir)
            {
#if DEBUG
                Console.WriteLine($"ERROR: filesystem object does not exist: '{filePath}");
#endif
                return null;
            }

            IntPtr factoryPtr = IntPtr.Zero;

            try
            {
#if DEBUG
                Console.WriteLine("\tRequesting thumbnail from OS: " + filePath);
#endif

                // Ask directly for IShellItemImageFactory
                Guid iid = ShellAPI.IID_IShellItemImageFactory; // must be BCC18B79-BA16-442F-80C4-8A59C30C463B
                int hr = ShellAPI.SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out factoryPtr);
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                    return null;

                var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);

                return GetThumbnailFromOsBase(factory, size);
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            }
        }


        /// <summary>
        /// Synchronously extracts a thumbnail for a shell item identified by an
        /// absolute PIDL. This overload supports virtual (non-file-system) shell
        /// items such as Control Panel entries or library roots.
        /// </summary>
        /// <param name="pidl">Absolute item identifier list (PIDL) of the shell item.</param>
        /// <param name="size">Desired thumbnail size in pixels (square).</param>
        /// <returns>
        /// A <see cref="Bitmap"/> letterboxed to <paramref name="size"/> x
        /// <paramref name="size"/>, or <c>null</c> if no image could be obtained.
        /// </returns>
        public Image GetThumbnailFromOS(IntPtr pidl, int size)
        {
            if (pidl == IntPtr.Zero) return null;

            IntPtr factoryPtr = IntPtr.Zero;

            try
            {
#if DEBUG
                string name = ShellPidl.PidlToString(CPidl.ILFindLastID(pidl));
                Console.WriteLine("\tRequesting thumbnail from OS: " + name);
#endif
                Guid iid = ShellAPI.IID_IShellItemImageFactory;
                int hr = ShellAPI.SHCreateItemFromIDList(pidl, ref iid, out factoryPtr);
                if (hr != 0 || factoryPtr == null) return null;

                var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);

                return GetThumbnailFromOsBase(factory, size);
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            }
        }

        private static Image GetThumbnailFromOsBase(IShellItemImageFactory factory, int size)
        {
            int hr;
            IntPtr hbm = IntPtr.Zero;

            try
            {
                //int flags = SIIGBF_ICONONLY | SIIGBF_THUMBNAILONLY; //SIIGBF_BIGGERSIZEOK - don't use biggersize you'll just get no thumbnail back;
                uint flags = (uint)ShellAPI.SIIGBF.THUMBNAILONLY;
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);

                if (hr != 0 || hbm == IntPtr.Zero) //in case of failure, fallback to get icon instead of thumbnail
                {
                    flags = (uint)ShellAPI.SIIGBF.ICONONLY;
                    hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero)
                        return null;
                }

                using (var raw = BitmapHelper.HBitmapToBitmapWithAlpha(hbm))
                {
                    if (raw == null) return null;
                    return ApplyLetterbox(raw, size);
                }
            }
            finally 
            {
                if (hbm != IntPtr.Zero) WinSDK.DeleteObject(hbm);
            }
        }


        /// <summary>
        /// Attempts to retrieve a previously generated thumbnail from the
        /// in-memory cache.
        /// </summary>
        /// <param name="filePath">Path of the shell item whose thumbnail is sought.</param>
        /// <param name="size">Pixel size that was originally requested.</param>
        /// <param name="thumbnail">When this method returns, contains the cached image if found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if a cached thumbnail was found; otherwise <c>false</c>.</returns>
        public bool TryGetCachedThumbnail(string filePath, int size, out Image thumbnail)
        {
            return _thumbnailCache.TryGetValue(ConstructCacheKey(filePath, size), out thumbnail);
        }



        /// <summary>
        /// Scales and pads a source bitmap to fit within a square of the given size, preserving
        /// aspect ratio, and centers it on a transparent background. The returned bitmap
        /// is always exactly size x size, suitable for direct insertion into an ImageList.
        /// </summary>
        private static Bitmap ApplyLetterbox(Image source, int size)
        {
            var destBmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            int srcW = source.Width;
            int srcH = source.Height;

            // If the shell already returned a square at the requested size, just copy it.
            if (srcW == size && srcH == size)
            {
                using (var graphic = Graphics.FromImage(destBmp))
                {
                    graphic.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphic.DrawImage(source, 0, 0);
                }
                return destBmp;
            }

            float scale = Math.Min((float)size / srcW, (float)size / srcH);
            int dstW = Math.Max(1, (int)Math.Round(srcW * scale));
            int dstH = Math.Max(1, (int)Math.Round(srcH * scale));
            int dstX = (size - dstW) / 2;
            int dstY = (size - dstH) / 2;

            using (var g = Graphics.FromImage(destBmp))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(dstX, dstY, dstW, dstH));
            }

            return destBmp;
        }

        /// <summary>
        /// Clears the thumbnail cache
        /// </summary>
        public void ClearCache()
        {
            foreach (var kvp in _thumbnailCache)
            {
                kvp.Value?.Dispose();
            }
            _thumbnailCache.Clear();
        }

        /// <summary>
        /// Cleans up resources
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _requestQueueRunner.CancelPending();
            _requestQueueRunner.Dispose();

            ClearCache();

            PruneActiveTasks();
            _activeTasks.Clear();

            _cancellationTokenSource?.Dispose();
        }

        private void PruneActiveTasks(bool thorough = true)
        {
            for (int i = 0; i < _activeTasks.Count;)
            {
                var t = _activeTasks[i];
                if (t.IsCompleted || t.IsCanceled)
                {
                    _activeTasks.Remove(t);
                }
                else if (thorough == false) //sometimes, we don't want to thoroughly prune.  A rough prune is good enough because it will soon be pruned again
                {
                    return;
                } 
                else i++;
            }
        }

        #region P/Invoke Declarations


        #endregion

        /// <summary>
        /// Represents a thumbnail generation request
        /// </summary>
        //private class ThumbnailRequest
        //{
        //    public string FilePath { get; set; }
        //    public int Size { get; set; }
        //    public object Tag { get; set; }
        //}
        private class ThumbnailRequest
        {
            public CShellItem ShellItem { get; set; }
            public int Size { get; set; }
            public object Tag { get; set; }
        }

    }

    /// <summary>
    /// Event arguments for thumbnail ready notifications
    /// </summary>
    public class ThumbnailReadyEventArgs : EventArgs
    {
        //public string FilePath { get; }
        public CShellItem ShellItem { get; }
        public Image Thumbnail { get; }
        public object Tag { get; }

        public int RequestedSize { get; }

        //public ThumbnailReadyEventArgs(string filePath, Image thumbnail, object tag, int size)
        //{
        //    FilePath = filePath;
        //    Thumbnail = thumbnail;
        //    Tag = tag;
        //    RequestedSize = size;
        //}
        public ThumbnailReadyEventArgs(CShellItem shellItem, Image thumbnail, object tag, int size)
        {
            ShellItem = shellItem;
            Thumbnail = thumbnail;
            Tag = tag;
            RequestedSize = size;
        }
    }

}
