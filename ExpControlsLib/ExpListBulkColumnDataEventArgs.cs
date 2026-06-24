using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListBulkColumnDataRequested"/> event.
    /// Contains all items in the directory being loaded, allowing bulk column data fetching.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ExpListBulkColumnDataEventArgs : EventArgs
    {
        /// <summary>
        /// Gets all <see cref="CShellItem"/>s in the directory being loaded.
        /// </summary>
        public IReadOnlyList<CShellItem> Items { get; }

        public ExpListBulkColumnDataEventArgs(IReadOnlyList<CShellItem> items)
        {
            Items = items;
        }
    }
}
