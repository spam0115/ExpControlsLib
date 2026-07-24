using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>Contains private helpers used to materialize, search, filter, sort, and update list items.</summary>
    [SupportedOSPlatform("windows")]
    public partial class ExpList
    {
        #region Private Methods

        private bool IsExcluded(CShellItem item)
        {
            if (_excludedItems.Count == 0 || item == null) return false;
            var path = (item.FullPath ?? "").Trim(':', '{', '}');
            return _excludedItems.Contains(path);
        }

        /// <summary>
        /// Creates a <see cref="ListViewItem"/> for a given <see cref="CShellItem"/>.
        /// Populates columns based on <see cref="ExpListGetColumnData"/> event or <see cref="ColumnHeader.Tag"/> mapping.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to create the list view item for.</param>
        /// <returns>A configured <see cref="ListViewItem"/>.</returns>
        private ListViewItem CreateListviewItemCallback(CShellItem item)
        {
            try
            {
                if (item == null) return new ListViewItem("Error: no CShellItem provided to CreateListviewItemCallback()");

                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList: CreateListviewItemCallback Begin - " + item.DisplayName);

                ListViewItem lvi = new ListViewItem(item.DisplayName);

                UpdateLviUsingCsiData(lvi, item);

                return lvi;
            }
            finally
            {
                //Debug.WriteLine("ExpList: MakeLVItem End");
            }
        }
        
        private bool _refreshing = false; //This variable is prevent reentrancy problems on the ui thread
        private bool _refreshPending = false;
        private bool _refetchImages = false;
        private ListViewItem[]? _pendingItems = null;

        /// <summary>
        /// Increments the enumeration depth counter. While depth > 0, DoItemUpdate will
        /// defer shell item modifications to prevent reentrant mutation of _listView.Items.
        /// Must be paired with <see cref="ExitListViewEnumeration"/>.
        /// </summary>
        private void EnterListViewEnumeration()
        {
            Debug.WriteLine("ExpList: EnterListViewEnumeration Begin");
            try
            {
                _enumerationDepth++;
            }
            finally
            {
                Debug.WriteLine("ExpList: EnterListViewEnumeration End");
            }
        }

        /// <summary>
        /// Decrements the enumeration depth counter. When it reaches 0, any deferred
        /// shell item updates are drained and applied.
        /// Must be paired with <see cref="EnterListViewEnumeration"/>.
        /// </summary>
        private void ExitListViewEnumeration()
        {
            Debug.WriteLine("ExpList: ExitListViewEnumeration Begin");
            try
            {
                _enumerationDepth--;
                if (_enumerationDepth <= 0)
                {
                    _enumerationDepth = 0;
                    DrainDeferredUpdates();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExitListViewEnumeration End");
            }
        }

        /// <summary>
        /// Processes all deferred shell item updates that were queued while an enumeration was in progress.
        /// </summary>
        private void DrainDeferredUpdates()
        {
            Debug.WriteLine("ExpList: DrainDeferredUpdates Begin");
            try
            {
                while (_deferredUpdates.Count > 0)
                {
                    var (sender, e) = _deferredUpdates.Dequeue();
                    ShellUpdater_UpdateEventHandler(sender, e);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: DrainDeferredUpdates End");
            }
        }

        /// <summary>
        /// Increments the image list mutation depth counter. While depth > 0, 
        /// ThumbnailManager_ThumbnailReady will defer image list modifications 
        /// to prevent reentrancy during OS draw cycles.
        /// Must be paired with <see cref="ExitImageListMutation"/>.
        /// </summary>
        internal void EnterImageListMutation()
        {
            _imageListMutationDepth++;
        }

        /// <summary>
        /// Decrements the image list mutation depth counter. When it reaches 0,
        /// any deferred thumbnail updates are drained. The drain is posted to the
        /// UI message pump via <see cref="BeginInvoke(Action)"/> rather than run
        /// inline, so it executes on a clean pump cycle outside of any in-flight
        /// <c>RetrieveVirtualItem</c> / WM_PAINT call stack. This prevents
        /// <see cref="System.Windows.Forms.ListView.RedrawItems"/> calls issued by
        /// the drain from being coalesced away by the ListView's reentrant draw,
        /// which previously left thumbnails visually blank until a click forced a
        /// synchronous repaint.
        /// Must be paired with <see cref="EnterImageListMutation"/>.
        /// </summary>
        internal void ExitImageListMutation()
        {
            _imageListMutationDepth--;
            if (_imageListMutationDepth <= 0)
            {
                _imageListMutationDepth = 0;
                ScheduleDeferredThumbnailDrain();
            }
        }

        /// <summary>
        /// Schedules a single <see cref="DrainDeferredThumbnailUpdates"/> call on the
        /// UI thread's message pump. If a drain is already scheduled, this is a no-op,
        /// so callers may invoke it freely without flooding the message queue.
        /// </summary>
        private void ScheduleDeferredThumbnailDrain()
        {
            if (_drainScheduled) return;
            if (_deferredThumbnailUpdates.Count == 0) return;
            if (IsDisposed || !IsHandleCreated) return;

            _drainScheduled = true;
            BeginInvoke(new Action(() =>
            {
                _drainScheduled = false;
                DrainDeferredThumbnailUpdates();
            }));
        }

        /// <summary>
        /// Processes all deferred thumbnail updates that were queued while an image list 
        /// mutation guard was active. Must run on the UI thread outside of any ListView
        /// draw/retrieve callback; use <see cref="ScheduleDeferredThumbnailDrain"/> to
        /// enqueue it safely.
        /// </summary>
        private void DrainDeferredThumbnailUpdates()
        {
            // Drain in a loop so that any updates deferred by re-entrancy during the
            // drain itself (e.g. if a RetrieveVirtualItem fires while we are mutating
            // the image list) are also processed before we return.
            while (_deferredThumbnailUpdates.Count > 0)
            {
                var (sender, e) = _deferredThumbnailUpdates.Dequeue();
                ThumbnailManager_ThumbnailReady(sender, e);
            }
        }

        /// <summary>
        /// Executes the action immediately if no enumeration is in progress, otherwise
        /// defers it via BeginInvoke to run after the enumeration completes.
        /// Use this for ListView modification operations outside of DoItemUpdate.
        /// </summary>
        private void InvokeWhenListViewReady(Action action)
        {
            Debug.WriteLine("ExpList: InvokeWhenListViewReady Begin");
            try
            {
                if (_enumerationDepth > 0)
                {
                    BeginInvoke(() => InvokeWhenListViewReady(action));
                    return;
                }
                action();
            }
            finally
            {
                Debug.WriteLine("ExpList: InvokeWhenListViewReady End");
            }
        }

        /// <summary>
        /// Launches a file using the default system handler.
        /// </summary>
        /// <param name="csi">The <see cref="CShellItem"/> to launch.</param>
        private void LaunchFile(CShellItem csi)
        {
            Debug.WriteLine("ExpList: LaunchFile Begin");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = csi.FullPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            finally
            {
                Debug.WriteLine("ExpList: LaunchFile End");
            }
        }

        /// <summary>
        /// Determines if the mouse coordinates are within the client area of the specified control.
        /// </summary>
        /// <param name="ctl">The control to check.</param>
        /// <param name="e">The <see cref="MouseEventArgs"/> containing the mouse position.</param>
        /// <returns>True if the mouse is within the control's client area.</returns>
        private bool IsWithin(Control ctl, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: IsWithin Begin");
            try
            {
                if (e.X < 0 || e.Y < 0) return false;
                Rectangle cr = ctl.ClientRectangle;
                if (e.X > cr.Width || e.Y > cr.Height) return false;
                return true;
            }
            finally
            {
                Debug.WriteLine("ExpList: IsWithin End");
            }
        }

        /////// <summary>
        /////// Sorts the items in the list view based on their tags (CShellItem).
        /////// </summary>
        //private void SortLVItems()
        //{
        //    Debug.WriteLine("ExpList: SortLVItems Begin");
        //    try
        //    {
        //        if (VirtualMode)
        //        {
        //            if (_listView.ListViewItemSorter is LVColSorter sorter)
        //            {
        //                _listViewWrapper.Sort(sorter.SortColumn, sorter.OrderOfSort);
        //            }
        //            return;
        //        }

        //        if (_listView.Items.Count < 2) return;

        //        EnterListViewEnumeration();
        //        try
        //        {
        //            _listView.BeginUpdate();
        //            var tmp = new ListViewItem[_listView.Items.Count];
        //            _listView.Items.CopyTo(tmp, 0);
        //            Array.Sort(tmp, new TagComparer());
        //            _listView.Items.Clear();
        //            _listView.Items.AddRange(tmp);
        //            _listView.EndUpdate();
        //        }
        //        finally
        //        {
        //            ExitListViewEnumeration();
        //        }
        //    }
        //    finally
        //    {
        //        Debug.WriteLine("ExpList: SortLVItems End");
        //    }
        //}

        #endregion
    }
}
