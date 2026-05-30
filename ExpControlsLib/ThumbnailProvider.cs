using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

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
        /// "path|size". Stores raw pixel data to avoid GDI handle exhaustion.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte[]> _thumbnailCache =
            new ConcurrentDictionary<string, byte[]>();

        /// <summary>
        /// queue for holding pending thumbnail requests that will be processed by the background worker.
        /// Do NOT use a bounded, blocking queue.  We need the UI to be responsive at all times.
        /// </summary>
        private StaThreadRunner _requestQueueRunner;

        /// <summary>Cancellation source used to stop the background processor on dispose.</summary>
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private LinkedList<Task> _activeTasks = new(); //strictly speaking, we don't really need this but just in case

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
        /// <param name="reqArgs">Optional caller-supplied object echoed back in the event args (useful for correlation).</param>
        public void EnqueueThumbnailRequest(int size, ThumbnailRequestArgs reqArgs)
        {
            if (ThumbnailReady == null)
            {
                Debug.WriteLine("No subscribers for ThumbnailReady event; skipping thumbnail generation.");
                return;
            }

            var csi = reqArgs.Item;

            if (csi is null && string.IsNullOrWhiteSpace(reqArgs.FilePath)) return;

            // Check cache first
            if (TryGetCachedThumbnail(csi.FullPath, size, out var cachedImage))
            {
#if DEBUG
                Console.WriteLine("\tFound cached thumbnail: " + csi.DisplayName);
#endif
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(csi, cachedImage, reqArgs.Index, size));
                return;
            }

#if DEBUG
            Console.WriteLine("\tAttempting to add to thumbnail request queue: " + csi.DisplayName);
