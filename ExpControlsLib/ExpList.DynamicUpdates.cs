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
    /// <summary>Handles shell notifications, shell commands, context-menu operations, and dynamic item updates.</summary>
    public partial class ExpList
    {
        #region Dynamic Update Handler


        /// <summary>
        /// Deletes the currently selected items via the shell and updates the UI.
        /// This is the shared implementation used by both keyboard Delete and context menu Delete.
        /// </summary>
        public void DeleteSelectedItems()
        {
            Debug.WriteLine("ExpList: DeleteSelectedItems Begin");
            try
            {
                if (_currentFolderCsi == null || !_currentFolderCsi.IsFolder)
                    return;

                if (SelectedCount <= 0) return;

                CShellItem[] selectedItems;
                IShellFolder? folder;
                List<IntPtr> relPidls;
                int deleteCount = 0;

                try
                {
                    folder = _currentFolderCsi.GetIShellFolder();
                    if (folder == null)
                    {
                        Debug.WriteLine("Failed to get folder interface for delete operation");
                        MessageBox.Show("Cannot delete: folder interface is unavailable.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (VirtualMode)
                        selectedItems = SelectedCShellItems.ToArray();
                    else
                        selectedItems = _listView?.SelectedItems?.Cast<ListViewItem>()?.Select(item => item.Tag as CShellItem)?.ToArray() ?? new CShellItem[0];

                    relPidls = new List<IntPtr>(selectedItems.Length);

                    for (int i = 0; i < selectedItems.Length; i++)
                    {
                        var sel = selectedItems[i];
                        if (sel == null)
                        {
                            Debug.WriteLine($"Selected item {i} is null");
                            continue;
                        }

                        if (!sel.CanDelete)
                        {
                            MessageBox.Show($"Cannot delete: {sel.DisplayName}", "Cannot Delete",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            continue;
                        }

                        IntPtr pidl = sel.LastPIDL;
                        if (pidl == IntPtr.Zero)
                        {
                            Debug.WriteLine($"Failed to get PIDL for item: {sel.DisplayName}");
                            MessageBox.Show($"Failed to get ID for item: {sel.DisplayName}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            continue;
                        }

                        relPidls.Add(CPidl.Clone(pidl));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error preparing delete operation: {ex.Message}");
                    MessageBox.Show($"Error preparing delete: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                deleteCount = relPidls.Count;
                if (deleteCount == 0)
                {
                    Debug.WriteLine("No items to delete");
                    return;
                }

                int topItemIndex = -1;
                int[] deletedIndices = Array.Empty<int>();
                bool hasItems = VirtualMode ? _listView.VirtualListSize > 0 : _listView.Items.Count > 0;
                if (hasItems)
                {
                    topItemIndex = _listViewWrapper.GetTopIndex();
                    deletedIndices = _listView.SelectedIndices.Cast<int>().ToArray();
                    _listView.SelectedIndices.Clear();
                    if (!VirtualMode)
                        _listView.SelectedItems.Clear();
                }

                var capturedParentPidl = CPidl.Clone(_currentFolderCsi.PIDL);
                _shellCommandService?.InvokeVerbAsync("delete", capturedParentPidl, relPidls);

                if (hasItems)
                {
                    try
                    {
                        _shellController.HierachyManager.RemoveRange(selectedItems, raiseEvents: false);
                        _listViewWrapper.RemoveItems(selectedItems);

                        if (selectedItems.Length > _listViewWrapper.GetApproxVisibleCount())
                            OnScroll();

                        if (_currentFolderCsi != null)
                        {
                            string path = _currentFolderCsi.FullPath.StartsWith(":")
                                ? _currentFolderCsi.DisplayName
                                : _currentFolderCsi.FullPath;
                            ExpListItemsChanged?.Invoke(path, _currentFolderCsi);
                        }

                        ExpListDeleted?.Invoke(this, new ExpListDeletedEventArgs(selectedItems, deletedIndices));
                    }
                    finally
                    {
                    }
                }

                if (topItemIndex >= 0 && deleteCount > _approxCountPerPage)
                {
                    int count = VirtualMode ? _listView.VirtualListSize : _listView.Items.Count;
                    topItemIndex = topItemIndex >= count ? count : topItemIndex;
                    EnsureVisible(topItemIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in DeleteSelectedItems: {ex.Message}");
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Debug.WriteLine("ExpList: DeleteSelectedItems End");
            }
        }

        /// <summary>
        /// Invokes a standard shell action (cut, copy, paste) on the selected items.
        /// For delete operations, use <see cref="DeleteSelectedItems"/> instead.
        /// </summary>
        /// <param name="cmd">The shell verb to invoke (e.g., "cut", "copy", "paste").</param>
        public void ExecuteShellCommand(string cmd)
        {
            Debug.WriteLine("ExpList: ExecuteShellCommand Begin");
            try
            {
                if (cmd == "delete")
                {
                    DeleteSelectedItems();
                    return;
                }

                // Validate preconditions
                if (_currentFolderCsi == null || !_currentFolderCsi.IsFolder)
                {
                    return;
                }

                IShellFolder? folder = null;
                List<IntPtr>? relPidls = null;
                CShellItem[] selectedItems = Array.Empty<CShellItem>();

                if (cmd == "paste")
                {
                    // Get the target folder for paste operation
                    try
                    {
                        folder = _currentFolderCsi == ShellController.DesktopCSI
                            ? _currentFolderCsi.GetIShellFolder()
                            : _currentFolderCsi.Parent?.GetIShellFolder();

                        if (folder == null)
                        {
                            Debug.WriteLine("Failed to get folder interface for paste operation");
                            MessageBox.Show("Cannot paste: folder interface is unavailable.", "Paste Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        IntPtr relPidl = CPidl.ILFindLastID(_currentFolderCsi.PIDL);
                        if (relPidl == IntPtr.Zero)
                        {
                            Debug.WriteLine("Failed to get relative PIDL for current folder");
                            return;
                        }

                        relPidls = new List<IntPtr> { CPidl.Clone(relPidl) };
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing paste operation: {ex.Message}");
                        MessageBox.Show($"Error preparing paste: {ex.Message}", "Paste Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else // Handle cut, copy operations
                {
                    if (SelectedCount <= 0) return;

                    try
                    {
                        folder = _currentFolderCsi.GetIShellFolder();
                        if (folder == null)
                        {
                            Debug.WriteLine("Failed to get folder interface for selected items");
                            MessageBox.Show("Cannot perform operation: folder interface is unavailable.", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        if (VirtualMode)
                        {
                            selectedItems = SelectedCShellItems.ToArray();
                        }
                        else
                        {
                            selectedItems = _listView?.SelectedItems?.Cast<ListViewItem>()?.Select(item => item.Tag as CShellItem)?.ToArray() ?? new CShellItem[0];
                        }

                        relPidls = new List<IntPtr>(selectedItems.Length);

                        for (int i = 0; i < selectedItems.Length; i++)
                        {
                            var sel = selectedItems[i];
                            if (sel == null)
                            {
                                Debug.WriteLine($"Selected item {i} is null");
                                continue;
                            }

                            IntPtr pidl = sel.LastPIDL;
                            if (pidl == IntPtr.Zero)
                            {
                                Debug.WriteLine($"Failed to get PIDL for item: {sel.DisplayName}");
                                MessageBox.Show($"Failed to get ID for item: {sel.DisplayName}", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                continue;
                            }

                            relPidls.Add(CPidl.Clone(pidl));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing {cmd} operation: {ex.Message}");
                        MessageBox.Show($"Error preparing operation: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                // Validate items to process
                if (relPidls == null || relPidls.Count == 0)
                {
                    Debug.WriteLine("No items to process");
                    return;
                }

                // Capture for background thread
                var capturedParentPidl = CPidl.Clone(_currentFolderCsi.PIDL);
                var capturedRelPidls = relPidls;

                // Offload shell interaction to background STA thread. 
                // Binding MUST happen on this thread to avoid marshaling back to UI thread.
                _shellCommandService?.InvokeVerbAsync(cmd, capturedParentPidl, capturedRelPidls);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in ExecuteShellCommand: {ex.Message}");
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Debug.WriteLine("ExpList: ExecuteShellCommand End");
            }
        }

        /// <summary>
        /// This invokes the specified shell command on a background STA thread with it's own window handle.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="capturedParentPidl"></param>
        /// <param name="capturedRelPidls"></param>
        /// <returns></returns>
        /// <summary>
        /// Creates a native Windows context menu for the current folder.
        /// </summary>
        /// <param name="comContextMenu">Output parameter for the main context menu handle.</param>
        /// <param name="viewSubMenu">Output parameter for the View submenu handle.</param>
        private void CreateContextMenu(out IntPtr comContextMenu, out IntPtr viewSubMenu, out IntPtr sortSubMenu)
        {
            Debug.WriteLine("ExpList: CreateContextMenu Begin");
            try
            {
                comContextMenu = CreatePopupMenu();
                viewSubMenu = CreatePopupMenu();
                sortSubMenu = CreatePopupMenu();

                // Create and insert the "View" submenu item into the main context menu.
                var itemInfo = new MENUITEMINFO("View")
                {
                    fMask = (int)(MIIM.SUBMENU | MIIM.STRING),
                    hSubMenu = viewSubMenu
                };
                InsertMenuItem(comContextMenu, 0, true, ref itemInfo);

                // Create and insert the "Sort by" submenu item into the main context menu.
                var sortInfo = new MENUITEMINFO("Sort by")
                {
                    fMask = (int)(MIIM.SUBMENU | MIIM.STRING),
                    hSubMenu = sortSubMenu
                };
                InsertMenuItem(comContextMenu, 1, true, ref sortInfo);

                // Add view mode options to the View submenu with radio button indicators.
                uint checkedFlag;
                uint checkedValue = (uint)(MFT.RADIOCHECK | MFT.CHECKED);

                checkedFlag = (DisplayMode == ListViewDisplayMode.Details) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.DETAILS, "Details");

                checkedFlag = (DisplayMode == ListViewDisplayMode.Thumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (uint)CMD.THUMBNAILS, "Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.LargeThumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (uint)CMD.LARGE_THUMBNAILS, "Large Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.ExtraLargeThumbnail) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.EXTRA_LARGE_THUMBNAILS, "Extra Large Thumbnails");

                checkedFlag = (DisplayMode == ListViewDisplayMode.LargeIcon) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.LARGEICON, "Icons");

                checkedFlag = (DisplayMode == ListViewDisplayMode.List) ? checkedValue : (uint)MFT.BYCOMMAND;
                AppendMenu(viewSubMenu, checkedFlag, (int)CMD.LIST, "List");

                // Tile view is not supported in virtual mode — omit the option entirely.
                if (!_listViewWrapper.VirtualMode)
                {
                    checkedFlag = (DisplayMode == ListViewDisplayMode.Tile) ? checkedValue : (uint)MFT.BYCOMMAND;
                    AppendMenu(viewSubMenu, checkedFlag, (int)CMD.TILES, "Tiles");
                }

                // Add sorting options to the Sort by submenu.
                if (_listView.ListViewItemSorter is LVColSorter sorter)
                {
                    int currentSortCol = sorter.SortColumn;
                    for (int i = 0; i < _listView.Columns.Count; i++)
                    {
                        uint sortChecked = (i == currentSortCol) ? checkedValue : (uint)MFT.BYCOMMAND;
                        AppendMenu(sortSubMenu, sortChecked, (uint)((int)CMD.SORT_BY_BASE + i), _listView.Columns[i].Text);
                    }
                }

                // Add separator and standard folder operations to the main context menu.
                AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.REFRESH, "Refresh (F5)");
                AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.SELECT_ALL, "Select All (Ctrl+A)");
                AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);

                // Determine if Paste operations are allowed by checking clipboard contents.
                // CanDropClipboard() returns the DragDropEffects supported by the target folder.
                var enabled = (uint)MFT.GRAYED;
                DragDropEffects effects = DragDropEffects.None;

                if (_currentFolderCsi == null)
                {
                    enabled = (uint)MFT.BYCOMMAND;
                }
                else
                {
                    effects = CanDropClipboard(_currentFolderCsi);
                    if ((effects & DragDropEffects.Copy) == DragDropEffects.Copy ||
                        (effects & DragDropEffects.Move) == DragDropEffects.Move)
                    {
                        enabled = (uint)MFT.BYCOMMAND;
                    }
                }

                // Add Paste menu item, enabled only if clipboard contents are compatible.
                AppendMenu(comContextMenu, enabled, (int)CMD.PASTE, "Paste (Ctrl+V)");

                // Add additional paste and context operations if a folder is selected.
                if (_currentFolderCsi != null)
                {
                    enabled = (uint)MFT.GRAYED;
                    if ((effects & DragDropEffects.Link) == DragDropEffects.Link)
                        enabled = (int)MFT.BYCOMMAND;

                    AppendMenu(comContextMenu, enabled, (uint)CMD.PASTELINK, "Paste Link");
                    AppendMenu(comContextMenu, (uint)MFT.SEPARATOR, 0, string.Empty);

                    // Add New menu for writable folders (excluding special shell folders like ::).
                    // The "New" submenu is managed by m_WindowsContextMenu.SetUpNewMenu(),
                    // which adds file creation options for the selected folder.
                    // Cleanup is done via ReleaseNewMenu() in ShowAndHandleContextMenu's CLEANUP section.
                    if (_currentFolderCsi.IsFolder &&
                        ((!_currentFolderCsi.FullPath.StartsWith("::")) || _currentFolderCsi == ShellController.DesktopCSI))
                    {
                        int xIndex = GetMenuItemCount(comContextMenu.ToInt32());
                        m_WindowsContextMenu.SetUpNewMenu(_currentFolderCsi, comContextMenu, xIndex);
                        AppendMenu(comContextMenu, (int)MFT.SEPARATOR, 0, string.Empty);
                    }

                    AppendMenu(comContextMenu, (uint)MFT.BYCOMMAND, (uint)CMD.PROPERTIES, "Properties");
                }
            }
            finally
            {
                Debug.WriteLine("ExpList: CreateContextMenu End");
            }
        }

        /// <summary>
        /// Displays a context menu for the ListView when no items are selected.
        /// This menu includes view options (Tiles, Icons, List, Details), 
        /// refresh, select all, paste operations, and new item creation.
        /// </summary>
        /// <param name="pt">The point (in screen coordinates) where the menu should be displayed.</param>
        /// <remarks>
        /// This function handles the creation and management of Windows popup menus.
        /// It directly manages native menu handles via Win32 API calls and must properly
        /// release all COM objects and menu handles to avoid memory leaks and access violations.
        /// 
        /// Key operations:
        /// 1. Creates two popup menus: a main context menu and a View submenu
        /// 2. Populates menus with commands and their checked states
        /// 3. Determines menu item availability based on clipboard contents
        /// 4. Invokes the selected command on shell objects (IShellFolder, IContextMenu)
        /// 5. Releases all COM interfaces and menu handles in the CLEANUP section
        /// 
        /// Memory safety note: Menu handles (comContextMenu, viewSubMenu) must be released
        /// via Marshal.Release() after TrackPopupMenuEx returns. COM objects (IContextMenu, 
        /// IShellFolder) must be released by ReleaseComObject() to prevent heap corruption.
        /// Mixing release mechanisms or skipping releases can cause access violations.
        /// </remarks>
        private async void ShowAndHandleContextMenu(Point pt)
        {
            if (m_IsShowingContextMenu) return;
            m_IsShowingContextMenu = true;
            try
            {
                await ShowAndHandleContextMenuCore(pt);
            }
            finally
            {
                m_IsShowingContextMenu = false;
            }
        }

        private async Task ShowAndHandleContextMenuCore(Point pt)
        {
            Debug.WriteLine("ExpList: ShowAndHandleContextMenu Begin");
            try
            {
                int MIN = 1;
                var cmi = new CMInvokeCommandInfoEx();

                // Create three native Windows popup menu handles.
                IntPtr comContextMenu;
                IntPtr viewSubMenu;
                IntPtr sortSubMenu;

                CreateContextMenu(out comContextMenu, out viewSubMenu, out sortSubMenu);

                // Display the context menu and capture the user's selection.
                int cmdID = TrackPopupMenuEx(comContextMenu, (int)TPM.RETURNCMD, pt.X, pt.Y, Handle, IntPtr.Zero);

                // Process the user's menu selection.
                if (cmdID >= MIN)
                {
                    // Handle sorting commands.
                    if (cmdID >= (int)CMD.SORT_BY_BASE)
                    {
                        int colIndex = cmdID - (int)CMD.SORT_BY_BASE;
                        if (_listView.ListViewItemSorter is LVColSorter sorter)
                        {
                            sorter.SortColumn = colIndex;
                        }
                        goto CLEANUP;
                    }

                    // Initialize the CMInvokeCommandInfoEx structure used for shell command invocation.
                    cmi = new CMInvokeCommandInfoEx
                    {
                        cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                        hwnd = IntPtr.Zero,
                        nShow = (int)SW.SHOWNORMAL,
                        fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE),
                        ptInvoke = pt
                    };

                    // Handle view mode changes and built-in operations.
                    var cmdEnum = (CMD)cmdID;
                    switch (cmdEnum)
                    {
                        case CMD.TILES:
                            DisplayMode = ListViewDisplayMode.Tile;
                            goto CLEANUP;
                        case CMD.LIST:
                            DisplayMode = ListViewDisplayMode.List;
                            goto CLEANUP;
                        case CMD.DETAILS:
                            DisplayMode = ListViewDisplayMode.Details;
                            goto CLEANUP;
                        case CMD.LARGEICON:
                            this.DisplayMode = ListViewDisplayMode.LargeIcon;
                            goto CLEANUP;
                        case CMD.THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.Thumbnail;
                            goto CLEANUP;
                        case CMD.LARGE_THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.LargeThumbnail;
                            goto CLEANUP;
                        case CMD.EXTRA_LARGE_THUMBNAILS:
                            this.DisplayMode = ListViewDisplayMode.ExtraLargeThumbnail;
                            goto CLEANUP;
                        case CMD.REFRESH:
                            // Refresh the folder contents and re-sort the ListView items.
                            await RefreshContents();
                            goto CLEANUP;
                        case CMD.SELECT_ALL:
                            // Select all items in the ListView.
                            if (VirtualMode)
                            {
                                _listView.BeginUpdate(); //is this needed?
                                try
                                {
                                    for (int i = 0; i < _listView.VirtualListSize; i++)
                                        _listView.SelectedIndices.Add(i);
                                }
                                finally
                                {
                                    _listView.EndUpdate();
                                }
                            }
                            else
                            {
                                EnterListViewEnumeration();
                                try
                                { //should we use beginupdate here?
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
                            goto CLEANUP;
                        case CMD.PASTE:
                        case CMD.PASTELINK:
                        case CMD.PROPERTIES:
                            if (_currentFolderCsi != null)
                            {
                                string verb = cmdEnum switch
                                {
                                    CMD.PASTE => "paste",
                                    CMD.PASTELINK => "pastelink",
                                    CMD.PROPERTIES => "properties",
                                    _ => ""
                                };
                                cmi.lpVerb = Marshal.StringToHGlobalAnsi(verb);
                                cmi.lpVerbW = Marshal.StringToHGlobalUni(verb);
                            }
                            else
                            {
                                goto CLEANUP;
                            }
                            break;
                        default:
                            // Handle commands from the "New" submenu.
                            cmdID -= 1;
                            cmi.lpVerb = (IntPtr)cmdID;
                            cmi.lpVerbW = (IntPtr)cmdID;
                            m_CreateNew = true;
                            
                            var newMenuBase = m_WindowsContextMenu.newMenuBase;
                            var cmi_new = cmi;
                            await _staRunner.EnqueueWork(_ =>
                            {
                                return newMenuBase.InvokeCommand(cmi_new);
                            });
                            goto CLEANUP;
                    }

                    if (_currentFolderCsi != null)
                    {
                        IntPtr parentPidl = _currentFolderCsi == ShellController.DesktopCSI
                            ? _currentFolderCsi.PIDL
                            : _currentFolderCsi.Parent.PIDL;

                        if (_currentFolderCsi.LastPIDL == IntPtr.Zero)
                        {
                            Debug.WriteLine("ERROR: no relative pidl for CSI.  Is this the root of the namespace?");
                            Debugger.Break();
                        }
                        var capturedRelPidl = CPidl.Clone(_currentFolderCsi.LastPIDL);
                        var capturedParentPidl = CPidl.Clone(parentPidl); //I don't think this is needed but just in case.
                        var cmi_shell = cmi;

                        await _shellCommandService!.InvokeContextMenuAsync(
                            capturedParentPidl,
                            new[] { capturedRelPidl },
                            cmi_shell);

                        // Clean up allocated strings
                        if (cmi.lpVerb != IntPtr.Zero && cmi.lpVerb.ToInt64() > 0xFFFF) Marshal.FreeHGlobal(cmi.lpVerb);
                        if (cmi.lpVerbW != IntPtr.Zero && cmi.lpVerbW.ToInt64() > 0xFFFF) Marshal.FreeHGlobal(cmi.lpVerbW);
                    }
                }

            CLEANUP:
                m_WindowsContextMenu.ReleaseNewMenu();

                if (comContextMenu != IntPtr.Zero)
                {
                    DestroyMenu(comContextMenu);
                    comContextMenu = IntPtr.Zero;
                }

                // Note: viewSubMenu and sortSubMenu are destroyed when comContextMenu is destroyed.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in ShowAndHandleContextMenu: {ex.Message}");
            }
            finally
            {
                Debug.WriteLine("ExpList: objects End");
            }
        }

        private delegate void InvokeUpdate(object? sender, ShellItemUpdateEventArgs e);

        /// <summary>
        /// Exposes the SelectedItems collection of the internal ListView to allow external handlers to access the currently selected items.
        /// </summary>
        public ListView.SelectedListViewItemCollection SelectedItems => _listView.SelectedItems;

        /// <summary>
        /// Marshals shell item update events to the UI thread.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private void ShellUpdater_UpdateEventInvoker(object? sender, ShellItemUpdateEventArgs e)
        {
            //Debug.WriteLine("ExpList: UpdateInvoke Begin");
            try
            {
                if (sender is null || IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                if (e is null)
                {
                    Console.WriteLine("Event arguments cannot be null in UpdateInvoke.");
                    return;
                }

                if (InvokeRequired)
                {
                    try
                    {
                        BeginInvoke((InvokeUpdate)ShellUpdater_UpdateEventHandler, sender, e);
                    }
                    catch (InvalidOperationException) { } // Handle race condition where control is disposed just after check
                }
                else
                {
                    ShellUpdater_UpdateEventHandler(sender, e);
                }
            }
            finally
            {
                //Debug.WriteLine("ExpList: UpdateInvoke End");
            }
        }

        private LruConcurrentDictionary<String, bool> _activeDeletes = new(1000);
        private CShellItem[]? _pendingDragItems;  // items from an active drag, awaiting shell notification with paths
        /// <summary>
        /// Performs the actual update of list view items in response to shell changes.
        /// Handles creation, deletion, renaming, and other updates of files and folders.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ShellItemUpdateEventArgs"/> containing the event data.</param>
        private async void ShellUpdater_UpdateEventHandler(object? sender, ShellItemUpdateEventArgs e)
        {
            try
            {
                if (sender is null || _currentFolderCsi == null || e?.Item is null) return;

                Debug.WriteLine($"ExpList: DoItemUpdate Begin - {e.UpdateType.ToString()}, {e.Item.DisplayName}");

                // If an enumeration is in progress, defer this update to prevent reentrant
                // mutation of _listView.Items (which causes null items during foreach).
                if (_enumerationDepth > 0)
                {
                    _deferredUpdates.Enqueue((sender, e));
                    return;
                }

                var senderCsi = e.Item;

                // For Created/Deleted/UpdateDir, sender is the Folder containing the item.
                // For Updated/Renamed/IconChange, sender is the Item itself.
                bool isTargetFolder = CPidl.ResolvesToSamePathOrName(senderCsi.PIDL, _currentFolderCsi.PIDL);
                bool isTargetItem = senderCsi.Parent != null && CPidl.ResolvesToSamePathOrName(senderCsi.Parent.PIDL, _currentFolderCsi.PIDL);

                // For Moved events, the item's Parent/PIDL have already been updated to the
                // new location, so isTargetFolder/isTargetItem will be false. The sender is
                // the old (or new) parent CShellItem — check if it matches the current folder.
                bool isTargetViaMoveSender = e.UpdateType == CShItemUpdateType.Moved
                    && sender is CShellItem movedFromFolder
                    && CPidl.ResolvesToSamePathOrName(movedFromFolder.PIDL, _currentFolderCsi.PIDL);

                if (!isTargetFolder && !isTargetItem && !isTargetViaMoveSender) return;

                try
                {
                    switch (e.UpdateType)
                    {
                        case CShItemUpdateType.Created:
                            {
                                if (!isTargetFolder && !isTargetItem) return;
                                if (IsExcluded(e.Item)) return;
                                if (_filter != null && !_filter(e.Item)) return; // pre-filter new items

                                _listViewWrapper.InsertSorted(e.Item);
                                m_CreateNew = false; //I don't think this is even used?

                                break;
                            }

                        case CShItemUpdateType.Deleted:
                            if (e.Item is null)
                            {
                                Debug.WriteLine("ExpList received DELETED event but no item was specified.");
                                return;
                            }

                            if (_activeDeletes.ContainsKey(e.Item.FullPath))
                            {
                                Debug.WriteLine("  [DELETE] Already processing delete for this item. Skipping to avoid duplicate work.");
                                return;
                            }

                            try
                            {
                                _activeDeletes.Add(e.Item.FullPath, true);
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    //bool wasSelected = _listViewWrapper.IsItemSelected(e.Item);
                                    _listViewWrapper.RemoveAt(index);

                                    //if (wasSelected && SelectedCount == 0 && Count > 0)
                                    //{
                                    //    int nextIndex = Math.Min(index, Count - 1);
                                    //    var nextLvi = _listViewWrapper.GetListViewItem(nextIndex);
                                    //    if (nextLvi != null)
                                    //    {
                                    //        nextLvi.Selected = true;
                                    //        nextLvi.Focused = true;
                                    //    }
                                    //}
                                }
                            }
                            finally
                            {
                                _activeDeletes.Remove(e.Item.FullPath);
                            }
                            break;

                        case CShItemUpdateType.Renamed: // This event can be raised in various rename scenarios - file rename, folder rename, drag-drop move with rename, etc.  The structure of the event (which properties are populated) can vary based on the scenario, so the handling needs to be robust to these variations.
                            {
                                var csi = e.Item;

                                if (e.Item.Parent.FullPath != _currentFolderCsi.FullPath) return;

                                int index = -1;
                                if (VirtualMode)
                                {
                                    index = _listViewWrapper.FindInsertionPoint(csi);
                                }
                                else
                                {
                                    var lvi = csi.LVItem;
                                    if (lvi is null) throw new Exception("ListViewItem not found for renamed item");
                                    index = lvi.Index;
                                }

                                if (index >= 0)
                                {
                                    _listViewWrapper.RemoveAt(index);
                                    if (!IsExcluded(csi) && (_filter == null || _filter(csi)))
                                    {
                                        _listViewWrapper.InsertSorted(csi);
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.Moved:
                            {
                                var csi = e.Item;
                                // The sender is the old parent folder.
                                // csi.Parent has already been updated to the new parent.
                                if (sender is CShellItem senderFolder
                                    && CPidl.ResolvesToSamePathOrName(senderFolder.PIDL, _currentFolderCsi.PIDL))
                                {
                                    // Item was moved FROM the current folder → remove it
                                    _listViewWrapper.RemoveItems(new[] { csi });

                                    if (_currentFolderCsi != null)
                                    {
                                        string path = _currentFolderCsi.FullPath.StartsWith(":")
                                            ? _currentFolderCsi.DisplayName
                                            : _currentFolderCsi.FullPath;
                                        ExpListItemsChanged?.Invoke(path, _currentFolderCsi);
                                    }
                                }

                                // If this item was part of a pending drag, fire DragCompleted with paths
                                if (_pendingDragItems is not null)
                                {
                                    var match = Array.Find(_pendingDragItems, i => ReferenceEquals(i, csi));
                                    if (match is not null)
                                    {
                                        // Remove from pending so DW_DragEnd doesn't fire a duplicate
                                        _pendingDragItems = _pendingDragItems.Where(i => !ReferenceEquals(i, csi)).ToArray();
                                        if (_pendingDragItems.Length == 0) _pendingDragItems = null;

                                        ExpListDragCompleted?.Invoke(this,
                                            new DragCompletedEventArgs(DragDropEffects.Move,
                                                new[] { csi }, e.OldPath, e.NewPath));
                                    }
                                }
                                break;
                            }

                        case CShItemUpdateType.Updated:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    _listViewWrapper.RedrawItem(index);
                                }

                                break;
                            }

                        case CShItemUpdateType.UpdateDir:
                            Debug.WriteLine("\tUpdateDir");
                            await LoadDirectoryAsync(_currentFolderCsi, true, reload: true);
                            break;

                        case CShItemUpdateType.IconChange:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    _imageListOrchestrator.RefreshImage(e.Item, index, () => _listViewWrapper.RedrawItem(index));
                                }
                                break;
                            }

                        case CShItemUpdateType.MediaChange:
                            {
                                int index = _listViewWrapper.GetIndexFromFullPath(e.Item.FullPath);
                                if (index >= 0)
                                {
                                    _imageListOrchestrator.RefreshImage(e.Item, index, () => _listViewWrapper.RedrawItem(index));
                                }
                                break;
                            }
                    }

                    // Fire ExpListItemsChanged for Created/Deleted events.
                    // This was previously in UpdateInvoke but must be here since
                    // BeginInvoke is now used (the marshaling path wouldn't fire it).
                    if (e.UpdateType == CShItemUpdateType.Created || e.UpdateType == CShItemUpdateType.Deleted)
                    {
                        if (_currentFolderCsi != null)
                        {
                            if (_currentFolderCsi.FullPath.StartsWith(":"))
                                ExpListItemsChanged?.Invoke(_currentFolderCsi.DisplayName, _currentFolderCsi);
                            else
                                ExpListItemsChanged?.Invoke(_currentFolderCsi.FullPath, _currentFolderCsi);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in frmTemplate -- ExpFileList updater -- " + ex);
                }
                finally
                {
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EXCEPTION: DoItemUpdate -- " + ex.ToString());
            }
            finally
            {
                //Debug.WriteLine("ExpList: DoItemUpdate End");
            }
        }


        /// <summary>
        /// Refreshes the display of a single item whose underlying filesystem or scoring data has changed.
        /// </summary>
        public void UpdateListviewItemCallback(ListViewItem lvi, CShellItem csi)
        {
            //Debug.WriteLine("ExpList: UpdateLviUsingCsi Begin");
            try
            {
                if (lvi == null || csi == null) return;

                // Update primary text
                lvi.Text = csi.DisplayName;
                lvi.Name = csi.FullPath;
                lvi.Tag = csi;
                csi.LVItem = lvi;

                PopulateColumnData(lvi, csi); //you need this even in non-details mode to facilitate sorting

                lvi.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(csi);
            }
            finally
            {
                //Debug.WriteLine("ExpList: UpdateLviUsingCsi End");
            }
        }

        private void PopulateColumnData(ListViewItem lvi, CShellItem item)
        {
            for (int i = 0; i < _listView.Columns.Count; i++)
            {
                ColumnHeader col = _listView.Columns[i];

                var data = GetColumnData(item, col);
                if (lvi.SubItems.Count <= i)
                {
                    var si = lvi.SubItems.Add(new ListViewItem.ListViewSubItem());
                    si.Text = data.Text;
                    si.Tag = data.Tag;
                }
                else
                {
                    lvi.SubItems[i].Text = data.Text;
                    lvi.SubItems[i].Tag = data.Tag;
                }
            }
        }

        /// <summary>
        /// Populates the text and tag for a single given column based on the provided shell item and column header.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="col"></param>
        /// <param name="text"></param>
        /// <param name="tag"></param>
        internal ListViewSubitemData GetColumnData(CShellItem item, ColumnHeader col)
        {
            return GetColumnData(item, col.Text, col.Index, col.Tag?.ToString().Trim() ?? string.Empty);
        }

        internal ListViewSubitemData GetColumnData(CShellItem item, string colText, int colIndex, string mapping)
        {
            //Debug.WriteLine("ExpList: GetColumnData Begin");
            try
            {
                string text = string.Empty;
                object? tag = null;

                // 1. Try Tag Mapping
                if (!string.IsNullOrEmpty(mapping) && mapping.StartsWith("."))
                {
                    string propName = mapping.Substring(1);
                    // Optimization: Check for common properties directly
                    switch (propName)
                    {
                        case "ID":
                            text = item.ID.ToString();
                            return new ListViewSubitemData(text, null);
                        case "DisplayName":
                            text = item.DisplayName;
                            return new ListViewSubitemData(text, null);
                        case "TypeName":
                            text = item.TypeName;
                            return new ListViewSubitemData(text, null);
                        case "Size":
                            if (!item.IsDisk && item.IsFileSystem && !item.IsFolder)
                            {
                                text = item.Size;
                                tag = item.Length;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                        case "LastWriteTime":
                            if (!item.IsDisk && item.LastWriteTime != EmptyTimeValue)
                            {
                                text = item.LastWriteTime.ToString("MM/dd/yyyy HH:mm:ss");
                                tag = item.LastWriteTime;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                        case "CreationTime":
                            if (!item.IsDisk && item.CreationTime != EmptyTimeValue)
                            {
                                text = item.CreationTime.ToString("MM/dd/yyyy HH:mm:ss");
                                tag = item.CreationTime;
                                return new ListViewSubitemData(text, tag);
                            }
                            else return new ListViewSubitemData(string.Empty, null);
                    }


                    if (item.ColumnDic.TryGetValue(colText, out ListViewSubitemData propInfo)) //maybe it was already fetched before
                        return propInfo;

                    // Fallback to reflection for other properties
                    if (mapping.StartsWith(".Tag")) //get the value from one of the fields within the custom Tag object property
                    {
                        if (item.Tag != null)
                        {
                            string memberName = mapping.Substring(4);
                            if (string.IsNullOrEmpty(memberName)) return new ListViewSubitemData(string.Empty, null);

                            Type tagType = item.Tag.GetType();
                            // Try Field first
                            FieldInfo field = tagType.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (field != null)
                            {
                                object val = field.GetValue(item.Tag);
                                text = val?.ToString() ?? string.Empty;
                                tag = val;
                                goto END;
                            }
                            // Then try Property
                            PropertyInfo prop = tagType.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (prop != null)
                            {
                                object val = prop.GetValue(item.Tag);
                                text = val?.ToString() ?? string.Empty;
                                tag = val;
                                goto END;
                            }
                        }
                    }
                    else
                    {
                        PropertyInfo prop = item.GetType().GetProperty(propName);
                        if (prop != null)
                        {
                            object val = prop.GetValue(item);
                            text = val?.ToString() ?? string.Empty;
                            tag = val;
                            goto END;
                        }
                    }
                }

                if (mapping == ".DisplayName")
                {
                    text = item.DisplayName;
                }
                else
                {
                    // 2. Try external event handler
                    EnsureCustomColumnDataFetched(item);
                    if (item.ColumnDic.TryGetValue(colText, out ListViewSubitemData propInfo))
                        return propInfo;
                }

            END:
                var result = new ListViewSubitemData(text, tag);

                item.ColumnDic.TryAdd(colText, result); //save for future use

                return result;
            }
            finally
            {
                //Debug.WriteLine("ExpList: GetColumnData End");
            }
        }

        /// <summary>
        /// Loads all special custom column data that isn't part of CShellItem's default properties by firing
        /// the ExpListGetColumnData event.  This allows external handlers to provide bulk data for all columns 
        /// in one shot, which is more efficient than firing GetColumnData for each individual column.
        /// </summary>
        /// <param name="item"></param>
        private void EnsureCustomColumnDataFetched(CShellItem item)
        {
            if (ExpListGetColumnData == null) return; //we have no way to get the data

            if (item.ColumnDic.ContainsKey("DbId")) return; //this indicates it has all the items already

            // Otherwise, fire the event to fetch ALL custom columns at once.
            var args = new ExpListGetColumnDataEventArgs(item);
            ExpListGetColumnData(this, args);
        }

        /// <summary>
        /// Refreshes all items in the current folder, clearing all caches and re-reading from disk.
        /// </summary>
        public async Task RefreshContents()
        {
            if (_currentFolderCsi == null) return;

            // Invalidate thumbnails and create a fresh ImageList
            _imageListOrchestrator.ResetForNewFolder();

            // Invalidate cached data in shell items
            if (VirtualMode)
            {
                foreach (var item in _listViewWrapper.Items)
                {
                    item.ColumnDic.Clear();
                    item.ResetInfo();
                }
            }
            else
            {
                EnterListViewEnumeration();
                try
                {
                    foreach (ListViewItem lvi in _listView.Items)
                    {
                        if (lvi.Tag is CShellItem csi)
                        {
                            csi.ColumnDic.Clear();
                            csi.ResetInfo();
                        }
                    }
                }
                finally
                {
                    ExitListViewEnumeration();
                }
            }

            // Also reset the folder itself
            _currentFolderCsi.ResetInfo();
            _currentFolderCsi.ResetChildren();

            // Force reload from disk
            await LoadDirectoryAsync(_currentFolderCsi, reload: true);

            // Re-sort
            _listViewWrapper.Sort();
        }

        public void RefreshItemByFullPath(string path)
        {
            _listViewWrapper.RefreshItemByFullPath(path);
        }

        public void RefreshItem(CShellItem? item)
        {
            _listViewWrapper.RefreshItem(item);
        }

        #endregion
    }
}
