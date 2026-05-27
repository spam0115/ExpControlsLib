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

        /// <summary>
        /// Event Raised when a Drag is started from the associated Control
        /// </summary>
        public event DragStartEventHandler DragStart;

        /// <summary>
        /// Event Raised when a Drag from the associated Control is complete (Dropped)
        /// </summary>
        public event EventHandler DragEnd;

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
        private void ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Guard against accidental drags by checking if the mouse has moved beyond the system drag threshold
            Point currentPoint = m_Client.PointToClient(Cursor.Position);
            Rectangle dragRect = new Rectangle(
                m_DragStartPoint.X - SystemInformation.DragSize.Width / 2,
                m_DragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);

            if (dragRect.Contains(currentPoint))
            {
                return;
            }

            ReleaseCom();

            startButton = e.Button;
            CShellItem item;

            if (isTreeView) // Can only drag 1 Item
            {
                item = (e.Item as TreeNode)?.Tag as CShellItem;
                if (item == null)
                    throw new ArgumentException("CDragWrapper -- Invalid item to drag -- No CShellItem in Tag");

                dataObjectPtr = ShellHelper.GetIDataObject(new[] { item });
            }
            else // ListView may have more than one item to drag
            {
                // All items to drag MUST be in the same Folder!
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
                dataObjectPtr = ShellHelper.GetIDataObject(items);
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

                DragStart?.Invoke(sender, new DragStartEventArgs(parent, m_Client));
                ShellAPI.DoDragDrop(dataObjectPtr, this, allowedEffects, out effects);
                DragEnd?.Invoke(m_Client, EventArgs.Empty);
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
    public delegate void DragStartEventHandler(object sender, DragStartEventArgs e);

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
        public DragStartEventArgs(CShellItem parent, Control dragStartControl)
        {
            m_parent = parent;
            m_DragStartControl = dragStartControl;
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
    }
}