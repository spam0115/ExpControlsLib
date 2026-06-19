using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListGetColumnData"/> event.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ExpListGetColumnDataEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the <see cref="CShellItem"/> being displayed.
        /// </summary>
        public CShellItem Item { get; }

        /// <summary>
        /// Gets the dictionary of column data, keyed by column name.
        /// </summary>
        //public Dictionary<string, ListViewSubitemData> ColumnData { get; } = new Dictionary<string, ListViewSubitemData>();

        public ExpListGetColumnDataEventArgs(CShellItem item)
        {
            Item = item;
        }
    }
}
