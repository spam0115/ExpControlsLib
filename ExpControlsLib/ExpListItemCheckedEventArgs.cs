using System;
using System.Runtime.Versioning;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ItemChecked"/> event.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ExpListItemCheckedEventArgs : EventArgs
    {
        /// <summary>Gets the <see cref="CShellItem"/> whose checked state changed.</summary>
        public CShellItem Item { get; }

        /// <summary>Gets the view index of the item in the current (possibly filtered) view.</summary>
        public int ViewIndex { get; }

        /// <summary>Gets the new checked state.</summary>
        public bool Checked { get; }

        /// <param name="item">The shell item whose state changed.</param>
        /// <param name="viewIndex">Its index in the active view.</param>
        /// <param name="checked">The new checked value.</param>
        public ExpListItemCheckedEventArgs(CShellItem item, int viewIndex, bool @checked)
        {
            Item = item;
            ViewIndex = viewIndex;
            Checked = @checked;
        }
    }
}
