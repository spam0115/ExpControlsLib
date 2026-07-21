using System;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Event arguments for drag-and-drop completion events
    /// (<see cref="ExpList.ExpListDragCompleted"/> and
    /// <see cref="ExpTree.ExpTreeDragCompleted"/>).
    /// Fired when a drag-and-drop operation completes, providing the effect, the items
    /// involved, and (for in-app drops) the resolved destination.
    /// </summary>
    public class DragCompletedEventArgs : EventArgs
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
        /// Null when the source path is not available (e.g. copy operations, tree-target drops, or when fired
        /// from <c>DW_DragEnd</c> before the shell notification arrives).
        /// </summary>
        public string? SourcePath { get; }

        /// <summary>
        /// For move operations resolved via shell notifications: the destination path of the first item after the move.
        /// For copy operations to an in-app target: the destination folder path.
        /// Null when the destination path is not available.
        /// </summary>
        public string? DestinationPath { get; }

        /// <summary>
        /// The in-app destination shell item the drop landed on, when the drop was received
        /// by an in-process drop wrapper (CtvDropWrapper / ClvDropWrapper). Null for
        /// external drops, cancelled drops, or drops on empty areas with no resolvable
        /// destination folder. For moves resolved via shell notifications, this is the
        /// destination folder; for copies to an in-app target, the same.
        /// </summary>
        public CShellItem? DestinationItem { get; }

        public DragCompletedEventArgs(DragDropEffects effect, CShellItem[] items,
            string? sourcePath = null, string? destinationPath = null, CShellItem? destination = null)
        {
            Effect = effect;
            Items = items;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            DestinationItem = destination;
        }
    }
}
