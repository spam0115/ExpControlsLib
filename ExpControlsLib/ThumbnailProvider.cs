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
        private readonly LruConcurrentDictionary<string, byte[]> _thumbnailCache = new (100000);

        /// <summary>
        /// queue for holding pending thumbnail requests that will be processed by the background worker.
        /// Do NOT use a bounded, blocking queue.  We need the UI to be responsive at all times.
        /// </summary>
        private StaThreadRunner _requestQueueRunner;

        /// <summary>Cancellation source used to stop the background processor on dispose.</summary>
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private readonly Dictionary<string, Task> _activeTasks = new();
        private readonly object _lifetimeLock = new();
        private readonly List<CancellationTokenSource> _retiredCancellationTokenSources = new();
        private bool _disposed;

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
            _maxThreads = 1;
#else
            _maxThreads = Environment.ProcessorCount;
#endif
            //in testing, moving from 1 to 2 to 4 provided improvements.  Going from 4 to 6 and to 8 both provided a slowdown.
            if (_maxThreads > 4) _maxThreads = 4;
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
            CancellationToken cancellationToken;
            lock (_lifetimeLock)
            {
                if (_disposed) return;
                cancellationToken = _cancellationToken;
            }

            if (ThumbnailReady == null)
            {
                Debug.WriteLine("No subscribers for ThumbnailReady event; skipping thumbnail generation.");
                return;
            }

            string? filePath = reqArgs.FilePath;
            if (string.IsNullOrEmpty(filePath) && reqArgs.Item != null)
                filePath = reqArgs.Item.FullPath;

            if (string.IsNullOrEmpty(filePath))
            {
                Debug.WriteLine("ERROR: EnqueueThumbnailRequest - No file path available.");
                return;
            }

            Debug.WriteLine("EnqueueThumbnailRequest: " + filePath);

            if (reqArgs.Item == null)
            {
                Debug.WriteLine("ERROR: EnqueueThumbnailRequest - No shell item available.");
                return;
            }
            var csi = reqArgs.Item;

            // Check cache first
            if (TryGetCachedThumbnail(filePath, size, out var cachedImage))
            {
#if DEBUG
                Console.WriteLine("\tFound cached thumbnail: " + (csi?.DisplayName ?? filePath));
#endif
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(csi, cachedImage, size, reqArgs.Index));
                return;
            }

            string key = ConstructCacheKey(filePath, size);

            lock (_activeTasks)
            {
                if (_activeTasks.ContainsKey(key))
                {
#if DEBUG
                    Console.WriteLine($"\tDuplicate thumbnail request ignored for: {filePath} ({size}px)");
#endif
                    return;
                }
            }

#if DEBUG
            Console.WriteLine("\tAdding to thumbnail request queue: " + (csi?.DisplayName ?? filePath));
