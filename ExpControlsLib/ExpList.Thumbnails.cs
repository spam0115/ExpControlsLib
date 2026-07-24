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
        /// <see cref="LoadImagesForItems"/> is called to populate thumbnail images.
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
        private void LoadImagesForRange(int index, int endIndex = -1)
        {
            Debug.WriteLine("ExpList: LoadImagesForRange Begin");
            try
            {
                LoadImagesAtIndexes(index, endIndex);
            }
            finally
            {
                //Debug.WriteLine("ExpList: LoadImagesForRange End");
            }
        }

        private void LoadImagesForVisibleItems()
        {
            Debug.WriteLine("ExpList: LoadImagesForVisibleItems Begin");
            try
            {
                LoadImagesForItems(true);
            }
            finally
            {
                //Debug.WriteLine("ExpList: LoadImagesForItems End");
            }
        }

        /// <summary>
        /// Loads the appropriate image for each item based on the current display mode.
        /// Can either load all images or only images near the visible section.
        /// </summary>
        /// <param name="onlyVisible">true if only images near the visible items should be loaded.</param>
        private void LoadImagesForItems(bool onlyVisible = false)
        {
            Debug.WriteLine("ExpList: LoadImagesForItems Begin");
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
                        int topIndex = 0;

                        //Preload downwards first, upwards later.
                        if (onlyVisible)
                        {
                            topIndex = _listViewWrapper.GetTopIndex();
                            startIndex = topIndex;
                            _approxCountPerPage = _listViewWrapper.GetApproxVisibleCount();
                            // Preload two pages below, then one page above, for smoother scrolling.
                            endIndex = Math.Min(_listViewWrapper.Count - 1, topIndex + _approxCountPerPage * 2 - 1);
                        }

                        LoadImagesForVirtualRange(startIndex, endIndex);

                        if (onlyVisible)
                        {
                            // also preload backwards
                            startIndex = Math.Max(0, topIndex - _approxCountPerPage);
                            endIndex = Math.Max(0, topIndex - 1);
                            LoadImagesForVirtualRange(startIndex, endIndex);
                        }
                    }
                    else
                    {
                        Rectangle clientRect = _listView.ClientRectangle;
                        clientRect.Inflate(0, clientRect.Height);

                        foreach (ListViewItem item in _listView.Items)
                        {
                            if (item is null) continue;
                            if (!clientRect.IntersectsWith(item.Bounds)) continue;
                            if (onlyVisible && item.ImageIndex != -1) continue;

                            if (item.Tag is CShellItem csi && !string.IsNullOrWhiteSpace(csi.FullPath))
                            {
                                int imageIndex = _imageListOrchestrator.EnsureImage(csi);
                                if (imageIndex != -1)
                                    item.ImageIndex = imageIndex;
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
                Debug.WriteLine("ExpList: LoadImagesForItems End");
            }
        }

        private void LoadImagesForVirtualRange(int startIndex, int endIndex)
        {
            for (int i = startIndex; i <= endIndex; i++)
                RequestImageAtIndex(i);
        }

        /// <summary>Requests the appropriate image for a virtual item at the given index.</summary>
        private bool RequestImageAtIndex(int i)
        {
            var csi = GetItem(i);
            if (csi is null)
            {
                Debug.WriteLine($"LoadImagesForItems: GetItem returned null for index {i}");
                return false;
            }
            int oldImageIndex = csi.ImageIndex;
            int imageIndex = _imageListOrchestrator.EnsureImage(csi, i);
            if (imageIndex != -1)
                csi.ImageIndex = imageIndex;

            if (oldImageIndex != csi.ImageIndex)
            {
                var lvi = _listViewWrapper.GetLviForVirtualItem(i);

                if (lvi is null)
                {
                    Debug.WriteLine($"LoadImagesForItems: GetItemInternal returned null for index {i}");
                    return false;
                }

                lvi.ImageIndex = csi.ImageIndex;
                _listView.RedrawItems(i, i, false);
            }

            return true;
        }

        private void LoadImagesAtIndexes(int startIndex, int endIndex = -1)
        {
            Debug.WriteLine("ExpList: LoadImagesAtIndexes Begin");
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
                        LoadImagesForVirtualRange(startIndex, endIndex);
                    }
                    else
                    {
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var item = _listView.Items[i];
                            if (item is null) continue;

                            if (item.Tag is CShellItem csi && !string.IsNullOrWhiteSpace(csi.FullPath))
                            {
                                int imageIndex = _imageListOrchestrator.EnsureImage(csi);
                                if (imageIndex != -1)
                                    item.ImageIndex = imageIndex;
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
                Debug.WriteLine("ExpList: LoadImagesAtIndexes End");
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
            LoadImagesForRange(index);

            if (VirtualMode)
                _listViewWrapper._listView.EnsureVisible(index);
            else
                _listViewWrapper._listView.Items[index].EnsureVisible();
        }

        #endregion
    }
}
