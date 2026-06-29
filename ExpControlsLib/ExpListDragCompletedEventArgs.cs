using System;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for the <see cref="ExpList.ExpListDragCompleted"/> event.
    /// Fired when a drag-and-drop operation completes, providing the effect and the items involved.
    /// </summary>
    public class ExpListDragCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the effect of the completed drag operation (Move, Copy, Link, or None for optimized moves).
        /// </summary>
        public DragDropEffects Effect { get; }

        /// <summary>
        /// Gets the items that were dragged.
        /// </summary>
        public CShellItem[] Items { get; }

        /// <summary>
        /// Gets whether the drag was a move operation (explicit Move or optimized same-volume move returning None).
        /// </summary>
        public bool IsMove => Effect == DragDropEffects.Move || Effect == DragDropEffects.None;

        /// <summary>
        /// Gets whether the drag was a copy operation.
        /// </summary>
        public bool IsCopy => Effect == DragDropEffects.Copy;

        /// <summary>
        /// For move operations resolved via shell notifications: the original path of the first item before the move.
        /// Null when the source path is not available (e.g. copy operations or when fired from <c>DW_DragEnd</c>
        /// before the shell notification arrives).
        /// </summary>
        public string? SourcePath { get; }

        /// <summary>
        /// For move operations resolved via shell notifications: the destination path of the first item after the move.
        /// Null when the destination path is not available.
        /// </summary>
        public string? DestinationPath { get; }

        public ExpListDragCompletedEventArgs(DragDropEffects effect, CShellItem[] items,
            string? sourcePath = null, string? destinationPath = null)
        {
            Effect = effect;
            Items = items;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
    }
}
