using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Thread-static bridge that lets an in-app <see cref="IDropTarget"/> implementation
    /// (CtvDropWrapper / ClvDropWrapper) report the resolved destination shell item back
    /// to the drag source (<see cref="CDragWrapper"/>) before
    /// <see cref="ShellAPI.DoDragDrop"/> returns on the same STA thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OLE <c>DoDragDrop</c> runs a modal message loop on the STA thread that started the
    /// drag. Any in-process drop handler that participates in that drag runs its
    /// <c>IDropTarget.DragDrop</c> on that same thread before <c>DoDragDrop</c> returns.
    /// This makes a single <see cref="ThreadStaticAttribute"/> slot sufficient to carry
    /// the destination from the drop side back to the source side — no marshaling, no
    /// races. The slot is single-use: <see cref="CDragWrapper"/> clears it before each drag
    /// and consumes it immediately after <c>DoDragDrop</c> returns.
    /// </para>
    /// <para>
    /// The slot also carries the <see cref="CShellItem"/>[] being dragged (the source
    /// items), recorded by <see cref="CDragWrapper"/> before <c>DoDragDrop</c> starts and
    /// peekable (non-consuming) by an in-app drop target such as <see cref="ExpTree"/>
    /// while the drop is in progress. This lets a tree-target drop raise a
    /// <c>DragCompleted</c>-style event that includes both the destination and the source
    /// items without a separate correlation channel.
    /// </para>
    /// <para>
    /// External drops (Explorer, desktop) never enter an in-process drop handler, so the
    /// slot remains empty and <see cref="TryConsume"/> reports no destination — callers
    /// fall back to the existing shell change-notification path for moves.
    /// </para>
    /// <para>
    /// This type is <see langword="internal"/>; it is an implementation detail of the
    /// drag-and-drop plumbing and not part of the public surface.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal static class DragDropContext
    {
        [ThreadStatic]
        private static CShellItem? t_destination;

        [ThreadStatic]
        private static bool t_hasRecord;

        [ThreadStatic]
        private static CShellItem[]? t_sourceItems;

        /// <summary>
        /// Records the source items being dragged. Called by
        /// <see cref="CDragWrapper.DoDragInternal"/> before
        /// <see cref="ShellAPI.DoDragDrop"/> starts, so an in-app drop target that fires
        /// during the modal loop can <see cref="PeekSource"/> to obtain them. The items
        /// persist for the duration of the drag and are cleared together with the
        /// destination by <see cref="TryConsume"/> / <see cref="Clear"/>.
        /// </summary>
        /// <param name="items">The shell items being dragged. Never null.</param>
        internal static void RecordSource(CShellItem[] items)
        {
            t_sourceItems = items;
        }

        /// <summary>
        /// Returns the recorded source items without clearing the slot. Called by an
        /// in-app drop target (e.g. <see cref="ExpTree.DragWrapper_ShDragDrop"/>) while the
        /// drop is still in progress so it can include them in a <c>DragCompleted</c>
        /// event. Returns <see langword="null"/> when no drag is active on this thread.
        /// </summary>
        /// <returns>The recorded source items, or <see langword="null"/>.</returns>
        internal static CShellItem[]? PeekSource()
        {
            return t_sourceItems;
        }

        /// <summary>
        /// Called by an in-app drop wrapper once the shell <c>IDropTarget</c> for the drop
        /// has been invoked. Stamps the resolved destination shell item and the final effect
        /// onto the current thread's slot so the drag source can read them when
        /// <c>DoDragDrop</c> returns.
        /// </summary>
        /// <param name="item">
        /// The <see cref="CShellItem"/> of the folder the drop landed on (the drop
        /// target). Pass <see langword="null"/> if the drop resolved to no in-app target
        /// (e.g. dropped on empty area of the control with no parent target).
        /// </param>
        /// <param name="effect">
        /// The final <see cref="DragDropEffects"/> reported by the shell drop target.
        /// </param>
        internal static void RecordDestination(CShellItem? item, DragDropEffects effect)
        {
            t_destination = item;
            t_hasRecord = true;
        }

        /// <summary>
        /// Atomically reads and clears the recorded destination (and the source items).
        /// Called by <see cref="CDragWrapper"/> immediately after
        /// <see cref="ShellAPI.DoDragDrop"/> returns.
        /// </summary>
        /// <param name="destination">
        /// The recorded destination <see cref="CShellItem"/>, or <see langword="null"/>
        /// if the drop did not land on an in-app target (or landed on an empty area).
        /// </param>
        /// <param name="effect">
        /// The final <see cref="DragDropEffects"/> reported by the shell drop target, or
        /// <see cref="DragDropEffects.None"/> if no record was made.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if a record was made for this drag; otherwise
        /// <see langword="false"/> (external drop, or dropped on an empty area).
        /// </returns>
        internal static bool TryConsume(out CShellItem? destination, out DragDropEffects effect)
        {
            bool has = t_hasRecord;
            destination = t_destination;
            effect = DragDropEffects.None;
            t_destination = null;
            t_hasRecord = false;
            t_sourceItems = null;
            return has;
        }

        /// <summary>
        /// Clears any recorded destination and source items without reading them. Called by
        /// <see cref="CDragWrapper"/> before starting a drag (defensive — ensures a
        /// stale slot from a previous in-app drop cannot leak into the next drag on the
        /// same thread) and in its <see langword="finally"/> block.
        /// </summary>
        internal static void Clear()
        {
            t_destination = null;
            t_hasRecord = false;
            t_sourceItems = null;
        }
    }
}
