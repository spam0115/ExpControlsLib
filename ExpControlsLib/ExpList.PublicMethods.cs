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
    public partial class ExpList
    {
        #region Public Methods


        /// <summary>
        /// Gets the CShellItem at the specified index.
        /// </summary>
        public CShellItem? GetItem(int index) => _listViewWrapper.GetItem(index);

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public void RemoveAt(int index) => _listViewWrapper.RemoveAt(index);

        /// <summary>
        /// Sets the sort column and order without triggering an actual sort.
        /// This is useful to set at startup before the first location is loaded.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSortState(int column, SortOrder order)
        {
            _listViewWrapper.SetSortState(column, order);
        }

        /// <summary>
        /// Sets the sort column and order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void Sort(int column, SortOrder order)
        {
            Debug.WriteLine("ExpList: SetSort Begin");
            try
            {
                _listViewWrapper.Sort(column, order);
            }
            finally
            {
                Debug.WriteLine("ExpList: SetSort End");
            }
        }

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/>.
        /// </summary>
        /// <param name="pathName">The display path of the folder.</param>
        /// <param name="csi">The <see cref="CShellItem"/> representing the folder to display.</param>
        /// <param name="includeFolder">True to include subdirectories in the list.</param>
        /// <param name="reload">True to force a reload even if the same item was previously selected.</param>
        public async Task LoadDirectoryAsync(string pathName, bool includeFolder = true, bool reload = false)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(string): Begin for '{pathName}', reload={reload}");
            if (!reload && (_currentFolderCsi is not null && pathName == CurrentPath))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(string): Skipping - same path");
                return;
            }

            CShellItem csi;
            if (pathName == null) 
                csi = null;
            else
                csi = _shellController.HierachyManager.FindAndAllowExpansion(pathName);

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(string): Found csi='{csi?.DisplayName}', calling LoadDirectoryBaseAsync...");
            await LoadDirectoryBaseAsync(csi, includeFolder);

            CurrentFolderCsi = csi;
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(string): End for '{pathName}'");
        }

        public async Task LoadDirectoryAsync(CShellItem? csi, bool includeFolder = true, bool reload = false)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(CShellItem): Begin for '{csi?.DisplayName}', reload={reload}");
            if (csi is null)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(CShellItem): csi is null, returning");
                return;
            }
            if (!reload && (_currentFolderCsi is not null && csi.FullPath == CurrentPath))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(CShellItem): Skipping - same path");
                return;
            }

            var hierarchyCsi = _shellController.HierachyManager.FindAndAllowExpansion(csi);
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(CShellItem): hierarchyCsi='{hierarchyCsi?.DisplayName}', calling LoadDirectoryBaseAsync...");

            await LoadDirectoryBaseAsync(hierarchyCsi, includeFolder);
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryAsync(CShellItem): End for '{csi?.DisplayName}'");
        }

        /// <summary>
        /// Populates the list view with files and directories from the specified <see cref="CShellItem"/> asynchronously.
        /// </summary>
        private async Task<bool> LoadDirectoryBaseAsync(CShellItem? csi, bool includeFolder = true)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: Begin for '{csi?.FullPath}'");

            if (csi is null)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: csi is null, clearing list");
                ClearListView();
                return true;
            }
            // Cancel and dispose the previous source before replacing it. This keeps
            // repeated navigation/reload operations from accumulating registrations
            // and native wait handles for the lifetime of the control.
            var previousLoadCancellation = _loadDirectoryCancelTs;
            previousLoadCancellation?.Cancel();
            previousLoadCancellation?.Dispose();
            _loadDirectoryCancelTs = new CancellationTokenSource();
            var token = _loadDirectoryCancelTs.Token;

            ExpListDirectoryLoading?.Invoke(this, EventArgs.Empty);

            // Capture sort settings and create comparer on UI thread to ensure thread-safe access to ColumnHeader properties
            int sortCol = _listViewWrapper.SortColumn;
            SortOrder sortOrder = _listViewWrapper.SortOrder;
            ColumnHeader colHeader = (sortCol >= 0 && sortCol < _listView.Columns.Count) ? _listView.Columns[sortCol] : null;
            CShellItemComparer comparer = null;
            if (sortOrder != SortOrder.None && colHeader != null)
            {
                comparer = new CShellItemComparer(this, sortCol, sortOrder, colHeader);
            }

            try
            {
                bool samePath = false;
                if (_currentFolderCsi == null && csi == null)
                    samePath = true;
                else if (_currentFolderCsi == null || csi == null)
                    samePath = false;
                else
                    samePath = CPidl.ResolvesToSamePathOrName(_currentFolderCsi.PIDL, csi.PIDL);

                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: Enqueueing STA work for '{csi.DisplayName}'...");

                //is this _staRunner even needed?  The current thread has to be in an sta thread since it is interacting with the ui
                var result = await _staRunner.EnqueueWork(t =>
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: Begin loading folder contents for '{csi.DisplayName}'");

                    if (token.IsCancellationRequested) return null;

                    var flags = SHCONTF.NONFOLDERS | (includeFolder ? SHCONTF.FOLDERS : 0);
                    _shellController.EnsureChildrenPopulatedAndRecent(csi, flags);

                    var dirList = new List<CShellItem>();
                    var fileList = new List<CShellItem>();

                    if (token.IsCancellationRequested) return null;
                    if (includeFolder)
                    {
                        foreach (var dir in csi.Directories)
                        {
                            if (!IsExcluded(dir)) dirList.Add(dir);
                        }
                    }

                    if (token.IsCancellationRequested) return null;

                    foreach (var file in csi.Files)
                    {
                        if (!IsExcluded(file)) fileList.Add(file);
                    }
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: {dirList.Count} dirs, {fileList.Count} files");


                    if (token.IsCancellationRequested) return null;
                    
                    var combined = new List<CShellItem>(dirList.Count + fileList.Count);
                    if (includeFolder) combined.AddRange(dirList);
                    combined.AddRange(fileList);

                    // Warming up
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: Warming up {combined.Count} items...");

                    if (token.IsCancellationRequested) return null;

                    // Fire bulk column data event so external handlers can do a single
                    // DB query for all items instead of one query per item.
                    ExpListBulkColumnDataRequested?.Invoke(this, new ExpListBulkColumnDataEventArgs(combined));

                    if (token.IsCancellationRequested) return null;

                    foreach (var item in combined)
                    {
                        if (token.IsCancellationRequested) return null;
                        
                        // Icon index
                        item.ImageIndex = _imageListOrchestrator.GetInitialImageIndex(item);
                    }
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: Warmup complete");

                    
                    // Sort according to current settings after data is fetched
                    if (comparer != null)
                    {
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: Sorting...");
                        combined.Sort(comparer);
                    }
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync.STA: Complete, returning {combined.Count} items");

                    if (token.IsCancellationRequested) return null;
                    
                    return new
                    {
                        Items = combined,
                        FolderCsi = csi,
                        IsSamePath = samePath
                    };
                }, token); //end async function

                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: STA work returned, result={result != null}, cancelled={token.IsCancellationRequested}");

                //already handled by bulk fetch above
                // Populate externally supplied custom-column values while the loaded
                // items are on the UI thread. This ensures callers can inspect column
                // data immediately after LoadDirectoryAsync completes, even for items
                // that have not yet been materialized by the ListView.
                //foreach (var item in result.Items)
                //{
                //    if (token.IsCancellationRequested) return false;
                //    EnsureCustomColumnDataFetched(item);
                //}

                if (result != null)
                {
                    if (token.IsCancellationRequested) return false;

                    if (InvokeRequired) Debug.WriteLine("ERROR: begin invoke required but not being used in explist.");
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: Updating ListView with {result.Items.Count} items...");
                    
                    _listView.BeginUpdate();
                    try
                    {
                        _listViewWrapper.Clear();

                        // Dispose old ImageLists and create a fresh one to prevent
                        // GDI handle exhaustion from accumulated thumbnails across navigations.
                        _imageListOrchestrator.ResetForNewFolder();

                        _listViewWrapper.AddRange(result.Items);

                        // Apply pre-load filter if set. All items are in the master list;
                        // the filter creates the filtered view that the ListView displays.
                        if (_filter != null)
                        {
                            _listViewWrapper.SetFilter(_filter);
                        }

                        _listView.Tag = csi;

                        CurrentFolderCsi = csi;

                        OnScroll();
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: ListView update complete");
                    }
                    finally
                    {
                        _listView.EndUpdate();
                    }

                    ExpListDirectoryLoaded?.Invoke(result.Items.Count);
                }
                else
                {
                    throw new Exception("ERROR: LoadDirectoryBaseAsync - Failed to load directory contents.");
                }
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: ERROR - {ex}");
                return false;
            }
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ExpList.LoadDirectoryBaseAsync: End for '{csi?.FullPath}'");
            return true;
        }

        private void ClearListView()
        {
            _listView.BeginUpdate();
            try
            {
                CurrentFolderCsi = null;
                _listViewWrapper.Clear();
                _listView.Tag = null;
            }
            finally
            {
                _listView.EndUpdate();
            }
        }


        /// <summary>
        /// Gets the zero-based index of the item identified by the specified full path.
        /// </summary>
        /// <remarks>Lookup is performed against an internal dictionary; -1 indicates no entry exists for
        /// the provided path.  Probably only works for virtual mode.
        /// </remarks>
        /// <param name="fullPath">The full path identifying the item to look up.</param>
        /// <returns>The zero-based index of the item if found; otherwise -1.</returns>
        public int GetIndexFromFullPath(string fullPath)
        {
            return _listViewWrapper.GetIndexFromFullPath(fullPath);
        }

        /// <summary>
        /// Returns true if the item at the given index is within the currently
        /// visible viewport. See <see cref="VirtualListViewWrapper.IsItemVisible"/>.
        /// </summary>
        internal bool IsItemVisible(int index)
        {
            return _listViewWrapper.IsItemVisible(index);
        }

        /// <summary>
        /// Finds a ListViewItem by its display name (case-insensitive).
        /// </summary>
        public ListViewItem FindItemByName(string name)
        {
            var fullPath = CurrentPath + name;
            return FindItemByPath(fullPath);
        }

        /// <summary>
        /// Finds a ListViewItem by its Shell ID (PIDL).
        /// </summary>
        /// <remarks>This is inefficient and takes O(n) time.</remarks>
        public ListViewItem FindItemByPidl(IntPtr pidl)
        {
            Debug.WriteLine("ExpList: FindItemByPidl Begin");
            try
            {
                for (int i = 0; i < _listViewWrapper.Count; i++)
                {
                    var item = _listViewWrapper.GetItem(i);
                    if (item != null && (CPidl.IsBinaryEqual(item.PIDL, pidl) || CPidl.ResolvesToSamePathOrName(item.PIDL, pidl)))
                        return _listViewWrapper.GetListViewItem(i);
                }
                return null;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindItemByPidl End");
            }
        }

        /// <summary>
        /// Finds a ListViewItem by its full filesystem path.
        /// </summary>
        public ListViewItem FindItemByPath(string path)
        {
            Debug.WriteLine("ExpList: FindItemByPath Begin");
            try
            {
                int index = _listViewWrapper.GetIndexFromFullPath(path);
                if (index >= 0)
                    return _listViewWrapper.GetListViewItem(index);
                return null;
            }
            finally
            {
                //Debug.WriteLine("ExpList: FindItemByPath End");
            }
        }

        #endregion
    }
}