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

    /// <summary>The ClvDropWrapper class deals with the mechanics of receiving a
    /// Drag/Drop operation for a ListView.  In effect, it implements the IDropTarget interface
    /// for a ListView.  It is designed to handle a ListView which <b>must</b> have CShItems 
    /// in the Tags of the ListViewItems contained in the control. The ListView <b>must also</b> have the 
    /// CShellItem of the <i>single</i> Parent Folder stored in its' .Tag Property
    /// </summary>
    /// <remarks>
    /// <para>The class receives the DragEnter, DragLeave, DragOver, and DragDrop events for
    /// the associated ListView and performs the Drag specific processing. Unlike CtvDropWrapper,
    /// this class DOES NOT raise ShDragxxx events for the ListView, and DOES do the
    /// GUI related processing.</para>
    /// <para>The interesting part of this class is that it makes no decisions about the drag
    /// nor does any Drop related processing itself. Instead, it acts as a broker between
    /// the Drag/Drop operation and the IDropTarget interface of the underlying 
    /// Shell Folder.  This allows the Shell Folder, which may be a Shell Extension, to
    /// perform whatever action it needs to in order to effect the Drag/Drop.
    /// The benefit of this approach is that Drag/Drop targets need not be part of the
    /// File System.</para>
    /// ListViews, unlike TreeViews, may be displaying non-folder items and may have
    /// substantial areas within the control that are empty of any ListViewItems. This requires
    /// different behavior rules for a ListView receiving a Drag.
    /// <list type="bullet">
    /// <item><description>Upon DragEnter, the "parent" directory of the entire set of Listview items is determined. The "parent" is
    ///                    determined from the CShItems contained in the ListViewItem.Tags or if there are no ListViewItems, from
    ///                    the CShellItem contained in the ListView's .Tag. </description></item>
    /// <item><description>The default pdweffect for this control/drag is obtained from the IDropTarget of that "parent"</description></item>
    /// <item><description>If there is no common "parent" and if the ListView's Tag does not contain a CShellItem,
    ///                    the default pdweffect for this control/drag is set to "None"</description></item>
    /// <item><description>Upon DragOver: 
    /// <br />If over a ListViewItem representing a Directory,Obtain IDropTarget from the Directory, and invoke its DragEnter,DragOver to set pwdeffects.
    /// <br />Also set BackGroundColor of that ListView Item to SelectedColor
    /// <br />If not over a ListViewItem representing a directory AndAlso a common "parent" can be determined from the ListViewItems or the ListView's
    /// .Tag Property then use IDropTarget of parent to accept DragOver and adjust pdweffect.
    /// <br />If not over a ListViewItem representing a directory AndAlso if no common "parent" can be determined, then
    /// set pwdeffects to "None"</description></item>
    /// <item><description>Upon DragLeave, all local vars are reset to "New" state</description></item>
    /// <item><description>Upon DragDrop, the IDropTarget.DragDrop of the Folder represented by the current ListViewItem
    /// is called and all local vars are reset to "New" state.</description></item>
    /// </list>
    /// </remarks>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ClvDropWrapper : WindowsApiLib.Shell.IDropTarget, IDisposable
    {

        #region    Private Fields

        private ListView m_ListView;                  // The control
                                                      // Private m_WasNotFullRowSelect As Boolean       'True only if need to switch back to FullRowSelect=False
        private IntPtr m_DataObj;                     // The COM interface to IDragData - saved in DragEnter
        private DragDropEffects m_Original_Effect;    // Save it
        private DragDropEffects m_Default_Effect;     // Default for this control, for this Drag
        private WindowsApiLib.Shell.IDropTarget m_LastTarget;    // IDropTarget of most recent Folder dragged over
        private ListViewItem m_LastItem;              // Most recent ListViewItem dragged over
        private Color m_OriginalColor;                // Original BackColor of ListViewItem Dragged Over
        private IDropTargetHelper m_DropHelper;       // IDropTargetHelper interface for this control
        private CShellItem? m_ParentItem;                 // CShellItem of Parent dir, if any, otherwise Nothing
        private WindowsApiLib.Shell.IDropTarget m_ParentTarget;  // IDropTarget of the Parent dir of all Items in control, or Nothing
        private bool m_disposed = false;           // To detect redundant Dispose calls

        #endregion

        #region    Constructor
        /// <summary>
    /// Initializes a new instance of the Class.
    /// </summary>
    /// <param name="ctl">The ListView for whom this Class will Handle Drag/Drops.</param>
    /// <remarks>Registers itself to Handle Drag/Drops for the ListView.</remarks>
        public ClvDropWrapper(ListView ctl)
        {
            m_ListView = ctl;
            // Correct type of Control, register IDropTarget for it
            if (m_ListView.IsHandleCreated)
            {
                if (Application.OleRequired() == ApartmentState.STA)
                {
                    int res;
                    res = RegisterDragDrop(m_ListView.Handle, this);
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
                throw new ArgumentException(m_ListView.Name + " Handle has not been created");
            }
            m_ListView.HandleCreated += View_HandleCreated;
            m_ListView.HandleDestroyed += View_HandleDestroyed;

            IntPtr dropHelperPtr;     // historical place to accept input from nxt call
            ShellHelper.GetIDropTargetHelper(out dropHelperPtr, out m_DropHelper);
            if (dropHelperPtr != IntPtr.Zero)
                Marshal.Release(dropHelperPtr);
        }
        #endregion

        #region    Handle Changes

        private void View_HandleCreated(object? sender, EventArgs e)
        {
            int res;
            res = RegisterDragDrop(m_ListView.Handle, this);
            if (!(res == 0) | res == -2147221247)
            {
                Marshal.ThrowExceptionForHR(res);
                // Throw New Exception("Failed to Register DragDrop for ClvDropWrapper on " & ctl.Name)
            }
        }

        private void View_HandleDestroyed(object? sender, EventArgs e)
        {
            if (m_ListView is not null)
            {
                RevokeDragDrop(m_ListView.Handle);
                m_ListView.HandleCreated -= View_HandleCreated;
                m_ListView.HandleDestroyed -= View_HandleDestroyed;
                m_ListView = null;
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
            if (m_LastItem is not null)
            {
                m_LastItem.BackColor = Color.Empty;
                m_LastItem.ForeColor = Color.Empty;
                m_LastItem = null;
            }
        }
        #endregion

        #region        DragEnter
        /// <summary>
        /// For internal use only
        /// DragEnter is called by the Shell as a drag enters the listview. It determines the default (parent)
        /// DropTarget and default (parent) pdwEffect for use in those areas of the ListView that do not
        /// contain eligible DropTargets.
        /// </summary>
        /// <param name="pDataObj">IDataObject of the Folder of the Item being dragged, containing references to
        /// the item(s) being Dragged.</param>
        /// <param name="grfKeyState">State of the keyboard keys at this moment</param>
        /// <param name="pt">Location, in screen coordinates, of the mouse.</param>
        /// <param name="pdwEffect">Permitted Drop actions as set by the DragSource.</param>
        /// <returns>Always returns S_OK (0)</returns>
        /// <remarks></remarks>
        public int DragEnter(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {

            ResetPrevTarget();                       // should be redundant, but, just in case ...
            m_Original_Effect = pdwEffect;
            m_DataObj = pDataObj;
            m_ParentItem = null;

            // Determine Parentage of this set of items
            if (m_ListView.Tag is CShellItem csiTag && csiTag.IsFolder)
            {
                m_ParentItem = csiTag;
            }
            else if (!m_ListView.VirtualMode)
            {
                foreach (ListViewItem lvi in m_ListView.Items)
                {
                    if (lvi is null) continue;
                    CShellItem csi = lvi.Tag as CShellItem;
                    if (csi is not null)
                    {
                        if (m_ParentItem is null)
                        {
                            if (csi.Parent is not null)
                            {
                                m_ParentItem = csi.Parent;
                            }
                            else            // only Desktop lacks a parent
                            {
                                m_ParentItem = ShellController.DesktopCSI;
                            }
                        }
                        else if (!ReferenceEquals(m_ParentItem, csi.Parent))    // multiple parents 
                        {
                            m_ParentItem = null;
                            break;
                        }
                    }
                }
            }

            if (m_ParentItem is not null)
            {
                m_ParentTarget = m_ParentItem.GetDropTargetOf(m_ListView);
                if (m_ParentTarget is not null)
                {
                    m_ParentTarget.DragEnter(pDataObj, grfKeyState, pt, ref pdwEffect);
                    m_Default_Effect = pdwEffect;
                }
                else
                {
                    m_Default_Effect = DragDropEffects.None;
                }
            }
            else
            {
                m_Default_Effect = DragDropEffects.None;
            }

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragEnter(m_ListView.Handle, pDataObj, ref pt, pdwEffect);
            }
            return S_OK;
        }
        #endregion

        #region    DragOver
        /// <summary>
    /// For internal use only
    /// Entered when a Drag moves over the surface of the associated Control.<br />
    /// If the Mouse is over a ListViewItem representing a Folder, sets that item as the 
    /// most recent potential Drop Target and Changes the colors of that ListViewItem.
    /// </summary>
    /// <param name="grfKeyState">The state of certain Keyboard keys</param>
    /// <param name="pt">The location of the Mouse in the Control.</param>
    /// <param name="pdwEffect">The actions permitted by the DragSource</param>
    /// <returns>S_OK</returns>
        public int DragOver(MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {
            bool reset = false;

            var point = m_ListView.PointToClient(new Point(pt.x, pt.y));

            var hitTest = m_ListView.HitTest(point);
            if (hitTest.Item is not null)
            {
                if (!ReferenceEquals(hitTest.Item, m_LastItem))
                {
                    ResetPrevTarget();
                    CShellItem item = hitTest.Item.Tag as CShellItem;
                    if (item is not null && item.IsFolder)
                    {
                        m_LastItem = hitTest.Item;
                        m_OriginalColor = m_LastItem.BackColor;
                        m_LastItem.BackColor = SystemColors.Highlight;
                        m_LastItem.ForeColor = SystemColors.HighlightText;
                        m_LastTarget = item.GetDropTargetOf(m_ListView);
                        reset = true;
                    }
                }
            }
            else
            {
                ResetPrevTarget();
            }

            if (m_LastTarget is not null)
            {
                if (reset)
                {
                    m_LastTarget.DragEnter(m_DataObj, grfKeyState, pt, ref pdwEffect);
                }
                else
                {
                    m_LastTarget.DragOver(grfKeyState, pt, ref pdwEffect);
                }
            }
            else if (m_ParentTarget is not null)
            {
                m_ParentTarget.DragOver(grfKeyState, pt, ref pdwEffect);
            }
            else
            {
                pdwEffect = m_Default_Effect;
            }

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragOver(ref pt, pdwEffect);
            }

            return S_OK;
        }

        #endregion

        #region    DragLeave
        /// <summary>
    /// For internal use only
    /// DragLeave is raised by the Shell when the Drag is cancelled or otherwise leaves the underlying
    /// listview.  The handler does whatever cleanup is needed to prepare for another DragEnter.
    /// </summary>
    /// <returns>Always returns S_OK</returns>
    /// <remarks></remarks>
        public int DragLeave()
        {
            // Debug.WriteLine("In DragLeave")
            m_Original_Effect = DragDropEffects.None;
            ResetPrevTarget();
            m_DataObj = IntPtr.Zero;
            if (m_ParentTarget is not null)
            {
                m_ParentTarget.DragLeave();
                Marshal.ReleaseComObject(m_ParentTarget);
                m_ParentTarget = null;
            }
            m_ParentItem = null;

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
        /// Normally simply passes the Drop on to the Dropped on Folder which is determined here
        /// in conjuction with DragEnter.
        /// </summary>
        /// <param name="pDataObj">Pointer to the IDataObject</param>
        /// <param name="grfKeyState">State of the keyboard Keys and Mouse Buttons</param>
        /// <param name="pt">Where the Drop occurred on the Control</param>
        /// <param name="pdwEffect">Result of the Drop - unreliable in case of Move</param>
        /// <returns>S_OK</returns>
        public int DragDrop(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {
            try
            {
                // Debug.WriteLine("In DragDrop: Effect = " & pdwEffect & " Keystate = " & grfKeyState)
                int res;
                if (!(m_LastTarget == null))
                {
                    res = m_LastTarget.DragDrop(pDataObj, grfKeyState, pt, ref pdwEffect);
                    if (m_ParentTarget is not null)
                        m_ParentTarget.DragLeave();            // Not dropping on it, so leave it
                                                               // version 21 change 
                    if (res != 0 && res != 1)
                    {
                        Debug.WriteLine("Error in dropping on DropTarget. res = " + res.ToString("X"));
                    } // No error on drop
                }
                // The documented norm for Optimized Moves is pdwEffect=None, so leave it
                // RaiseEvent ShDragDrop(m_LastItem, grfKeyState, pdwEffect)
                else if (m_ParentTarget is not null)
                {
                    res = m_ParentTarget.DragDrop(pDataObj, grfKeyState, pt, ref pdwEffect);
                    if (res != 0 && res != 1)
                    {
                        Debug.WriteLine("Error in dropping on DropTarget. res = " + res.ToString("X"));
                    } // No error on drop
                }
                m_Original_Effect = DragDropEffects.None;
                ResetPrevTarget();
                m_DataObj = IntPtr.Zero;

                m_ParentItem = null;

                if (m_DropHelper is not null)
                {
                    m_DropHelper.Drop(pDataObj, ref pt, pdwEffect);
                }
                return S_OK;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return E_FAIL;
            }
            finally
            {
                if (m_ParentTarget is not null)
                {
                    Marshal.ReleaseComObject(m_ParentTarget);
                    m_ParentTarget = null;
                }
            }
            
        }
        #endregion

        #region IDisposable processing
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    DisposeDropWrapper();
                }
                if (m_ListView is not null && m_ListView.Handle != IntPtr.Zero)
                {
                    RevokeDragDrop(m_ListView.Handle);
                    m_ListView = null;
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
            ResetPrevTarget();
            if (m_ParentTarget is not null)
            {
                Marshal.ReleaseComObject(m_ParentTarget);
                m_ParentTarget = null;
            }
            m_ParentItem = null;
            if (m_DropHelper is not null)
            {
                Marshal.ReleaseComObject(m_DropHelper);
                m_DropHelper = null;
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
            }
        }

        #endregion

    }
}