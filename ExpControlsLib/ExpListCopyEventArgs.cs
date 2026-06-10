using System;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListCopy"/> event.
    /// </summary>
    public class ExpListCopyEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the items to be copied.
        /// </summary>
        public CShellItem[] Items { get; }

        public ExpListCopyEventArgs(CShellItem[] items)
        {
            Items = items;
        }
    }
}
