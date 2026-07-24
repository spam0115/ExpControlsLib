using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.Versioning;
using WindowsApiLib;
using WindowsApiLib.Shell;
using MethodInvoker = System.Windows.Forms.MethodInvoker;

namespace ExpControlsLib
{
    /// <summary>Manages lazy icon and thumbnail loading, image-list updates, and visible-item refreshes.</summary>
    [SupportedOSPlatform("windows")]
    public partial class ExpList
    {
        #region Lazy Thumbnail Loading Support

        /// <summary>
        /// Configures the image lists bound to the ListView for the given display mode.
        /// For built-in Windows view modes (Details, List, LargeIcon, Tile), the system image
        /// list is applied and each item's <see cref="ListViewItem.ImageIndex"/> is refreshed.
        /// For custom thumbnail modes, the ListView is switched to LargeIcon view and
        /// <see cref="LoadThumbnailsForItems"/> is called to populate thumbnail images.
        /// </summary>
        /// <param name="value">The <see cref="ListViewDisplayMode"/> to configure for.</param>
        private void SetImageListForMode(ListViewDisplayMode value)
        {
            // DisplayMode can be assigned by the designer before the control's Load event.
            // Defer native/managed image-list installation until the orchestrator exists.
            if (_imageListOrchestrator == null) return;

            Debug.WriteLine("ExpList: SetAndLoadImageList Begin");
            try
            {
                EnterImageListMutation();
                try
                {
                    _imageListOrchestrator.ApplyAppropriateImageList(value);
                }
                finally
                {
                    ExitImageListMutation();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: SetAndLoadImageList End");
            }
        }
        private void LoadImageAtIndex(int index, int endIndex = -1)
        {
            Debug.WriteLine("ExpList: LoadImageAtIndex Begin");
            try
            {
                _imageListOrchestrator.LoadImageAtIndex(
                    index,
                    endIndex,
                    () => LoadIconsForItems(index, endIndex),
                    () => LoadThumbnailsAtIndexes(index, endIndex));
            }
            finally
            {
                //Debug.WriteLine("ExpList: LoadImageAtIndex End");
            }
        }

        private void LoadImagesForVisibleItems(ListViewDisplayMode? mode = null)
        {
            Debug.WriteLine("ExpList: LoadImagesForItems Begin");
            try
            {
                mode = mode == null ? DisplayMode : mode;

                _imageListOrchestrator.LoadImagesForVisibleItems(
                    () => LoadIconsForItems(true),
                    () => LoadThumbnailsForItems(GetThumbnailSizeForMode(mode), true));
            }
            finally
            {
                //Debug.WriteLine("ExpList: LoadImagesForItems End");
            }
        }

        /// <summary>
        /// loads icons (not thumbnails) for the items in the list.
        /// Can either load all icons or only icons near the visible section.
        /// </summary>
        /// <param name="onlyVisible">true if you only want icons near the visible items.</param>
        private void LoadIconsForItems(bool onlyVisible = false)
        {
            Debug.WriteLine("ExpList: LoadIconsForItems Begin");
            try
            {
                if (!_listView.IsHandleCreated) return;

                EnterListViewEnumeration();
                try
                {
                    if (VirtualMode)
                    {
                        int startIndex = 0;
                        int endIndex = _listViewWrapper.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = _listViewWrapper.GetTopIndex();
                            _approxCountPerPage = _listViewWrapper.GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex - _approxCountPerPage);
                            endIndex = Math.Min(_listViewWrapper.Count - 1, topIndex + _approxCountPerPage * 2);
                        }

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = GetItem(i);
                            if (csi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItem returned null for index {i}");
                                continue;
                            }
                            int oldImageIndex = csi.ImageIndex;
                            csi.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(csi);

                            var lvi = _listViewWrapper.GetLviFromVirtual(i);

                            if (lvi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItemInternal returned null for index {i}");
                                continue;
                            }

                            if (oldImageIndex != csi.ImageIndex)
                            {
                                lvi.ImageIndex = csi.ImageIndex;
                                _listView.RedrawItems(i, i, false);
                            }
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;

                        foreach (ListViewItem item in _listView.Items)
                        {
                            if (item is null) continue;
                            if (!clientRect.IntersectsWith(item.Bounds)) continue;

                            if (item.Tag is CShellItem csi && item.ImageIndex == -1)
                            {
                                item.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(csi);
                            }
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: LoadIconsForItems End");
            }
        }

        private void LoadIconsForItems(int startIndex, int endIndex = -1)
        {
            Debug.WriteLine("ExpList: LoadIconsForItems Begin");
            try
            {
                if (!_listView.IsHandleCreated) return;

                EnterListViewEnumeration();
                try
                {
                    if (endIndex == -1) endIndex = startIndex;
                    endIndex = Math.Min(_listViewWrapper.Count - 1, endIndex);

                    if (VirtualMode)
                    {
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = GetItem(i);
                            if (csi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItem returned null for index {i}");
                                continue;
                            }
                            int oldImageIndex = csi.ImageIndex;
                                csi.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(csi);

                            var lvi = _listViewWrapper.GetLviFromVirtual(i);

                            if (lvi is null)
                            {
                                Debug.WriteLine($"LoadIconsForItems: GetItemInternal returned null for index {i}");
                                continue;
                            }

                            if (oldImageIndex != csi.ImageIndex)
                            {
                                lvi.ImageIndex = csi.ImageIndex;
                                _listView.RedrawItems(i, i, false);
                            }
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var item = _listView.Items[i];
                            if (item is null) continue;

                            if (item.Tag is CShellItem csi && item.ImageIndex == -1)
                            {
                                item.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(csi);
                            }
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: LoadIconsForItems End");
            }
        }

        /// <summary>
        /// Gets the pixel size for a given thumbnail display mode
        /// </summary>
        private int GetThumbnailSizeForMode(ListViewDisplayMode? mode = null)
        {
            //Debug.WriteLine("ExpList: GetThumbnailSizeForMode Begin");
            try
            {
                mode ??= DisplayMode;
                return mode switch
                {
                    ListViewDisplayMode.Thumbnail => 48,
                    ListViewDisplayMode.LargeThumbnail => 96,
                    ListViewDisplayMode.ExtraLargeThumbnail => 256,
                    _ => 48 // Default to 48 for non-thumbnail modes, though this should never be used
                };
            }
            finally
            {
                //Debug.WriteLine("ExpList: GetThumbnailSizeForMode End");
            }
        }

        /// <summary>
        /// loads thumbnails (not icons) for the items in the list.
        /// Can either load all thumbnails or only some thumbnails near the visible section.
        /// </summary>
        /// <param name="thumbnailSize">The size of the thumbnails to load.</param>
        /// <param name="onlyVisible">If true, only loads thumbnails for items currently visible in the viewport that don't already have one.</param>
        private void LoadThumbnailsForItems(int thumbnailSize, bool onlyVisible = false)
        {
            Debug.WriteLine("ExpList: LoadThumbnailsForItems Begin");

            try
            {
                if (!_listView.IsHandleCreated) return;

                EnterListViewEnumeration();
                try
                {
                    if (VirtualMode)
                    {
                        int startIndex = 0, backFill = 0;
                        int endIndex = _listViewWrapper.Count - 1;

                        if (onlyVisible)
                        {
                            int topIndex = _listViewWrapper.GetTopIndex();
                            _approxCountPerPage = _listViewWrapper.GetApproxVisibleCount();
                            // Use a reasonable buffer (1 page above/below) for smoother scrolling
                            startIndex = Math.Max(0, topIndex);
                            endIndex = Math.Min(_listViewWrapper.Count - 1, topIndex + _approxCountPerPage * 2);
                            backFill = startIndex - _approxCountPerPage / 2; // if user scrolls up, we want to have thumbnails ready for the previous page
                        }

                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = _listViewWrapper.GetItem(i);
                            if (_imageListOrchestrator.EnsureImage(csi, i) != -1) continue;
                            Debug.WriteLine("ExpList: thumbnailManager.RequestThumbnail: " + i.ToString());
                        }

                        backFill = backFill < 0 ? 0 : backFill;
                        for (int i = backFill; i < startIndex; i++)
                        {
                            var csi = _listViewWrapper.GetItem(i);
                            if (csi is null)
                            {
                                Debug.WriteLine($"LoadThumbnailsForItems: GetItem returned null for index {i}");
                                continue;
                            }

                            if (_imageListOrchestrator.EnsureImage(csi, i) != -1) continue;
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;
                        clientRect.Inflate(0, clientRect.Height); // buffer zone

                        foreach (ListViewItem item in _listView.Items)
                        {
                            if (item is null) continue;
                            if (onlyVisible && item.ImageIndex != -1) continue;
                            if (!clientRect.IntersectsWith(item.Bounds)) continue;

                            if (item.Tag is CShellItem csi && !string.IsNullOrWhiteSpace(csi.FullPath))
                                _imageListOrchestrator.EnsureImage(csi);
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }

            }
            finally
            {
                Debug.WriteLine("ExpList: LoadThumbnailsForItems End");
            }
        }

        private void LoadThumbnailsAtIndexes(int startIndex, int endIndex = -1)
        {
            Debug.WriteLine("ExpList: LoadThumbnailsAtIndexes Begin");

            try
            {
                if (!_listView.IsHandleCreated) return;

                EnterListViewEnumeration();
                try
                {
                    if (endIndex == -1) endIndex = startIndex;
                    endIndex = Math.Min(_listViewWrapper.Count - 1, endIndex);

                    if (VirtualMode)
                    {
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var csi = _listViewWrapper.GetItem(i);
                            if (_imageListOrchestrator.EnsureImage(csi, i) != -1) continue;
                            Debug.WriteLine("ExpList: thumbnailManager.RequestThumbnail: " + i.ToString());
                        }
                    }
                    else
                    {
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var item = _listView.Items[i];
                            if (item is null) continue;

                            if (item.Tag is CShellItem csi && !string.IsNullOrWhiteSpace(csi.FullPath))
                                _imageListOrchestrator.EnsureImage(csi);
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: LoadThumbnailsAtIndexes End");
            }
        }

        private ListViewScrollHook _scrollHook;
        /// <summary>
        /// The _thumbnailTimer is a debounce timer used to implement Lazy Loading. Even though the actual thumbnail generation
        ///happens on background threads(via ThumbnailProvider), the timer is essential for maintaining UI performance and
        ///efficiency.
        ///Here is why it's necessary:
        ///1. Preventing "Scroll Stutter"
        ///The ListView fires dozens of scroll events per second during rapid scrolling.If the app tried to calculate which
        ///items are visible on every single event, the UI thread would "stutter" because it's spending too much time doing
        ///geometry calculations instead of rendering the list. The timer waits for a 200ms pause in scrolling before doing this
        ///calculation.
        /// </summary>
        private System.Windows.Forms.Timer? _scrollDebounceTimer;

        /// <summary>
        /// Hook for capturing scroll and other events from the ListView to trigger lazy loading.
        /// </summary>
        private class ListViewScrollHook : NativeWindow
        {
            private readonly Action _onScroll;
            private readonly ListView _listView;
            private readonly VirtualListViewWrapper _listViewWrapper;

            public ListViewScrollHook(VirtualListViewWrapper listView, Action onScroll)
            {
                Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook Begin");
                try
                {
                    _onScroll = onScroll;
                    _listViewWrapper = listView;
                    _listView = _listViewWrapper._listView;
                    AssignHandle(_listView.Handle);
                }
                finally
                {
                    Debug.WriteLine("ExpList.ListViewScrollHook: ListViewScrollHook End");
                }
            }

            protected override void WndProc(ref Message m)
            {
                //Debug.WriteLine("ExpList.WndProc Begin");
                try
                {
                    try
                    {
                        base.WndProc(ref m); //must call before exit or you will get form creation errors.
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        _listView.SelectedIndices.Clear();
                    }

                    if (m.Msg == WindowsMessages.WM_QUERYENDSESSION || m.Msg == WindowsMessages.WM_ENDSESSION || m.Msg == WindowsMessages.WM_CLOSE) // || m.Msg == WindowsMessages.WM_NCDESTORY WM_NCDESTORY get's called during startup
                        _listViewWrapper.IsShuttingDown = true;

                    if (_listViewWrapper.IsShuttingDown) return;

                    switch (m.Msg)
                    {
                        case WindowsMessages.WM_VSCROLL:
                        case WindowsMessages.WM_HSCROLL:
                        case WindowsMessages.WM_MOUSEWHEEL:
                            _listViewWrapper.LastTopIndex = -1; //invalid due to a scroll moving items
                            QueueOnScroll();
                            break;
                        case WindowsMessages.WM_KEYDOWN:
                            Keys key = (Keys)m.WParam.ToInt32();
                            if (key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || key == Keys.Up || key == Keys.Down)
                            {
                                //the problem with the arrow keys is we don't have a test yet to see if the navigation movement stayed with the list of visible items or moved to a non-visible item
                                _listViewWrapper.LastTopIndex = -1; //invalid due to a scroll moving items
                                QueueOnScroll();
                            }
                            break;
                    }
                }
                finally
                {
                    //Debug.WriteLine("ExpList.WndProc End");
                }
            }

            private int _scrollQueued;
            private void QueueOnScroll()
            {
                Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll Begin");
                try
                {
                    if (_listView.IsDisposed || !_listView.IsHandleCreated) return;
                    if (System.Threading.Interlocked.Exchange(ref _scrollQueued, 1) == 1) return;

                    _listView.BeginInvoke((MethodInvoker)(() =>
                    {
                        System.Threading.Interlocked.Exchange(ref _scrollQueued, 0);
                        if (!_listView.IsDisposed) _onScroll?.Invoke();
                    }));
                }
                finally
                {
                    Debug.WriteLine("ExpList.ListViewScrollHook: QueueOnScroll End");
                }
            }
        }

        private void OnScroll()
        {
            Debug.WriteLine("ExpList: OnListViewScroll Begin");
            if (IsShuttingDown) return;
            try
            {
                //issues a new request to get thumbnails after a brief debounce delay
                _scrollDebounceTimer?.Stop();
                _scrollDebounceTimer?.Start();
            }
            finally
            {
                //Debug.WriteLine("ExpList: OnListViewScroll End");
            }
        }

        public void EnsureVisible(int index)
        {
            LoadImageAtIndex(index);

            if (VirtualMode)
                _listViewWrapper._listView.EnsureVisible(index);
            else
                _listViewWrapper._listView.Items[index].EnsureVisible();
        }

        #endregion
    }
}
