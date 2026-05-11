using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    /// <summary>The CtvDropWrapper class deals with the mechanics of receiving a
    /// Drag/Drop operation for a TreeView Control. In effect, it implements the IDropTarget interface
    /// for a TreeView. It is designed to handle a TreeView which <b>must</b> have CShItems 
    /// in the Tags of the TreeNodes contained in the control.
    /// </summary>
    /// <remarks>
    /// <para>The class recieves the DragEnter, DragLeave, DragOver, and DragDrop events for
    /// the associated TreeView, performs the Drag specific processing, and raises corresponding 
    /// Events for the associated TreeView to allow the TreeView to do any GUI related processing.</para>
    /// The interesting part of this class is that it makes no decisions about the drag
    /// nor does any Drop related processing itself. Instead, it acts as a broker between
    /// the Drag/Drop operation and the IDropTarget interface of the underlying 
    /// Shell Folder.  This allows the Shell Folder, which may be a Shell Extention, to
    /// perform whatever action it needs to in order to effect the Drag/Drop.
    /// The benefit of this approach is that Drag/Drop targets need not be part of the
    /// File System.
    /// </remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CtvDropWrapper : WindowsApiLib.Shell.IDropTarget, IDisposable
    {

        #region    Private Fields

        private TreeView m_TreeView;                  // The Tree if m_treeview is a TreeView, else nothing
        private IntPtr m_DataObj;                     // The COM interface to IDragData - saved in DragEnter
        private int m_Original_Effect;            // Save it
        private WindowsApiLib.Shell.IDropTarget m_LastTarget;    // IDropTarget of most recent Folder dragged over
        private TreeNode m_LastNode;                  // Most recent node dragged over
        private readonly IDropTargetHelper m_DropHelper;       // IDropTargetHelper interface for this control
        private bool m_disposed = false;           // To detect redundant Dispose calls

        #endregion

        #region    Public Events
        /// <summary>
    /// The Event Raised by this Class to inform the TreeView that a Drag has entered the TreeView
    /// </summary>
    /// <param name="pDataObj">Pointer to the DataObject being Dragged.</param>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pdwEffect">The type of Drop actions permitted by the Drag Source</param>
        public event ShDragEnterEventHandler ShDragEnter;

        public delegate void ShDragEnterEventHandler(IntPtr pDataObj, int grfKeyState, int pdwEffect);


        /// <summary>
    /// The Event Raised by this Class to inform the TreeView that a Drag has moved over the TreeView
    /// </summary>
    /// <param name="Node">The TreeNode that the Drag is over</param>
    /// <param name="ClientPoint">Location, in Client coordinates, of the mouse.</param>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pdwEffect">The type of Drop actions permitted by the Drag Source</param>
        public event ShDragOverEventHandler ShDragOver;

        public delegate void ShDragOverEventHandler(TreeNode Node, Point ClientPoint, int grfKeyState, int pdwEffect);


        /// <summary>
    /// The Event Raised by this Class to inform the TreeView that a Drag has left the TreeView
    /// </summary>
        public event ShDragLeaveEventHandler ShDragLeave;

        public delegate void ShDragLeaveEventHandler();

        /// <summary>
    /// The Event Raised by this Class to inform the TreeView that a Drop has occured on the TreeView
    /// </summary>
    /// <param name="Node">The TreeNode that the Drop occured on</param>
    /// <param name="grfKeyState"></param>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
        public event ShDragDropEventHandler ShDragDrop;

        public delegate void ShDragDropEventHandler(TreeNode Node, int grfKeyState, int pdwEffect);


        #endregion

        #region    Public Enum -- KeyStates
        /// <summary>
    /// Bit mask showing the state of Control Keys and Mouse Buttons during a Drag
    /// </summary>
        [Flags()]
        public enum KeyStates
        {
            LButtonMask = 1,
            RButtonMask = 2,
            ShiftMask = 4,
            CtrlMask = 8,
            MButtonMask = 16,
            AltMask = 32
        }
        #endregion

        #region    Constructor
        /// <summary>
    /// Initializes a new instance of the Class.
    /// </summary>
    /// <param name="ctl">The TreeView for which this instance will Handle Drag/Drop</param>
    /// <remarks>Registers itself to Handle Drag/Drops for the TreeView.</remarks>
        public CtvDropWrapper(TreeView ctl)
        {
            m_TreeView = ctl;
            // Correct type of Control, register IDropTarget for it
            if (m_TreeView.IsHandleCreated)
            {
                if (Application.OleRequired() == ApartmentState.STA)
                {
                    int res;
                    res = RegisterDragDrop(m_TreeView.Handle, this);
                    if (!(res == 0) | res == -2147221247)
                    {
                        Marshal.ThrowExceptionForHR(res);
                    }
                }
                else
                {
                    throw new ThreadStateException("ThreadMustBeSTA");
                }
            }
            else
            {
                throw new ArgumentException(m_TreeView.Name + " Handle has not been created");
            }
            m_TreeView.HandleCreated += View_HandleCreated;
            m_TreeView.HandleDestroyed += View_HandleDestroyed;

            IntPtr dropHelperPtr;     // historical place to accept input from nxt call
            ShellHelper.GetIDropTargetHelper(out dropHelperPtr, out m_DropHelper);
        }
        #endregion

        #region    Handle Changes

        private void View_HandleCreated(object sender, EventArgs e)
        {
            int res;
            res = RegisterDragDrop(m_TreeView.Handle, this);
            if (!(res == 0) | res == -2147221247)
            {
                Marshal.ThrowExceptionForHR(res);
                // Throw New Exception("Failed to Register DragDrop for CDropWrapper on " & ctl.Name)
            }
        }

        private void View_HandleDestroyed(object sender, EventArgs e)
        {
            if (m_TreeView is not null && m_TreeView.IsHandleCreated)
            {
                RevokeDragDrop(m_TreeView.Handle);
                // UPDATE: Added remove handler calls to allow a treeview
                // to be shown multiple times in a modal dialog
                m_TreeView.HandleCreated -= View_HandleCreated;
                m_TreeView.HandleDestroyed -= View_HandleDestroyed;
                m_TreeView = null;
            }
        }

        #endregion

        #region    ResetPreviousTarget -- a utility/cleanup Method
        private void ResetPrevTarget()
        {
            if (!(m_LastTarget == null))
            {
                int hr = m_LastTarget.DragLeave();
                Marshal.ReleaseComObject(m_LastTarget);
                m_LastTarget = null;
            }
            m_LastNode = null;
        }
        #endregion

        #region    DragEnter
        /// <summary>
    /// For internal use only<br />
    /// DragEnter is called by the Shell as a drag enters the TreeView. Its main function is to
    /// save off the IDataObject of the Drag for use in DragOver processing, since DragOver does
    /// not receive the IDataObject as a parameter.
    /// </summary>
    /// <param name="pDataObj">IDataObject of the Folder of the Item being dragged, containing references to
    /// the item(s) being Dragged.</param>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pt">Location, in screen coordinates, of the mouse.</param>
    /// <param name="pdwEffect">Permitted Drop actions as set by the DragSource.</param>
    /// <returns>Always returns S_OK (0)</returns>
        internal int DragEnter(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)



        {

            ResetPrevTarget();                       // should be redundant, but, just in case ...
            m_Original_Effect = (int)pdwEffect;
            m_DataObj = pDataObj;

            ShDragEnter?.Invoke(pDataObj, (int)grfKeyState, (int)pdwEffect);

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragEnter(m_TreeView.Handle, pDataObj, ref pt, pdwEffect);
            }
            return S_OK;
        }

        int WindowsApiLib.Shell.IDropTarget.DragEnter(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect) => DragEnter(pDataObj, grfKeyState, pt, ref pdwEffect);
        #endregion

        #region    DragOver
        /// <summary>
    /// For internal use only
    /// Entered when a Drag moves over the surface of the associated Control.<br />
    /// </summary>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pt">Location, in screen coordinates, of the mouse.</param>
    /// <param name="pdwEffect">Permitted Drop actions as set by the DragSource and modified by
    /// candidate DropTargets.</param>
    /// <returns>Always returns S_OK (0)</returns>
        internal int DragOver(MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)


        {

            TreeNode tn;
            var ptClient = m_TreeView.PointToClient(new Point(pt.x, pt.y));
            tn = m_TreeView.GetNodeAt(ptClient);
            if (tn == null)                   // not over a TreeNode
            {
                ResetPrevTarget();
            }
            else                                    // currently over Treenode
            {
                if (!(m_LastNode == null))   // not the first, check if same as last time
                {
                    if (ReferenceEquals(tn, m_LastNode))
                    {
                        if (m_DropHelper is not null)
                            m_DropHelper.DragOver(ref pt, pdwEffect); // 7/11/2012
                        return S_OK;        // all set up anyhow
                    }
                    else
                    {
                        ResetPrevTarget();
                        m_LastNode = tn;
                    }
                }
                else    // is the first
                {
                    ResetPrevTarget();   // just in case
                    m_LastNode = tn;
                }     // save current node

                // Drag is now over a new node. Get the IDropTarget of the Folder and interact with it

                CShellItem CSI = (CShellItem)tn.Tag;
                if (CSI.IsDropTarget)
                {
                    m_LastTarget = CSI.GetDropTargetOf(m_TreeView);
                    if (!(m_LastTarget == null))
                    {
                        pdwEffect = (DragDropEffects)m_Original_Effect;

                        int res = m_LastTarget.DragEnter(m_DataObj, grfKeyState, pt, ref pdwEffect);
                        if (res == 0)
                        {
                            res = m_LastTarget.DragOver(grfKeyState, pt, ref pdwEffect);
                        }
                        if (res != 0)
                        {
                            Marshal.ThrowExceptionForHR(res);
                        }
                    }
                    else
                    {
                        pdwEffect = DragDropEffects.None;
                    } // couldn't get IDropTarget, so report effect None
                }
                else
                {
                    pdwEffect = DragDropEffects.None;
                }  // CSI not a drop target, so report effect None
                ShDragOver?.Invoke(tn, ptClient, (int)grfKeyState, (int)pdwEffect);
            }

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragOver(ref pt, pdwEffect);
            }
            return S_OK;
        }

        int WindowsApiLib.Shell.IDropTarget.DragOver(MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect) => DragOver(grfKeyState, pt, ref pdwEffect);
        #endregion

        #region    DragLeave
        /// <summary>
    /// DragLeave is raised by the Shell when the Drag is cancelled or otherwise leaves the underlying
    /// TreeView.  The handler does whatever cleanup is needed to prepare for another DragEnter.
    /// </summary>
    /// <returns>Always returns S_OK</returns>
    /// <remarks></remarks>
        public int DragLeave()
        {
            // Debug.WriteLine("In DragLeave")
            m_Original_Effect = 0;
            ResetPrevTarget();
            m_DataObj = IntPtr.Zero;
            ShDragLeave?.Invoke();

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragLeave();
            }
            return S_OK;
        }
        #endregion

        #region    DragDrop
        /// <summary>
    /// For internal use only
    /// Entered when a DragDrop has occurred on the associated Control.
    /// </summary>
    /// <param name="pDataObj">Pointer to the IDataObject</param>
    /// <param name="grfKeyState">State of the keyboard Keys and Mouse Buttons</param>
    /// <param name="pt">Where the Drop occurred on the Control</param>
    /// <param name="pdwEffect">Result of the Drop - unreliable in case of Move</param>
    /// <returns>S_OK</returns>
        public int DragDrop(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)



        {

            // Debug.WriteLine("In DragDrop: Effect = " & pdwEffect & " Keystate = " & grfKeyState)
            int res;
            if (!(m_LastTarget == null))
            {
                res = m_LastTarget.DragDrop(pDataObj, grfKeyState, pt, ref pdwEffect);
                // version 21 change 
                if (res != 0 && res != 1)
                {
                    Debug.WriteLine("Error in dropping on DropTarget. res = " + res.ToString("X"));
                } // No error on drop
                  // The documented norm for Optimized Moves is pdwEffect=None, so leave it
                ShDragDrop?.Invoke(m_LastNode, (int)grfKeyState, (int)pdwEffect);
            }
            ResetPrevTarget();
            m_DataObj = IntPtr.Zero;
            m_Original_Effect = 0;

            if (m_DropHelper is not null)
            {
                m_DropHelper.Drop(pDataObj, ref pt, pdwEffect);
            }
            return S_OK;
        }
        #endregion

        #region    IDisposable processing
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    DisposeDropWrapper();
                }
                if (m_TreeView is not null)
                {
                    RevokeDragDrop(m_TreeView.Handle);
                    m_TreeView = null;
                }
            }
            m_disposed = true;
        }

        // This code added by Visual Basic to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code.  Put cleanup code in Dispose(ByVal disposing As Boolean) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
    /// Revokes the View from getting drop messages and releases the IDropTarget
    /// </summary>
        private void DisposeDropWrapper()
        {
            ReleaseCom();
            if (m_DropHelper is not null)
            {
                Marshal.ReleaseComObject(m_DropHelper);
            }
        }

        /// <summary>
    /// Release the IDropTarget and free's the allocated memory
    /// </summary>
        private void ReleaseCom()
        {
            if (m_LastTarget is not null)
            {
                Marshal.ReleaseComObject(m_LastTarget);

                m_LastTarget = null;
                // m_dropHelperPtr = IntPtr.Zero
            }
        }

        #endregion

    }
}