using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Immutable record of a single completed drop operation received by an
    /// <see cref="ExpTree"/> (or other drop target). Stored in the drop history ring
    /// buffer and surfaced for undo / audit / "recent drop target" UI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class DropRecord
    {
        /// <summary>
        /// When the drop completed (local time).
        /// </summary>
        public DateTime CompletedAt { get; init; }

        /// <summary>
        /// The drag-and-drop effect reported by the shell drop target (Move, Copy, Link,
        /// or None for optimized same-volume moves).
        /// </summary>
        public DragDropEffects Effect { get; init; }

        /// <summary>
        /// The in-app destination shell item the drop landed on (the folder the
        /// <see cref="ExpTree"/> node represents). Null when the drop resolved to no
        /// in-app target (e.g. dropped on empty area of the control).
        /// </summary>
        public CShellItem? DestinationItem { get; init; }

        /// <summary>
        /// Convenience accessor for <see cref="DestinationItem"/>'s file system path,
        /// or null when <see cref="DestinationItem"/> is null.
        /// </summary>
        public string? DestinationPath => DestinationItem?.FullPath;

        /// <summary>
        /// The source items that were dragged (captured from the drag source via
        /// <see cref="DragDropContext"/> at drop time). Empty when the source items were
        /// not available (e.g. drag originated outside this process).
        /// </summary>
        public CShellItem[] SourceItems { get; init; } = Array.Empty<CShellItem>();
    }
}
