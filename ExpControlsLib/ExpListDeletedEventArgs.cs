using System;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListDeleted"/> event.
    /// </summary>
    public class ExpListDeletedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the items that were deleted.
        /// </summary>
        public CShellItem[] Items { get; }

        /// <summary>
        /// Gets the indices the deleted items occupied before removal.
        /// </summary>
        public int[] Indices { get; }

        public ExpListDeletedEventArgs(CShellItem[] items, int[] indices)
        {
            Items = items;
            Indices = indices;
        }
    }
}
