using WindowsApiLib.Shell;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ExpControlsLib
{

    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CDragWrapper : IDropSource, IDisposable
    {
        // The Control which is our client
        private readonly Control m_Client;

        // The point where the mouse was pressed down
        private Point m_DragStartPoint;

        // The pointer to the IDataObject being dragged
        private IntPtr dataObjectPtr;

        // If true then working for TreeView, false then working for ListView
        private readonly bool isTreeView;

        // The mouseButtons state when a drag starts
        private MouseButtons startButton;

        // A bool to indicate whether this class has been disposed
        private bool disposed = false;

        // Reentrancy guard: DoDragDrop runs a modal message loop that pumps pending
        // BeginInvoke callbacks. A second StartDragInternal queued before the drag
        // started would otherwise reenter while the first DoDragDrop is still on the
        // stack, releasing the in-use IDataObject / IDropTarget and corrupting the
        // COM apartment (System.ExecutionEngineException).
        private bool m_IsDragging = false;

        // Deferral fields
        private object? m_PendingItem;
        private MouseButtons m_PendingButton;
        private const int SecondaryThreshold = 20; // Radius in pixels for less sensitivity before initiating drag

        /// <summary>
        /// Event Raised when a Drag is started from the associated Control
        /// </summary>
        public event DragStartEventHandler DragStart;

        /// <summary>
        /// Event Raised when a Drag from the associated Control is complete (Dropped)
        /// </summary>
        public event DragEndEventHandler DragEnd;

        /// <summary>
        /// Creates and registers this instance to receive events when an item is being dragged
        /// </summary>
        /// <param name="ctl">The ListView or TreeView for which to support the drag</param>
        public CDragWrapper(Control ctl)
        {
            if (ctl is ListView listView)
            {
                listView.ItemDrag += ItemDrag;
                isTreeView = false;
            }
            else if (ctl is TreeView treeView)
            {
                treeView.ItemDrag += ItemDrag;
                isTreeView = true;
            }
            else
            {
                throw new ArgumentException("CDragWrapper -- Does not support drags from " + ctl.GetType().Name);
            }

            m_Client = ctl;
            m_Client.MouseDown += OnMouseDown;
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            m_DragStartPoint = e.Location;
        }

        /// <summary>
        /// This method initialises the dragging of a TreeNode or 1 or more ListViewItems
        /// </summary>
        private void ItemDrag(object? sender, ItemDragEventArgs e)
        {
            // Guard against accidental drags by checking if the mouse has moved beyond the system drag threshold
            Point currentPoint = m_Client.PointToClient(Cursor.Position);
            Rectangle dragRect = new Rectangle(
                m_DragStartPoint.X - SystemInformation.DragSize.Width,
                m_DragStartPoint.Y - SystemInformation.DragSize.Height,
                SystemInformation.DragSize.Width * 2,
                SystemInformation.DragSize.Height * 2);

            if (dragRect.Contains(currentPoint))
            {
                return;
            }

            // Secondary check: If we haven't reached our own larger threshold yet, defer the drag
            if (Math.Abs(currentPoint.X - m_DragStartPoint.X) < SecondaryThreshold &&
                Math.Abs(currentPoint.Y - m_DragStartPoint.Y) < SecondaryThreshold)
            {
                if (m_PendingItem == null)
                {
                    m_PendingItem = e.Item;
                    m_PendingButton = e.Button;
                    m_Client.MouseMove += OnMouseMoveDeferred;
                    m_Client.MouseUp += OnMouseUpDeferred;
                }
                return;
            }

            StartDragInternal(sender, e.Item, e.Button);
        }

        private void OnMouseMoveDeferred(object? sender, MouseEventArgs e)
        {
            if (m_PendingItem == null) return;

            // Check if we've finally crossed the secondary threshold
            if (Math.Abs(e.X - m_DragStartPoint.X) >= SecondaryThreshold ||
                Math.Abs(e.Y - m_DragStartPoint.Y) >= SecondaryThreshold)
            {
                object? item = m_PendingItem;
                MouseButtons button = m_PendingButton;
                CleanupDeferred();

                // Use BeginInvoke to ensure the current MouseMove event completes 
                // before starting the modal DoDragDrop loop.
                m_Client.BeginInvoke(new Action(() =>
                {
                    StartDragInternal(m_Client, item, button);
                }));
            }
        }

        private void OnMouseUpDeferred(object? sender, MouseEventArgs e)
        {
            CleanupDeferred();
        }

        private void CleanupDeferred()
        {
            if (m_PendingItem != null)
            {
                m_PendingItem = null;
                m_Client.MouseMove -= OnMouseMoveDeferred;
                m_Client.MouseUp -= OnMouseUpDeferred;
            }
        }

        private void StartDragInternal(object? sender, object? itemToDrag, MouseButtons button)
        {
            if (m_IsDragging) return;          // block reentrancy while DoDragDrop pumps messages
            m_IsDragging = true;
            try
            {
                ReleaseCom();
                startButton = button;

                CShellItem item;
                CShellItem[] itemsToReport;
                if (isTreeView) // Can only drag 1 Item
                {
                    item = (itemToDrag as TreeNode)?.Tag as CShellItem;
                    if (item == null)
                        return;

                    itemsToReport = new[] { item };
                    dataObjectPtr = ShellHelper.GetIDataObject(itemsToReport);
                }
                else // ListView may have more than one item to drag
                {
                    var ctl = (ListView)m_Client;
                    if (ctl.SelectedIndices.Count == 0) return;

                    var items = new CShellItem[ctl.SelectedIndices.Count];

                    // Get first item to establish parent
                    CShellItem firstItem = null;
                    if (ctl.Parent is ExpList expList)
                    {
                        firstItem = expList.GetItem(ctl.SelectedIndices[0]);
                    }
                    else if (!ctl.VirtualMode)
                    {
                        firstItem = ctl.Items[ctl.SelectedIndices[0]].Tag as CShellItem;
                    }

                    if (firstItem == null) return;
                    var parent = firstItem.Parent;

                    for (int i = 0; i < ctl.SelectedIndices.Count; i++)
                    {
                        int index = ctl.SelectedIndices[i];
                        CShellItem itemTag = null;
                        if (ctl.Parent is ExpList el)
                        {
                            itemTag = el.GetItem(index);
                        }
                        else if (!ctl.VirtualMode)
                        {
                            itemTag = ctl.Items[index].Tag as CShellItem;
                        }

                        if (itemTag == null || !ReferenceEquals(parent, itemTag.Parent))
                            return;

                        items[i] = itemTag;
                    }

                    item = items[0];
                    itemsToReport = items;
                    dataObjectPtr = ShellHelper.GetIDataObject(itemsToReport);
                }

                if (dataObjectPtr != IntPtr.Zero)
                {
                    DragDropEffects allowedEffects;
                    DragDropEffects effects;
                    CShellItem parent = item.Parent ?? item;

                    if (m_Client is TreeView)
                    {
                        allowedEffects = DragDropEffects.Copy | DragDropEffects.Move;
                    }
                    else // must be ListView
                    {
                        allowedEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
                    }

                    // Right-button drags show a context menu at the drop target.
                    // The shell builds the menu from allowedEffects (Move, Copy, etc.).
                    // Keep the same allowed effects so the user gets both Move and Copy.

                    DragStart?.Invoke(sender, new DragStartEventArgs(parent, m_Client, itemsToReport));
                    int hr = ShellAPI.DoDragDrop(dataObjectPtr, this, allowedEffects, out effects);
                    bool dropCompleted = hr != ShellAPI.DRAGDROP_S_CANCEL;
                    DragEnd?.Invoke(m_Client, new DragEndEventArgs(effects, itemsToReport, dropCompleted));
                }
            }
            finally
            {
                m_IsDragging = false;
            }
        }


        /// <summary>
        /// Provides a minimal implementation of IDropSource.QueryContinueDrag
        /// </summary>
        /// <param name="fEscapePressed">True if the Escape Key is pressed</param>
        /// <param name="grfKeyState">Which Button is pressed</param>
        /// <returns>S_CANCEL if Escape Key is pressed, S_OK otherwise</returns>
        public int QueryContinueDrag(bool fEscapePressed, ShellAPI.MK grfKeyState)
        {
            if (fEscapePressed)
            {
                return ShellAPI.DRAGDROP_S_CANCEL;
            }

            if ((startButton & MouseButtons.Left) != 0 && (grfKeyState & ShellAPI.MK.LBUTTON) == 0)
            {
                return ShellAPI.DRAGDROP_S_DROP;
            }

            if ((startButton & MouseButtons.Right) != 0 && (grfKeyState & ShellAPI.MK.RBUTTON) == 0)
            {
                return ShellAPI.DRAGDROP_S_DROP;
            }

            return ShellAPI.S_OK;
        }

        /// <summary>
        /// Used to provide a minimal implementation of IDropSource.GiveFeedback
        /// </summary>
        /// <param name="dwEffect">Unused</param>
        /// <returns>Always returns DRAGDROP_S_USEDEFAULTCURSORS</returns>
        public int GiveFeedback(DragDropEffects dwEffect)
        {
            return ShellAPI.DRAGDROP_S_USEDEFAULTCURSORS;
        }

        /// <summary>
        /// If not disposed, dispose the class
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                CleanupDeferred();
                if (m_Client != null)
                {
                    m_Client.MouseDown -= OnMouseDown;
                }
                ReleaseCom();
                GC.SuppressFinalize(this);
                disposed = true;
            }
        }

        /// <summary>
        /// Release the IDataObject and free the allocated memory
        /// </summary>
        private void ReleaseCom()
        {
            if (dataObjectPtr != IntPtr.Zero)
            {
                Marshal.Release(dataObjectPtr);
                dataObjectPtr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// The Delegate defining the signature of an Event Handler to Handle the Event Raised when
    /// a Drag is started from a Control associated with an instance of CDragWrapper.
    /// </summary>
    /// <param name="sender">The Control from which the Drag originates</param>
    /// <param name="e">A DragStartEventArgs constructed by CDragWrapper</param>
    public delegate void DragStartEventHandler(object? sender, DragStartEventArgs e);

    /// <summary>
    /// An EventArgs which provides information about a Drag started within a Control managed by an instance of CDragWrapper.
    /// </summary>
    /// <remarks>
    /// The information is the CShellItem of the Folder being Dragged or the Parent of the Items being Dragged
    /// and the Control in which the Drag originated.
    /// </remarks>
    public class DragStartEventArgs : EventArgs
    {
        private readonly CShellItem m_parent;
        private readonly Control m_DragStartControl;

        /// <summary>
        /// Contructs a new Instance of this Class
        /// </summary>
        /// <param name="parent">The Folder being Dragged or the Parent Folder of the Items being Dragged</param>
        /// <param name="dragStartControl">Control in which the Drag originated</param>
        /// <param name="items">The items being dragged</param>
        public DragStartEventArgs(CShellItem parent, Control dragStartControl, CShellItem[] items)
        {
            m_parent = parent;
            m_DragStartControl = dragStartControl;
            Items = items;
        }

        /// <summary>
        /// The Folder being Dragged or the Parent Folder of the Items being Dragged
        /// </summary>
        /// <remarks>
        /// If Drag is a single Folder then the CShellItem of that Folder. If from a ListView,
        /// then the Folder containing all Item(s) being Dragged.
        /// </remarks>
        public CShellItem Parent => m_parent;

        /// <summary>
        /// The Control in which the Drag originated
        /// </summary>
        public Control DragStartControl => m_DragStartControl;

        /// <summary>
        /// The items being dragged.
        /// </summary>
        public CShellItem[] Items { get; }
    }

    /// <summary>
    /// The Delegate defining the signature of an Event Handler to Handle the Event Raised when
    /// a Drag from a Control associated with an instance of CDragWrapper is complete.
    /// </summary>
    public delegate void DragEndEventHandler(object? sender, DragEndEventArgs e);

    /// <summary>
    /// An EventArgs which provides information about the completion of a Drag operation.
    /// </summary>
    public class DragEndEventArgs : EventArgs
    {
        /// <summary>
        /// Constructs a new instance of DragEndEventArgs.
        /// </summary>
        /// <param name="effect">The final effect of the drag-and-drop operation.</param>
        /// <param name="items">The items that were dragged.</param>
        /// <param name="dropCompleted">True if the drop was performed (DoDragDrop returned DRAGDROP_S_DROP or S_OK); false if the drag was cancelled.</param>
        public DragEndEventArgs(DragDropEffects effect, CShellItem[] items, bool dropCompleted)
        {
            Effect = effect;
            Items = items;
            DropCompleted = dropCompleted;
        }

        /// <summary>
        /// Gets the final effect of the drag-and-drop operation.
        /// For optimized moves, the shell returns DragDropEffects.None even though the move succeeded.
        /// </summary>
        public DragDropEffects Effect { get; }

        /// <summary>
        /// Gets the items that were dragged.
        /// </summary>
        public CShellItem[] Items { get; }

        /// <summary>
        /// True if the drop was actually performed (DoDragDrop returned DRAGDROP_S_DROP or S_OK).
        /// False if the drag was cancelled (DRAGDROP_S_CANCEL).
        /// When true and Effect is None, the shell performed an optimized move.
        /// </summary>
        public bool DropCompleted { get; }
    }
}