#endif

            var task = _requestQueueRunner.InvokeAsync(_cancellationToken => { 
                if (_cancellationToken.IsCancellationRequested) return; 
                GenerateThumbnailAndNotify(reqArgs); }
                , _cancellationToken);

            lock (_activeTasks)
            {
                _activeTasks.AddLast(task);
            }
            PruneActiveTasks(false); //don't need to be thouroughl here
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>After cancellation, already-running tasks may still finish and create bitmaps which may not have gdi resources released correctly.</remarks>
        public void CancelAllPendingOperations() { 
            _cancellationTokenSource.Cancel();

            PruneActiveTasks(); 

            _activeTasks.Clear(); //this might be a memory leak because tasks don't get canceled instantaneously
        }


        /// <summary>
        /// Clears the thumbnail cache
        /// </summary>
        public void ClearCache()
        {
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

            _activeTasks.Clear();

            _cancellationTokenSource?.Dispose();
        }


        #endregion

        #region Private Methods
        /// <summary>
        /// Worker routine that produces the thumbnail for a single
        /// <see cref="ThumbnailRequest"/>, populates the cache, and raises
        /// <see cref="ThumbnailReady"/>. A null thumbnail is reported on failure
        /// so consumers can fall back to an icon.
        /// </summary>
        /// <param name="request">The request to process.</param>
        private void GenerateThumbnailAndNotify(ThumbnailRequestArgs request)
        {
            Bitmap? thumbnail = null;
            try
            {
                Debug.WriteLine("Attempting to generate thumbnail for: " + request.Item.DisplayName);

                if (ThumbnailReady == null) return;

                using (var magickImage = GetMagickThumbnailFromOS(request.Item.PIDL, request.Size))
                {
                    if (magickImage != null)
                    {
                        // Store in cache as raw PArgb bytes to maintain compatibility with BytesToBitmap
                        magickImage.Alpha(AlphaOption.Associate);
                        byte[] bytes = magickImage.ToByteArray(MagickFormat.Bgra);
                        _thumbnailCache.TryAdd(ConstructCacheKey(request.Item.FullPath, request.Size), bytes);

                        // Final output stage: convert to Bitmap
                        thumbnail = magickImage.ToBitmap();
                    }
                }

                //send event back to the consumer.  Subscriber is responsible for disposing thumbnail.
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(request.Item, thumbnail, request.Size, request.Index));
                thumbnail = null; // ownership transferred to subscriber
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating thumbnail for {request.Item.FullPath}: {ex}");
            }
        }

        private Bitmap BytesToBitmap(byte[] bytes, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                return bmp;
            }
            finally
            {
                bmp.UnlockBits(data);
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
        /// <param name="fileName">Full path to the file or folder.</param>
        /// <param name="size">Desired thumbnail size in pixels (square).</param>
        /// <returns>
        /// A <see cref="Bitmap"/> letterboxed to <paramref name="size"/> x
        /// <paramref name="size"/>, or <c>null</c> if no image could be obtained.
        /// </returns>
        public Image? GetThumbnailFromOS(string fileName, int size)
        {
            using (var magickImage = GetMagickThumbnailFromOS(fileName, size))
            {
                return magickImage?.ToBitmap();
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
        public Bitmap? GetThumbnailFromOS(IntPtr pidl, int size)
        {
            using (var magickImage = GetMagickThumbnailFromOS(pidl, size))
            {
                return magickImage?.ToBitmap();
            }
        }

        private MagickImage? GetMagickThumbnailFromOS(string fileName, int size)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            bool isFile = File.Exists(fileName);
            bool isDir = Directory.Exists(fileName);

            if (!isFile && !isDir)
            {
#if DEBUG
                Console.WriteLine($"ERROR: filesystem object does not exist: '{fileName}");
#endif
                return null;
            }

            IntPtr factoryPtr = IntPtr.Zero;
            IShellItemImageFactory factory = null;
            try
            {
#if DEBUG
                Console.WriteLine("\tRequesting thumbnail from OS: " + fileName);
#endif

                // Ask directly for IShellItemImageFactory
                Guid iid = ShellAPI.IID_IShellItemImageFactory; // must be BCC18B79-BA16-442F-80C4-8A59C30C463B
                int hr = ShellAPI.SHCreateItemFromParsingName(fileName, IntPtr.Zero, ref iid, out factoryPtr);
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                    return null;

                factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);

                var result = GetThumbnailFromOsBaseMagick(factory, size);
                if (result == null)
                {
                    Console.WriteLine("Failed to get thumbnail from OS for " + fileName);
                }
                else
                {
                    Console.WriteLine("\tSuccessfully obtained thumbnail from OS for " + fileName);
                }
                return result;
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        private MagickImage? GetMagickThumbnailFromOS(IntPtr pidl, int size)
        {
            if (pidl == IntPtr.Zero) return null;

            IntPtr shellItemImageFactory = IntPtr.Zero;

            try
            {
                string? fileName = CPidl.ToString(pidl);
#if DEBUG
                var length = CPidl.SegmentCount(pidl);
                Console.WriteLine("\tRequesting thumbnail from OS: " + fileName);
#endif
                Guid iid = ShellAPI.IID_IShellItemImageFactory;
                int hr = ShellAPI.SHCreateItemFromIDList(pidl, ref iid, out shellItemImageFactory);
                if (hr != 0 || shellItemImageFactory == IntPtr.Zero) return null;

                var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(shellItemImageFactory);

                var result = GetThumbnailFromOsBaseMagick(factory, size);
                if (result == null)
                {
                    Console.WriteLine("Failed to get thumbnail from OS for " + fileName);
                }
                else
                {
                    Console.WriteLine("\tSuccessfully obtained thumbnail from OS for " + fileName);
                }
                return result;
            }
            finally
            {
                if (shellItemImageFactory != IntPtr.Zero) Marshal.Release(shellItemImageFactory);
            }
        }

        private static MagickImage? GetThumbnailFromOsBaseMagick(IShellItemImageFactory factory, int size)
        {
            int hr;
            IntPtr hbm = IntPtr.Zero;

            try
            {
                uint flags = (uint)ShellAPI.SIIGBF.THUMBNAILONLY;
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);

                if (hr != 0 || hbm == IntPtr.Zero) //in case of failure, fallback to get icon instead of thumbnail
                {
                    flags = (uint)ShellAPI.SIIGBF.ICONONLY;
                    hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero)
                    {
                        Console.WriteLine("Failed to get image from shell item factory");
                        return null;
                    }
                }

                var image = BitmapHelper.HBitmapToMagickImage(hbm);
                if (image == null) return null;
                return ApplyLetterboxMagick(image, size);
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
        public bool TryGetCachedThumbnail(string filePath, int size, out Image? thumbnail)
        {
            if (_thumbnailCache.TryGetValue(ConstructCacheKey(filePath, size), out byte[]? bytes))
            {
                thumbnail = BytesToBitmap(bytes, size);
                return true;
            }
            thumbnail = null;
            return false;
        }



        /// <summary>
        /// Scales and pads a source image to fit within a square of the given size, preserving
        /// aspect ratio, and centers it on a transparent background using ImageMagick.
        /// </summary>
        private static MagickImage ApplyLetterboxMagick(MagickImage source, int size)
        {
            // Resize to fit inside the square while preserving aspect ratio
            source.Resize(new MagickGeometry((uint)size, (uint)size) { IgnoreAspectRatio = false });

            // Set background to transparent and extent to square size, centering the image
            source.BackgroundColor = MagickColors.Transparent;
            source.Extent((uint)size, (uint)size, Gravity.Center);

            return source;
        }

        /// <summary>
        /// Prunes active tasks.  
        /// If thorough is false, it will just immediately return as soon as it finds 1 still active task.
        /// </summary>
        /// <param name="thorough"></param>
        private void PruneActiveTasks(bool thorough = true)
        {
            lock (_activeTasks)
            {
                var currentNode = _activeTasks.First;

                while (currentNode != null)
                {
                    var nextNode = currentNode.Next; // Cache the next node
                    var task = currentNode.Value;

                    if (task.IsCompleted || task.IsCanceled)
                    {
                        _activeTasks.Remove(currentNode);
                    }
                    else if (!thorough)
                    {
                        break; // Early exit for non-thorough pruning
                    }

                    currentNode = nextNode;
                }
            }
        }


        #endregion 


        #region P/Invoke Declarations


        #endregion

    }


}
