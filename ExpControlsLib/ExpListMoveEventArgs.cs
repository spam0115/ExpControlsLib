using System;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListMove"/> event.
    /// </summary>
    public class ExpListMoveEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the items to be moved.
        /// </summary>
        public CShellItem[] Items { get; }

        public ExpListMoveEventArgs(CShellItem[] items)
        {
            Items = items;
        }
    }
}
