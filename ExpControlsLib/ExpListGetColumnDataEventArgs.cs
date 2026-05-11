using System;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListGetColumnData"/> event.
    /// </summary>
    public class ExpListGetColumnDataEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the <see cref="CShellItem"/> being displayed.
        /// </summary>
        public CShellItem Item { get; }

        /// <summary>
        /// Gets the <see cref="ColumnHeader"/> for which data is requested.
        /// </summary>
        public ColumnHeader Column { get; }

        /// <summary>
        /// Gets or sets the text to display in the column.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets the tag for the sub-item.
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the event has been handled.
        /// If true, the default property mapping logic will be skipped.
        /// </summary>
        public bool Handled { get; set; }

        public ExpListGetColumnDataEventArgs(CShellItem item, ColumnHeader column)
        {
            Item = item;
            Column = column;
        }
    }
}
