using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Coordinates the image-list implementation used by <see cref="ExpList"/>.
    /// System image-list operations remain delegated to <see cref="SystemImageListManager"/>,
    /// while thumbnail operations are owned by an instance-scoped manager.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ImageListOrchestrator : IDisposable
    {
        private readonly ExpList _expList;
        private readonly ListView _listView;
        private readonly ThumbnailImageListManager _thumbnailManager;
        private bool _disposed;

        public ImageListOrchestrator(
            ExpList expList,
            ListView listView)
        {
            if (expList == null) throw new ArgumentNullException(nameof(expList));
            _expList = expList;
            _listView = listView ?? throw new ArgumentNullException(nameof(listView));
            _thumbnailManager = new ThumbnailImageListManager(expList, GetThumbnailSize(CurrentMode));
            _thumbnailManager.ThumbnailReady += OnThumbnailReady;
        }

        public event EventHandler<ThumbnailReadyEventArgs> ThumbnailReady;

        public ListViewDisplayMode CurrentMode => _expList.DisplayMode;

        public bool IsThumbnailMode => IsThumbnailModeFor(CurrentMode);

        public int ActiveThumbnailSize => GetThumbnailSize(CurrentMode);

        /// <summary>
        /// Applies the appropriate image list to the ListView based on the current display mode.
        /// Either a system image list or a managed thumbnail image list is installed.
        /// </summary>
        /// <param name="mode"></param>
        public void ApplyAppropriateImageList(ListViewDisplayMode mode)
        {
            if (IsThumbnailModeFor(mode))
            {
                _thumbnailManager.SetExpListLargeImageList(GetThumbnailSize(mode));
                return;
            }

            // SystemImageListManager installs native handles with SendMessage. Clear the
            // WinForms properties first so a later property synchronization cannot restore
            // a stale thumbnail ImageList handle.
            _listView.LargeImageList = null;
            _listView.SmallImageList = null;

            bool large = mode == ListViewDisplayMode.LargeIcon;
            SystemImageListManager.SetListViewImageList(_listView, large, false);
        }

        /// <summary>
        /// Returns an initial index without starting asynchronous thumbnail work.
        /// This is used by virtual-list callbacks that may be invoked for off-screen items.
        /// </summary>
        public int GetInitialImageIndex(CShellItem item, bool getOpenIcon = false)
        {
            if (item == null) return -1;
            if (IsThumbnailMode) return -1;

            return SystemImageListManager.GetIconIndex(item, getOpenIcon);
        }

        /// <summary>
        /// Gets an icon immediately, or gets/queues the active thumbnail and returns -1 while
        /// the thumbnail request is pending.
        /// </summary>
        public int EnsureImage(CShellItem item, int itemIndex = -1)
        {
            if (item == null) return -1;

            if (IsThumbnailMode)
                return _thumbnailManager.EnsureThumbnail(item, ActiveThumbnailSize, itemIndex);

            return GetInitialImageIndex(item);
        }

        /// <summary>
        /// Refreshes an item after a Shell update. Thumbnail mode queues work; system-icon mode
        /// asks the caller to redraw the item after the synchronous icon lookup.
        /// </summary>
        public void RefreshImage(CShellItem item, int itemIndex, Action redraw)
        {
            if (item == null) return;

            if (IsThumbnailMode)
                _thumbnailManager.EnsureThumbnail(item, ActiveThumbnailSize, itemIndex);
            else
                redraw?.Invoke();
        }

        public int AddThumbnail(ThumbnailReadyEventArgs args, Bitmap thumbnail)
        {
            return _thumbnailManager.AddThumbnail(args, thumbnail);
        }

        public void CancelPendingRequests()
        {
            if (!_disposed)
                _thumbnailManager.CancelPendingRequests();
        }

        /// <summary>
        /// ThumbnailImageListManager.Reset clears all existing sizes of ListView image lists and 
        /// installs a managed thumbnail list.That is only appropriate for thumbnail modes.Standard 
        /// views use the native shell image list;
        /// </summary>
        public void ResetThumbnailImageLists()
        {
            if (IsThumbnailMode)
            {
                _thumbnailManager.Reset();
                return;
            }

            //re-apply the current mode so a folder load cannot replace it with an empty managed list.
            //ApplyMode(_currentMode);
        }

        public void ClearCache() => _thumbnailManager.ClearCache();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _thumbnailManager.ThumbnailReady -= OnThumbnailReady;
            _thumbnailManager.Dispose();
        }

        private void OnThumbnailReady(object? sender, ThumbnailReadyEventArgs e)
        {
            ThumbnailReady?.Invoke(this, e);
        }

        private static bool IsThumbnailModeFor(ListViewDisplayMode mode)
        {
            return mode == ListViewDisplayMode.Thumbnail
                || mode == ListViewDisplayMode.LargeThumbnail
                || mode == ListViewDisplayMode.ExtraLargeThumbnail;
        }

        private static int GetThumbnailSize(ListViewDisplayMode mode)
        {
            return mode switch
            {
                ListViewDisplayMode.Thumbnail => 48,
                ListViewDisplayMode.LargeThumbnail => 96,
                ListViewDisplayMode.ExtraLargeThumbnail => 256,
                _ => 48
            };
        }
    }
}
