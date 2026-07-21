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
    /// <summary>Provides checkbox state synchronization between the ListView and shell-item models.</summary>
    public partial class ExpList
    {
        #region Checkbox Support

        /// <summary>
        /// Handles the inner ListView's <c>ItemChecked</c> event.
        /// Writes the new state back to the <see cref="CShellItem"/> model and raises
        /// <see cref="ItemChecked"/> on <see cref="ExpList"/>.
        /// Suppressed while <see cref="VirtualListViewWrapper"/> is materializing items.
        /// </summary>
        private void ExpFileList_ItemChecked(object? sender, ItemCheckedEventArgs e)
        {
            if (_listViewWrapper.SuppressCheckEvents) return;
            if (IsShuttingDown) return;

            int viewIndex = e.Item.Index;
            var csi = _listViewWrapper.GetShellItemAtViewIndex(viewIndex);
            if (csi == null) return;

            csi.Checked = e.Item.Checked;
            ItemChecked?.Invoke(this, new ExpListItemCheckedEventArgs(csi, viewIndex, csi.Checked));
        }

        /// <summary>
        /// Programmatically sets the checked state of <paramref name="item"/>.
        /// Updates both the <see cref="CShellItem"/> model and the visible
        /// <see cref="ListViewItem"/> if currently cached. Raises <see cref="ItemChecked"/>.
        /// </summary>
        public void SetChecked(CShellItem item, bool value)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.Checked = value;

            int viewIndex = _listViewWrapper.GetIndexFromFullPath(item.FullPath);
            _listViewWrapper.SyncCheckedInCache(viewIndex, value);

            ItemChecked?.Invoke(this, new ExpListItemCheckedEventArgs(item, viewIndex, value));
        }

        /// <summary>
        /// Checks all items in the master list.
        /// Does not raise <see cref="ItemChecked"/> per item.
        /// </summary>
        public void CheckAll()   => SetAllChecked(true);

        /// <summary>
        /// Unchecks all items in the master list.
        /// Does not raise <see cref="ItemChecked"/> per item.
        /// </summary>
        public void UncheckAll() => SetAllChecked(false);

        private void SetAllChecked(bool value)
        {
            // Update the model for every item regardless of virtual/non-virtual mode.
            foreach (var csi in _listViewWrapper.AllShellItems)
                csi.Checked = value;

            _listViewWrapper.SuppressCheckEvents = true;
            try
            {
                if (_listViewWrapper.VirtualMode)
                {
                    // Clear the item cache: the next RetrieveVirtualItem for each row will
                    // call CreateLviFromCsi which applies csi.Checked automatically.
                    _listViewWrapper.InvalidateCache();
                    _listView.Invalidate();
                }
                else
                {
                    _listView.BeginUpdate();
                    foreach (ListViewItem lvi in _listView.Items)
                    {
                        if (lvi.Tag is CShellItem csi)
                            lvi.Checked = csi.Checked;
                    }
                    _listView.EndUpdate();
                }
            }
            finally
            {
                _listViewWrapper.SuppressCheckEvents = false;
            }
        }

        #endregion
    }
}
