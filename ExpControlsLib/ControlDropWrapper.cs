using WindowsApiLib.Shell;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{

    /// <summary>
    /// A Class to serve as a broker between a Control that is associated with a single Folder and
    /// a DragTo/DropOn operation initiated From any other DragSource that the Shell supports. The calling application may change 
    /// the associated Folder of an instance of ControlDropWrapper as needed - it is not needed or desireable to create new a instance
    /// as the associated Folder changes.<br />
    /// This should not be used for TreeView or ListView Controls which require special handling.
    /// </summary>
    /// <remarks>Originally the targeted use of this Class was for DragSources that provide email items,however it will also work
    ///          for Drags from WinExplorer or any application that provides an IDataObject with appropriately formatted data.</remarks>
    ///          
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class ControlDropWrapper : WindowsApiLib.Shell.IDropTarget, IDisposable
    {

        #region    Private Fields
        private string m_FullPath;                // The FullPath to the Dir to Drop On
        private Control m_Owner;                  // The Control whose IDropTarget this instance is serving as
        private CShellItem m_DirCSI;                 // The CShellItem of the Dir associated with this Control
        private WindowsApiLib.Shell.IDropTarget m_Target;    // The DropTarget of the Dir associated with this Control
        private IDropTargetHelper m_DropHelper;   // A Generic Helper for showing images 
        private int m_Original_Effect;        // Preserved across Dragxxx Events
        private DragDropEffects m_Default_Effect; // Default for this control, for this Drag


        #endregion

        #region    Constructor
        public ControlDropWrapper(Control Ctl, string FullPath)
        {
            m_DirCSI = CShellItemFactory.Create(FullPath);
            if (m_DirCSI is null)
            {
                throw new ArgumentException(FullPath + " Is not Valid or Reachable");
            }
            if (!m_DirCSI.IsDropTarget)
            {
                throw new ArgumentException(FullPath + " Is not a Valid DropTarget");
            }

            if (Ctl is TreeView || Ctl is ListView)
            {
                throw new ArgumentException("Not for use on " + Ctl.GetType().Name);
            }

            // Ensure FolderList and FileList is initialized 
            if (!m_DirCSI.FoldersInitialized)
                m_DirCSI.GetDirectories();
            if (!m_DirCSI.FilesInitialized)
                m_DirCSI.GetFiles();

            m_FullPath = FullPath;
            m_Owner = Ctl;
            if (m_Owner.IsHandleCreated)
            {
                if (Application.OleRequired() == System.Threading.ApartmentState.STA)
                {
                    Ctl_HandleCreated(this, new EventArgs());
                }
                else
                {
                    throw new ArgumentException("This App or Thread MustBe STA");
                }
            }
            m_Owner.HandleCreated += Ctl_HandleCreated;
            m_Owner.HandleDestroyed += Ctl_HandleDestroyed;

            IntPtr dropHelperPtr;     // historical place to accept input from nxt call
            ShellHelper.GetIDropTargetHelper(out dropHelperPtr, out m_DropHelper);
        }
        #endregion

        #region    Handle Changes

        private void Ctl_HandleCreated(object? sender, EventArgs e)
        {
            int res;
            res = RegisterDragDrop(m_Owner.Handle, this);
            if (res != 0 && res != -2147221247)
            {
                Marshal.ThrowExceptionForHR(res);
            }
            m_Target = m_DirCSI.GetDropTargetOf(m_Owner);
        }

        private void Ctl_HandleDestroyed(object? sender, EventArgs e)
        {
            if (m_Owner is not null && m_Owner.IsHandleCreated)
            {
                RevokeDragDrop(m_Owner.Handle);
                // UPDATE: Added remove handler calls to allow a control
                // to be shown multiple times in a modal dialog
                m_Owner.HandleCreated -= Ctl_HandleCreated;
                m_Owner.HandleDestroyed -= Ctl_HandleDestroyed;
                m_Owner = null;
            }
        }
        #endregion

        #region    Public Properties
        /// <summary>
    /// Contains the Full Path of the Folder associated with this Control
    /// </summary>
    /// <returns>The Full Path of the Folder associated with this Control</returns>
    /// <remarks>Setting this Property to another valid Path will release all references to the previous Folder (if any) and
    ///          associate this instance with the new Folder.</remarks>
        public string FullPath
        {
            get
            {
                return m_FullPath;
            }
            set
            {
                if (value.Equals(m_FullPath, StringComparison.CurrentCultureIgnoreCase))
                    return;
                m_DirCSI.ClearItems(true, false);
                Marshal.ReleaseComObject(m_Target);
                m_DirCSI = CShellItemFactory.Create(value);
                m_Target = m_DirCSI.GetDropTargetOf(m_Owner);
                m_FullPath = value;

                // Ensure FolderList and FileList is initialized 
                if (!m_DirCSI.FoldersInitialized)
                    m_DirCSI.GetDirectories();
                if (!m_DirCSI.FilesInitialized)
                    m_DirCSI.GetFiles();
            }
        }

        #endregion

        #region    IDropTarget Implementation
        /// <summary>
    /// For internal use only<br />
    /// DragEnter is called by the Shell as a drag enters the Control. Its main function is to
    /// save off the IDataObject of the Drag for use in DragOver processing, since DragOver does
    /// not receive the IDataObject as a parameter.
    /// </summary>
    /// <param name="pDataObj">IDataObject of the Folder of the Item being dragged, containing references to
    /// the item(s) being Dragged.</param>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pt">Location, in screen coordinates, of the mouse.</param>
    /// <param name="pdwEffect">Permitted Drop actions as set by the DragSource.</param>
    /// <returns>S_OK</returns>
        public int DragEnter(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {
            m_Original_Effect = (int)pdwEffect;
            if (m_Target is not null)
            {
                m_Target.DragEnter(pDataObj, grfKeyState, pt, ref pdwEffect);
                m_Default_Effect = pdwEffect;
            }
            else
            {
                m_Default_Effect = DragDropEffects.None;
            }
            if (m_DropHelper is not null)
            {
                m_DropHelper.DragEnter(m_Owner.Handle, pDataObj, ref pt, pdwEffect);
            }
            return S_OK;
        }

        /// <summary>
    /// For internal use only
    /// Entered when a Drag moves over the surface of the associated Control.<br />
    /// </summary>
    /// <param name="grfKeyState">State of the Control Keys and Mouse Buttons</param>
    /// <param name="pt">Location, in screen coordinates, of the mouse.</param>
    /// <param name="pdwEffect">Permitted Drop actions as set by the DragSource and modified by
    /// candidate DropTargets.</param>
    /// <returns>S_OK</returns>
        public int DragOver(MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {
            m_Target.DragOver(grfKeyState, pt, ref pdwEffect);
            if (m_DropHelper is not null)
            {
                m_DropHelper.DragOver(ref pt, pdwEffect);
            }
            return S_OK;
        }

        /// <summary>
    /// DragLeave is raised by the Shell when the Drag is cancelled or otherwise leaves the underlying
    /// Control.  The handler does whatever cleanup is needed to prepare for another DragEnter.
    /// </summary>
    /// <returns>S_OK</returns>
        public int DragLeave()
        {
            m_Original_Effect = (int)DragDropEffects.None;
            if (m_Target is not null)
            {
                m_Target.DragLeave();
            }

            if (m_DropHelper is not null)
            {
                m_DropHelper.DragLeave();
            }
            return S_OK;
        }

        /// <summary>
    /// For internal use only<br />
    /// Entered when a DragDrop has occurred on the associated Control.
    /// </summary>
    /// <param name="pDataObj">Pointer to the IDataObject</param>
    /// <param name="grfKeyState">State of the keyboard Keys and Mouse Buttons</param>
    /// <param name="pt">Where the Drop occurred on the Control</param>
    /// <param name="pdwEffect">Result of the Drop - unreliable in case of Move</param>
    /// <returns>S_OK</returns>
        public int DragDrop(IntPtr pDataObj, MK grfKeyState, POINT pt, ref DragDropEffects pdwEffect)
        {
            m_Target.DragDrop(pDataObj, grfKeyState, pt, ref pdwEffect);
            if (m_DropHelper is not null)
            {
                m_DropHelper.Drop(pDataObj, ref pt, pdwEffect);
            }
            return S_OK;
        }
        #endregion

        #region  IDisposable Support 

        private bool disposedValue = false;        // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // NoneTODO: free other state (managed objects).
                }
                if (m_Target is not null)
                {
                    Marshal.ReleaseComObject(m_Target);
                }
                if (m_DropHelper is not null)
                {
                    Marshal.ReleaseComObject(m_DropHelper);
                }
                if (m_Owner is not null && m_Owner.Handle != IntPtr.Zero)
                {
                    RevokeDragDrop(m_Owner.Handle);
                    m_Owner.HandleCreated -= Ctl_HandleCreated;
                    m_Owner.HandleDestroyed -= Ctl_HandleDestroyed;
                    m_Owner = null;
                }
            }
            disposedValue = true;
        }

        // This code added by Visual Basic to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code.  Put cleanup code in Dispose(ByVal disposing As Boolean) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }
}