#endif

            Task task;
            lock (_lifetimeLock)
            {
                if (_disposed) return;

                cancellationToken = _cancellationToken;
                task = _requestQueueRunner.EnqueueWork(cancellationToken =>
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    GenerateThumbnailAndNotify(reqArgs);
                }, cancellationToken);
            }

            lock (_activeTasks)
            {
                _activeTasks[key] = task;
            }

            // Automatically remove the task from the active tasks dictionary upon completion
            task.ContinueWith(t =>
            {
                lock (_activeTasks)
                {
                    if (_activeTasks.TryGetValue(key, out var activeTask) && activeTask == t)
                    {
                        _activeTasks.Remove(key);
                    }
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>After cancellation, already-running tasks may still finish and create bitmaps which may not have gdi resources released correctly.</remarks>
        public void CancelPendingRequests()
        {
            lock (_lifetimeLock)
            {
                if (_disposed || _cancellationTokenSource == null) return;

                var previousSource = _cancellationTokenSource;
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
                _retiredCancellationTokenSources.Add(previousSource);
                previousSource.Cancel();
            }

            lock (_activeTasks)
            {
                _activeTasks.Clear();
            }
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
            CancellationTokenSource? currentSource;
            CancellationTokenSource[] retiredSources;
            lock (_lifetimeLock)
            {
                if (_disposed) return;
                _disposed = true;

                currentSource = _cancellationTokenSource;
                _cancellationTokenSource = null;
                retiredSources = _retiredCancellationTokenSources.ToArray();
                _retiredCancellationTokenSources.Clear();
            }

            currentSource?.Cancel();
            _requestQueueRunner.CancelPending();
            _requestQueueRunner.Dispose();

            ClearCache();

            lock (_activeTasks)
            {
                _activeTasks.Clear();
            }

            currentSource?.Dispose();
            foreach (var retiredSource in retiredSources)
                retiredSource.Dispose();
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

                if (ThumbnailReady == null)
                {
                    Debug.WriteLine("ERROR: No subscribers for ThumbnailReady event.  Returning.");
                    return;
                }

                using (var magickImage = GetMagickThumbnailFromOS(request.Item.PIDL, request.Size))
                {
                    if (magickImage == null)
                    {
                        Debug.WriteLine("\tError generating thumbnail for: " + request.Item.DisplayName);
                        return;
                    }
                    // Create the Bitmap FIRST, while the image still has straight
                    // (non-premultiplied) alpha. MagickImage.ToBitmap() returns a
                    // Format32bppArgb bitmap, which expects straight-alpha pixel data.
                    // Calling this after Alpha(Associate) would embed premultiplied
                    // data in a straight-alpha-format bitmap, causing partially-
                    // transparent pixels (e.g. anti-aliased edges) to render too dark.
                    thumbnail = magickImage.ToBitmap();

                    // Store in cache as premultiplied BGRA bytes, paired with
                    // BytesToBitmap's Format32bppPArgb for correct round-tripping.
                    magickImage.Alpha(AlphaOption.Associate);
                    byte[] bytes = magickImage.ToByteArray(MagickFormat.Bgra);
                    _thumbnailCache.TryAdd(ConstructCacheKey(request.Item.FullPath, request.Size), bytes);
                }

                //send event back to the consumer.  Subscriber is responsible for disposing thumbnail.
                ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(request.Item, thumbnail, request.Size, request.Index));
                thumbnail = null; // ownership transferred to subscriber
                Debug.WriteLine("\tThumbnail generation complete: " + request.Item.DisplayName);
                return;
            }
            catch (Exception ex)
            {
                thumbnail?.Dispose();
                thumbnail = null;
                Debug.WriteLine($"Error generating thumbnail for {request.Item.FullPath}: {ex}");
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

        private MagickImage? GetMagickThumbnailFromOsCore(string? fileName, int size, Func<(int hr, IntPtr factoryPtr)> createFactory)
        {
            IntPtr factoryPtr = IntPtr.Zero;
            IShellItemImageFactory? factory = null;

            try
            {
#if DEBUG
                Console.WriteLine("\tRequesting thumbnail from OS: " + fileName);
#endif

                (var hr, factoryPtr) = createFactory();
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                    return null;

                factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);

                var result = GetThumbnailFromOsBaseMagick(factory, size);
                if (result == null)
                    Console.WriteLine("Failed to get thumbnail from OS for " + fileName);
                else
                    Console.WriteLine("\tSuccessfully obtained thumbnail from OS for " + fileName);
                return result;
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
                if (factory != null) Marshal.ReleaseComObject(factory);
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
                Debug.WriteLine($"ERROR: filesystem object does not exist: '{fileName}");
                return null;
            }

            return GetMagickThumbnailFromOsCore(fileName, size, () =>
            {
                Guid iid = ShellAPI.IID_IShellItemImageFactory;
                int hr = ShellAPI.SHCreateItemFromParsingName(fileName, IntPtr.Zero, ref iid, out IntPtr factoryPtr);
                return (hr, factoryPtr);
            });
        }

        private MagickImage? GetMagickThumbnailFromOS(IntPtr pidl, int size)
        {
            if (pidl == IntPtr.Zero) return null;

            string? fileName = CPidl.ToString(pidl);

            return GetMagickThumbnailFromOsCore(fileName, size, () =>
            {
                Guid iid = ShellAPI.IID_IShellItemImageFactory;
                int hr = ShellAPI.SHCreateItemFromIDList(pidl, ref iid, out IntPtr factoryPtr);
                return (hr, factoryPtr);
            });
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

                var image = ImageMagickHelper.HBitmapToMagickImage(hbm);
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




        #endregion 


        #region P/Invoke Declarations


        #endregion

    }


}
