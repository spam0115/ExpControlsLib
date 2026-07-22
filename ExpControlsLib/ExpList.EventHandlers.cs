using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;
using MethodInvoker = System.Windows.Forms.MethodInvoker;

namespace ExpControlsLib
{
    /// <summary>Contains the ListView, keyboard, mouse, label-edit, and other UI event handlers.</summary>
    [SupportedOSPlatform("windows")]
    
    public partial class ExpList
    {
        #region Event Handlers
        private void ExpFileList_Click(object? sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_Click Begin");
            try
            {
                ListView listView = (ListView)sender;

                if (listView.SelectedIndices.Count == 0)
                {
                    ExpListEmptyClick?.Invoke(this, EventArgs.Empty);
                    return;
                }

                CShellItem? csi = null;
                if (listView.FocusedItem != null) //could be selected OR deselected
                {
                    csi = GetItem(listView.FocusedItem.Index);
                    if (csi == null) return;

                    if (csi.ImageIndex == -1)
                    {
                        _imageListOrchestrator.EnsureImage(csi, listView.FocusedItem.Index);
                    }

                    ExpListItemClick?.Invoke(listView.FocusedItem, csi);
                }
                else
                {
                    ExpListItemClick?.Invoke(null, null);
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_Click End");
            }
        }

        /// <summary>
        /// Handles double-click events on list view items. 
        /// Folders are navigated into, while files are launched.
        /// </summary>
        private async void ExpFileList_DoubleClick(object? sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_DoubleClick Begin");
            try
            {
                if (_listView.SelectedIndices.Count <= 0) return;

                CShellItem? csi = null;
                if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                    csi = GetItem(_listView.FocusedItem.Index);
                else
                    csi = GetItem(_listView.SelectedIndices[0]);

                if (csi == null) return;

                if (csi.IsFolder)
                {
                    try
                    {
                        // Navigate into the folder
                        await LoadDirectoryAsync(csi, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error in starting application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (csi.FullPath.StartsWith(":"))
                    ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                else
                    ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_DoubleClick End");
            }
        }

        private void ExpFileList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged Begin");

            if (IsShuttingDown) return;

            try
            {
                if (_listView.SelectedIndices.Count > 0)
                {
                    // If current SelectedItem is still selected, keep it.
                    // This handles the case where multiple items are selected and we don't want to 
                    // jump back to the first one in the list.
                    if (SelectedItem != null && _listViewWrapper.IsItemSelected(SelectedItem))
                    {
                        // keep SelectedItem as is
                    }
                    else if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                    {
                        SelectedItem = GetItem(_listView.FocusedItem.Index);
                    }
                    else
                    {
                        SelectedItem = GetItem(_listView.SelectedIndices[0]);
                    }
                }
                else
                {
                    SelectedItem = null;
                }

                if (VirtualMode)
                {
                    // In virtual mode, SelectedListViewItemCollection is not populated.
                    // Consumers should use SelectedCShellItems property instead.
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine("ExpList: InvalidOperationException in ExpFileList_SelectedIndexChanged: " + ex.ToString());
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine("ExpList: NullReferenceException in ExpFileList_SelectedIndexChanged: " + ex.ToString());
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_SelectedIndexChanged End");
            }
        }

        private void ExpFileList_ItemSelectionChanged(object? sender, ListViewItemSelectionChangedEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged Begin");
            try
            {
                if (e.IsSelected)
                {
                    SelectedItem = GetItem(e.ItemIndex);
                }
                ItemSelectionChanged?.Invoke(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_ItemSelectionChanged End");
            }
        }

        private void DW_DragStart(object? sender, DragStartEventArgs e)
        {
            _listView.SelectedIndices.Clear();
            _pendingDragItems = e.Items;
        }

        private void DW_DragEnd(object? sender, DragEndEventArgs e)
        {
            if (_shellController is null) { _pendingDragItems = null; return; }

            // Case 1: Explicit Move effect (cross-volume moves, or shells that return
            // DROPEFFECT_MOVE for same-volume moves). Unconditionally remove items.
            if (e.DropCompleted && e.Effect == DragDropEffects.Move)
            {
                RemoveItemsFromList(e.Items);
            }
            // Case 2: Optimized same-volume move. The shell returns DROPEFFECT_NONE even
            // though the move succeeded. The Moved shell notification handler may also
            // handle this during the DoDragDrop modal loop, but it's not guaranteed to
            // fire in time (it's dispatched via BeginInvoke from a background thread).
            // Verify the files were actually moved by checking that they no longer exist
            // at the source path. This prevents false removal when dropping on empty space
            // or a target that returns DROPEFFECT_NONE without doing anything.
            else if (e.DropCompleted && e.Effect == DragDropEffects.None)
            {
                var movedItems = new List<CShellItem>(e.Items.Length);
                foreach (var item in e.Items)
                {
                    if (item is null) continue;
                    string path = item.FullPath;
                    if (!string.IsNullOrEmpty(path)
                        && !System.IO.File.Exists(path)
                        && !System.IO.Directory.Exists(path))
                    {
                        movedItems.Add(item);
                    }
                }

                if (movedItems.Count > 0)
                    RemoveItemsFromList(movedItems);
            }

            // Fire ExpListDragCompleted for copy operations (move is handled by the shell notification
            // handler which provides source/destination paths via OldPath/NewPath).
            if (e.DropCompleted && e.Items.Length > 0 && e.Effect == DragDropEffects.Copy)
            {
                ExpListDragCompleted?.Invoke(this, new DragCompletedEventArgs(
                    e.Effect, e.Items,
                    destinationPath: e.DestinationItem?.FullPath,
                    destination: e.DestinationItem));
            }

            // If the shell notification didn't fire for a move (e.g. cross-volume DELETE+CREATE
            // which doesn't go through DoRenameOrMove), fire DragCompleted here as fallback.
            if (e.DropCompleted && _pendingDragItems is not null && _pendingDragItems.Length > 0
                && (e.Effect == DragDropEffects.Move || e.Effect == DragDropEffects.None))
            {
                ExpListDragCompleted?.Invoke(this,
                    new DragCompletedEventArgs(e.Effect, _pendingDragItems,
                        destinationPath: e.DestinationItem?.FullPath,
                        destination: e.DestinationItem));
            }

            _pendingDragItems = null;
        }

        public void RemoveItemsFromList(IEnumerable<CShellItem> items)
        {
            bool useUpdate = items.Count() > _batchThreshold;
            if (useUpdate) _listView.BeginUpdate();
            try
            {
                _listViewWrapper.RemoveItems(items);

                if (items.Count() > this._listViewWrapper.GetApproxVisibleCount())
                    OnScroll();

                if (_currentFolderCsi != null)
                {
                    ExpListItemsChanged?.Invoke(_currentFolderCsi.FullPath, _currentFolderCsi);
                }
            }
            finally
            {
                if (useUpdate) _listView.EndUpdate();
            }
        }

        /// <summary>
        /// Handles the <see cref="Control.Leave"/> event of the <see cref="_listView"/> ListView.
        /// Clears the current selection.
        /// </summary>
        /// what the hell good is this?  It makes it impossible to use any selections to do anything.
        //private void ExpFileList_Leave(object? sender, EventArgs e)
        //{
        //    ExpFileList.SelectedItems.Clear();
        //}

        /// <summary>
        /// Handles the <see cref="ListView.BeforeLabelEdit"/> event.
        /// Determines if an item can be renamed and sets up the edit control.
        /// </summary>
        private void ExpFileList_BeforeLabelEdit(object? sender, LabelEditEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit Begin");
            try
            {
                IntPtr editWnd = SendMessage(_listView.Handle, LVM_GETEDITCONTROL, 0, IntPtr.Zero);
                var csi = GetItem(e.Item);
                if (csi == null) { e.CancelEdit = true; return; }

                int textLen = Path.GetFileNameWithoutExtension(csi.DisplayName).Length;
                SendMessage(editWnd, EM_SETSEL, IntPtr.Zero, (IntPtr)textLen);

                if ((!csi.IsFileSystem) || csi.IsDisk ||
                    csi.FullPath == CShellItemFactory.MyDocuments.FullPath ||
                    !csi.CanRename)
                {
                    System.Media.SystemSounds.Beep.Play();
                    e.CancelEdit = true;
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_BeforeLabelEdit End");
            }
        }

        /// <summary>
        /// Handles the <see cref="ListView.AfterLabelEdit"/> event.
        /// Applies the new name to the shell item.
        /// </summary>
        private void ExpFileList_AfterLabelEdit(object? sender, LabelEditEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit Begin");
            try
            {
                var item = GetItem(e.Item);
                if (item == null || e.Label == null || e.Label == string.Empty) return;

                try
                {
                    string newName = e.Label.Trim();

                    if (newName.Length < 1 || newName.IndexOfAny(Path.GetInvalidPathChars()) != -1)
                    {
                        e.CancelEdit = true;
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }

                    string path = item.FullPath;
                    int index = path.LastIndexOf('\\');
                    if (index == -1)
                    {
                        e.CancelEdit = true;
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }

                    IntPtr newPidl = IntPtr.Zero;
                    if (item.Parent.GetIShellFolder().SetNameOf(
                            _listView.Handle.ToInt32(),
                            CPidl.ILFindLastID(item.PIDL),
                            newName,
                            SHGDN.NORMAL,
                            newPidl) != S_OK)
                    {
                        System.Media.SystemSounds.Beep.Play();
                        e.CancelEdit = true;
                    }
                }
                catch
                {
                    e.CancelEdit = true;
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_AfterLabelEdit End");
            }
        }

        private void ThumbnailManager_ThumbnailReady(object? sender, ThumbnailReadyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => ThumbnailManager_ThumbnailReady(sender, e)));
                else
                    e.Thumbnail?.Dispose();
                return;
            }

            if (e.Size != GetThumbnailSizeForMode()) // if the display mode is changed, the thumbnail will have the wrong size. Discard.
            {
                e.Thumbnail?.Dispose();
                return;
            }

            if (e.Item == null || e.Item.Parent == null || e.Item.Parent.FullPath != CurrentPath)
            {
                e.Thumbnail?.Dispose();
                return;
            }

            // If a draw cycle or another mutation is in progress, defer this update.
            if (_imageListMutationDepth > 0)
            {
                _deferredThumbnailUpdates.Enqueue((sender, e));
                return;
            }

            int image_index = -1;
            try
            {
                EnterImageListMutation();
                if (e.Thumbnail != null)
                {
                    //using (var bitmap = (Bitmap)e.Thumbnail)
                    //{
                    //    image_index = _imageListOrchestrator.AddThumbnail(e, bitmap);
                    //}
                    image_index = _imageListOrchestrator.AddThumbnail(e, (Bitmap)e.Thumbnail);
                    e.Thumbnail.Dispose();
                }
                else
                {
                    image_index = _imageListOrchestrator.AddThumbnail(e, null);
                }
            }
            finally
            {
                ExitImageListMutation();
            }

            if (image_index == -1)
            {
                // Failed to add thumbnail, likely due to disposal or mode change. Just exit.
                Debug.WriteLine("Failed to add thumbnail for item: " + e.Item.DisplayName);
                return;
            }

            if (VirtualMode)
            {
                lock (_listViewWrapper.Items)
                {
                    int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                    if (index == -1)
                    {
                        // Failed to find item in listview, possibly due to deletion or move. Just exit.
                        Debug.WriteLine("Failed to find the item in the listview: " + e.Item.DisplayName);
                        return;
                    }
                    _listViewWrapper.GetItem(index).ImageIndex = image_index;
                    //Debug.WriteLine("Redrawing: " + e.Item.DisplayName);

                    //thumbnails that are provided for items that are offscreen will be drawn by the ListView when
                    //they are brought on screen.  Items that are already onscreen are not redrawn unless done so 
                    //manually here.
                    _listViewWrapper._listView.RedrawItems(index, index, false);
                }
            }
            else
            {
                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                var lvi = _listViewWrapper.GetItem(index);
                if (lvi != null) lvi.ImageIndex = image_index;
            }

        }

        #region Context Menu Handlers

        private readonly ExpControlsLib.ContextMenu m_WindowsContextMenu = new ExpControlsLib.ContextMenu();
        private bool m_OutOfRange;

        /// <summary>
        /// Handles the MouseLeave event to track when the mouse is outside the list view.
        /// </summary>
        private void ExpFileList_MouseLeave(object? sender, EventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseLeave Begin");
            try
            {
                m_OutOfRange = true;
                OnMouseLeave(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseLeave End");
            }
        }

        private void ExpFileList_MouseEnter(object? sender, EventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseEnter Begin");
            try
            {
                OnMouseEnter(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseEnter End");
            }
        }

        /// <summary>
        /// Handles the MouseDown event to reset the out-of-range flag for right-clicks.
        /// </summary>
        private void ExpFileList_MouseDown(object? sender, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_MouseDown Begin");
            try
            {
                if (e.Button == MouseButtons.Right) m_OutOfRange = false;

                // In virtual mode WinForms does not toggle the checkbox glyph when the
                // user clicks it \u2014 ItemCheck/ItemChecked only fire in non-virtual mode.
                // Detect a click on the state-image (checkbox) region and toggle manually.
                if (e.Button == MouseButtons.Left
                    && _listView.CheckBoxes
                    && _listViewWrapper.VirtualMode)
                {
                    var hit = _listView.HitTest(e.X, e.Y);
                    if (hit.Item != null && hit.Location == ListViewHitTestLocations.StateImage)
                    {
                        var csi = _listViewWrapper.GetShellItemAtViewIndex(hit.Item.Index);
                        if (csi != null)
                        {
                            SetChecked(csi, !csi.Checked);
                        }
                    }
                }

                OnMouseDown(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_MouseDown End");
            }
        }

        /// <summary>
        /// Handles the MouseUp event to trigger context menus or middle-click actions.
        /// </summary>
        private async void ExpFileList_MouseUp(object? sender, MouseEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_MouseUp Begin");
            try
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (!IsWithin(_listView, e)) return;
                    if (m_OutOfRange) return;

                    Point pt = new Point(e.X, e.Y);
                    ListViewItem tn = _listView.GetItemAt(e.X, e.Y);

                    if (tn != null && _listView.SelectedIndices.Count > 0)
                    {
                        var itms = SelectedCShellItems.ToArray();

                        CMInvokeCommandInfoEx cmi;
                        bool allowRename = itms.Length <= 1; //Don't allow rename of more than 1 item

                        var menuResult = m_WindowsContextMenu.ShowMenu(Handle, itms, MousePosition, allowRename, MinimalContextMenu);
                        if (menuResult.Success)
                        {
                            int verbId = menuResult.CommandInfo.lpVerb.ToInt32();
                            if (verbId == 99999)
                            {
                                ExpListMove?.Invoke(this, new ExpListMoveEventArgs(itms));
                            }
                            else if (verbId == 99998)
                            {
                                ExpListCopy?.Invoke(this, new ExpListCopyEventArgs(itms));
                            }
                            else
                            {
                                string cmdName = menuResult.Verb ?? string.Empty;

                                if (cmdName.Equals("rename"))
                                {
                                    _listView.LabelEdit = true;
                                    tn.BeginEdit();
                                }
                                else if (cmdName.Equals("delete"))
                                {
                                    DeleteSelectedItems();
                                }
                                else
                                {
                                    IntPtr parentPidl = itms[0].Parent == ShellController.DesktopCSI
                                        ? itms[0].PIDL
                                        : itms[0].Parent.PIDL;
                                    
                                    var capturedRelPidls = itms.Select(i => CPidl.Clone(i.LastPIDL)).ToArray();
                                    var capturedParentPidl = CPidl.Clone(parentPidl);

                                    var invokeCmi = new CMInvokeCommandInfoEx
                                    {
                                        cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                                        nShow = (int)SW.SHOWNORMAL,
                                        fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                                        ptInvoke = pt,
                                        lpVerb = (IntPtr)verbId,
                                        lpVerbW = (IntPtr)verbId
                                    };

                                    await _shellCommandService!.InvokeContextMenuAsync(
                                        capturedParentPidl,
                                        capturedRelPidls,
                                        invokeCmi);
                                }
                            }
                        }
                    }
                    else
                    {
                        ShowAndHandleContextMenu(MousePosition);
                    }
                }

                ExpListItemGetSelItems?.Invoke(_listView.SelectedItems);

                if (e.Button == MouseButtons.Middle && _listView.SelectedIndices.Count > 0)
                {
                    CShellItem? csi = null;
                    if (_listView.FocusedItem != null && _listView.FocusedItem.Selected)
                        csi = GetItem(_listView.FocusedItem.Index);
                    else
                        csi = GetItem(_listView.SelectedIndices[0]);

                    if (csi != null) ExpListItemMouseMBUp?.Invoke(csi.FullPath, csi);
                }
                OnMouseUp(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_MouseUp End");
            }
        }

        private void ExpFileList_MouseMove(object? sender, MouseEventArgs e)
        {
            //Debug.WriteLine("ExpList: ExpFileList_MouseMove Begin");
            try
            {
                OnMouseMove(e);
            }
            finally
            {
                //Debug.WriteLine("ExpList: ExpFileList_MouseMove End");
            }
        }

        #endregion


        /// <summary>
        /// Handles KeyDown events for shortcuts (Ctrl+A, Ctrl+C/V/X, Delete, F2, F5, Enter).
        /// </summary>
        private async void ExpFileList_KeyDown(object? sender, KeyEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyDown Begin");
            try
            {
                // Space toggles the checkbox on focused/selected items in virtual mode
                // (WinForms only handles this natively in non-virtual mode).
                if (e.KeyCode == Keys.Space
                    && !e.Control && !e.Alt && !e.Shift
                    && _listView.CheckBoxes
                    && _listViewWrapper.VirtualMode)
                {
                    bool handled = false;
                    foreach (int idx in _listView.SelectedIndices)
                    {
                        var csi = _listViewWrapper.GetShellItemAtViewIndex(idx);
                        if (csi != null)
                        {
                            SetChecked(csi, !csi.Checked);
                            handled = true;
                        }
                    }
                    if (handled)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        return;
                    }
                }

                if (e.Control && e.KeyCode == Keys.A)
                {
                    if (VirtualMode)
                    {
                        EnterListViewEnumeration();
                        _listView.BeginUpdate();
                        try
                        {
                            for (int i = 0; i < _listView.VirtualListSize; i++)
                                _listView.SelectedIndices.Add(i);
                        }
                        finally
                        {
                            _listView.EndUpdate();
                            ExitListViewEnumeration();
                        }
                    }
                    else
                    {
                        EnterListViewEnumeration();
                        try
                        {
                            foreach (ListViewItem item in _listView.Items)
                            {
                                if (item is null) continue;
                                item.Selected = true;
                            }
                        }
                        finally
                        {
                            ExitListViewEnumeration();
                        }
                    }
                    ExpListItemGetSelItems?.Invoke(_listView.SelectedItems);
                }

                if (e.Control)
                {
                    switch (e.KeyCode)
                    {
                        case Keys.X:
                            ExecuteShellCommand("cut"); 
                            break;
                        case Keys.C:
                            ExecuteShellCommand("copy"); 
                            break;
                        case Keys.V:
                            ExecuteShellCommand("paste"); 
                            break;
                        case Keys.Z: 
                            MessageBox.Show("Don't support UNDO now!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); 
                            break;
                    }
                }

                if (e.KeyCode == Keys.F2 && _listView.SelectedIndices.Count > 0)
                {
                    if (VirtualMode)
                    {
                        // In virtual mode, we must ensure the item is cached or retrieved
                        _listView.FocusedItem?.BeginEdit();
                    }
                    else
                    {
                        _listView.SelectedItems[0].BeginEdit();
                    }
                }

                if (e.KeyCode == Keys.F5)
                {
                    await RefreshContents();
                }

                if (e.KeyCode == Keys.Space)
                {
                    PostMessage(_listView.Handle, (uint)WindowsMessages.WM_KEYDOWN, (IntPtr)0x22, IntPtr.Zero);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }

                if (e.KeyCode == Keys.Enter && !e.Control && _listView.SelectedIndices.Count > 0)
                {
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi == null) return;

                    if (csi.FullPath.StartsWith(":"))
                        ExpListItemDoubleClick?.Invoke(csi.DisplayName, csi);
                    else
                        ExpListItemDoubleClick?.Invoke(csi.FullPath, csi);

                    if (!csi.IsFolder)
                    {
                        try
                        {
                            // LaunchFile(csi); // Let MainForm handle it via the event.
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error in starting application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }

                OnKeyDown(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyDown End");
            }
        }


        /// <summary>
        /// Handles the KeyUp event for navigation keys.
        /// </summary>
        private void ExpFileList_KeyUp(object? sender, KeyEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyUp Begin");
            try
            {
                if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                    && _listView.SelectedIndices.Count > 0)
                {
                    var csi = GetItem(_listView.SelectedIndices[0]);
                    if (csi != null) ExpListItemArrowKeyUp?.Invoke(csi.FullPath, csi);
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedItems();
                }

                OnKeyUp(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyUp End");
            }
        }

        private void ExpFileList_KeyPress(object? sender, KeyPressEventArgs e)
        {
            Debug.WriteLine("ExpList: ExpFileList_KeyPress Begin");
            try
            {
                OnKeyPress(e);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExpFileList_KeyPress End");
            }
        }


        #endregion Event Handlers
    }
}
