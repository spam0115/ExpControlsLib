using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;
using static WindowsApiLib.Shell.ShellHelper;
using static WindowsApiLib.SystemImageListManager;

namespace ExpControlsLib
{
    /// <summary>
    /// ExpTree is a UserControl encapsulating a TreeView which will display all or part of the Windows Shell
    /// Namespace.  The Shell Namespace is a superset of the Windows file system. It is the Tree commonly shown
    /// by Windows Explorer, in Classic View. That is, it is a Tree rooted in the Desktop.
    /// ExpTree supports Drag and Drop and standard Windows Context Menus.
    /// </summary>
    /// <remarks>ExpTree raises one major Event, ExpTreeNodeSelected. That event is raised whenever the 
    /// Selected TreeNode changes because of User Action (i.e. -- clicking on the node)</remarks>
    [DefaultProperty("StartUpDirectory")]
    [DefaultEvent("StartUpDirectoryChanged")]
    [SupportedOSPlatform("windows")]
    public partial class ExpTree
    {
        #region Private fields
        /// <summary>
        /// The root <see cref="TreeNode"/> of the TreeView. Represents the top-level Shell item
        /// from which the entire tree is built.
        /// </summary>
        private TreeNode? _Root;

        /// <summary>
        /// Flag used to suppress the raising of <see cref="ExpTreeNodeSelected"/> during
        /// tree refresh and programmatic navigation operations.
        /// </summary>
        private bool EnableEventPost = true;

        /// <summary>
        /// A case-insensitive set of paths or GUIDs representing Shell items that should be
        /// excluded from display in the tree.
        /// </summary>
        private HashSet<string> _excludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Stack holding the backward navigation history of visited <see cref="CShellItem"/> folders.
        /// </summary>
        private Stack<CShellItem> _backHistory = new Stack<CShellItem>();

        /// <summary>
        /// Stack holding the forward navigation history of visited <see cref="CShellItem"/> folders,
        /// populated when the user navigates back.
        /// </summary>
        private Stack<CShellItem> _forwardHistory = new Stack<CShellItem>();

        /// <summary>
        /// Indicates whether a history navigation (back or forward) is currently in progress,
        /// used to prevent the navigation from being recorded again as a new history entry.
        /// </summary>
        private bool _isNavigatingHistory = false;

        /// <summary>
        /// The most recently selected <see cref="CShellItem"/>, used as the source entry
        /// when recording navigation history.
        /// </summary>
        private CShellItem? _lastSelectedCSI = null;

        /// <summary>
        /// Cancellation token source for the currently active root-load operation.
        /// Cancelled and replaced whenever a new root load is initiated.
        /// </summary>
        private CancellationTokenSource? _rootLoadCts;

        /// <summary>
        /// Shared STA thread runner used to marshal Shell COM operations onto a dedicated
        /// Single-Threaded Apartment thread pool, keeping the UI thread responsive.
        /// </summary>
        private StaThreadRunner? _staRunner = null;

        /// <summary>
        /// A <see cref="CShellItem"/> that is waiting to be expanded once the root load completes.
        /// Set when <see cref="ExpandANode(CShellItem, bool)"/> is called before the tree is ready.
        /// </summary>
        private CShellItem? _pendingExpansionItem;

        /// <summary>
        /// Indicates whether the pending expansion item should also be selected after the root
        /// load completes and the deferred expansion is performed.
        /// </summary>
        private bool _pendingSelectExpandedNode;

        /// <summary>
        /// Backing field for the <see cref="DropHandler"/> property.
        /// Holds the current <see cref="CtvDropWrapper"/> instance managing Shell drop operations.
        /// </summary>
        private CtvDropWrapper? _DropHandler;

        /// <summary>
        /// Gets or sets the <see cref="CtvDropWrapper"/> that handles Shell drag-and-drop operations
        /// onto the TreeView. Setting this property automatically wires or unwires the associated
        /// drag event handlers.
        /// </summary>
        private CtvDropWrapper? DropHandler
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return _DropHandler;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (_DropHandler != null)
                {
                    _DropHandler.ShDragEnter -= DragWrapper_ShDragEnter;
                    _DropHandler.ShDragLeave -= DragWrapper_ShDragLeave;
                    _DropHandler.ShDragOver -= DragWrapper_ShDragOver;
                    _DropHandler.ShDragDrop -= DragWrapper_ShDragDrop;
                }

                _DropHandler = value;
                if (_DropHandler != null)
                {
                    _DropHandler.ShDragEnter += DragWrapper_ShDragEnter;
                    _DropHandler.ShDragLeave += DragWrapper_ShDragLeave;
                    _DropHandler.ShDragOver += DragWrapper_ShDragOver;
                    _DropHandler.ShDragDrop += DragWrapper_ShDragDrop;
                }
            }
        }

        /// <summary>
        /// Manages Shell drag-source operations initiated from within the TreeView,
        /// allowing items to be dragged out to other Shell targets.
        /// </summary>
        private CDragWrapper? DragHandler;

        /// <summary>
        /// Backing field for <see cref="ShowHiddenFolders"/>. When <c>true</c>, folders
        /// with the Hidden attribute are included in the tree display.
        /// </summary>
        private bool m_showHiddenFolders = true;

        /// <summary>
        /// The Windows Shell context menu helper used to display and process the native
        /// right-click context menu for selected TreeNode items.
        /// </summary>
        private readonly ContextMenu m_WindowsContextMenu = new ContextMenu();

        private bool IsInDesignMode => (DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime);

        /// <summary>Windows message identifier for setting the selection range in an edit control.</summary>
        private const int EM_SETSEL = 0xB1;

        /// <summary>Base value for TreeView control messages (WM_USER + 0x1100).</summary>
        private const int TVM_FIRST = 0x1100;

        /// <summary>
        /// TreeView message that retrieves the handle of the edit control used for in-place label editing.
        /// Equals <c>TVM_FIRST + 15</c>.
        /// </summary>
        private const int TVM_GETEDITCONTROL = TVM_FIRST + 15;

        #endregion

        #region Event delegates
        /// <summary>
        /// StartUpDirectoryChanged is raised when the root of the TreeView is changed via StartUpDirectory
        /// Property. 
        /// </summary>
        /// <param name="newVal">One of the StartDir Enum values that represent the possible Start Up Directories.</param>
        /// <remarks>Seldom listened for since, in typical use, the Method which set the StartUpDirectory value
        /// is the only Method which is interested. It is also true that a by-product of setting the StartUpDirectory 
        /// value is the Selection of the new root node.  That change in SelectedNode will cause an ExpTreeNodeSelected
        /// Event to be raised.
        /// Is this event even useful?  It seems kinda useless.
        /// </remarks>
        public event StartUpDirectoryChangedEventHandler StartUpDirectoryChanged;

        /// <summary>
        /// Delegate for the <see cref="StartUpDirectoryChanged"/> event.
        /// </summary>
        /// <param name="newVal">The new <see cref="StartDir"/> value that was applied.</param>
        public delegate void StartUpDirectoryChangedEventHandler(StartDir newVal);

        /// <summary>
        /// ExpTreeNodeSelected is raised when a Node in the TreeView is Selected.
        /// </summary>
        /// <param name="SelPath">The Path of the CShellItem represented by the TreeNode, and stored in the
        /// TreeNode's Tag.</param>
        /// <param name="Item">The CShellItem represented by the TreeNode, and stored in the
        /// TreeNode's Tag.</param>
        /// <remarks></remarks>
        [Category("Action")]
        [Description("Fires when an item is selected")]
        public event ExpTreeNodeSelectedEventHandler ExpTreeNodeSelected;

        /// <summary>
        /// Delegate for the <see cref="ExpTreeNodeSelected"/> event.
        /// </summary>
        /// <param name="SelPath">The file system path or display name of the selected Shell item.</param>
        /// <param name="Item">The <see cref="CShellItem"/> associated with the selected TreeNode.</param>
        public delegate void ExpTreeNodeSelectedEventHandler(string SelPath, CShellItem Item);

        #endregion region

        #region Public Properties

        /// <summary>
        /// Backing field for <see cref="AllowDrop"/>. Stores the last value assigned to the property
        /// so that the drop handler can be re-created when the TreeView handle is (re-)created.
        /// </summary>
        private bool m_AllowDrop = false;

        /// <summary>
        /// Turns this ExpTree Control's ability to accept Drops on or Off.<br />
        /// True - Enables the ExpTree Control to accept Drops.<br />
        /// False - Disables the ExpTree Control acceptance of  Drops.
        /// </summary>
        /// <returns>True or False</returns>
        /// <remarks>Works by assigning or  removing an instance of CtvDropWrapper to the Local variable DropHandler.</remarks>
        public override bool AllowDrop
        {
            get
            {
                return DropHandler is not null;
            }
            set
            {
                m_AllowDrop = value;
                if (value)
                {
                    if (_TreeView?.IsHandleCreated ?? false)
                    {
                        if (DropHandler is null)      // otherwise, already running
                        {
                            DropHandler = new CtvDropWrapper(_TreeView);
                        }
                    }
                }
                else if (DropHandler is not null)
                {
                    DropHandler.Dispose();
                    DropHandler = null;
                }
            }
        }

        /// <summary>
        /// Backing field for <see cref="AllowFolderRename"/>. When <c>true</c>, the TreeView's
        /// <c>LabelEdit</c> is enabled and the Shell rename verb is permitted via the context menu.
        /// </summary>
        private bool m_allowFolderRename;

        /// <summary>
        /// Allow renaming of folders using LabelEdit
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        [Category("Behavior")]
        [Description("Allow renaming of folders using LabelEdit")]
        public bool AllowFolderRename
        {
            get
            {
                return m_allowFolderRename;
            }
            set
            {
                m_allowFolderRename = value;
                _TreeView?.LabelEdit = value;
            }
        }

        /// <summary>
        /// Gets or sets the foreground color of the underlying TreeView control.
        /// </summary>
        /// <returns>The current foreground <see cref="Color"/> of the TreeView.</returns>
        public override Color ForeColor
        {
            get
            {
                return _TreeView?.ForeColor ?? base.ForeColor;
            }
            set
            {
                if (value != _TreeView?.ForeColor)
                {
                    _TreeView?.ForeColor = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the background color of the underlying TreeView control.
        /// </summary>
        /// <returns>The current background <see cref="Color"/> of the TreeView.</returns>
        public override Color BackColor
        {
            get
            {
                return _TreeView?.BackColor ?? base.BackColor;
            }
            set
            {
                if (value != _TreeView?.BackColor)
                {
                    _TreeView?.BackColor = value;
                }
            }
        }

        /// <summary>
        /// RootItem is a Run-Time only Property. Setting this Item via an External call results in
        /// re-setting the entire tree to be rooted in the input CShellItem.
        /// The new CShellItem must be a valid CShellItem of some kind of Folder (File Folder or System Folder).
        /// Attempts to set it using a non-Folder CShellItem are ignored.
        /// </summary>
        [Browsable(false)]
        public CShellItem? Root
        {
            get
            {
                if (_Root is null || _Root.Tag is null)
                {
                    return ShellController.DesktopCSI;
                }
                else
                {
                    return (CShellItem)_Root.Tag;
                }
            }
            set
            {
                if (value is null) return;

                if (value.IsFolder)
                {
                    _ = SetRootItemAsync(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected tree node in the underlying TreeView control.
        /// </summary>
        [Browsable(false)]
        public TreeNode? SelectedNode
        {
            get => _TreeView?.SelectedNode;
            set => _TreeView?.SelectedNode = value;
        }

        /// <summary>
        /// Run-time only Property which returns the CShellItem underlying the SelectedNode of the TreeView.
        /// </summary>
        /// <returns>The underlying CShellItem of the TreeView.SelectedNode. If none Selected, returns Nothing.</returns>
        [Browsable(false)]
        public CShellItem? SelectedItem
        {
            get
            {
                if (!(_TreeView?.SelectedNode == null))
                {
                    return (CShellItem?)_TreeView?.SelectedNode?.Tag;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the collection of tree nodes that are assigned to the tree view control.
        /// </summary>
        [Browsable(false)]
        public TreeNodeCollection? Nodes => _TreeView?.Nodes;

        /// <summary>
        /// ShowHiddenFolders sets or gets a Boolean indicating whether or not to Display Folders with the Hidden Attribute.
        /// </summary>
        /// <value></value>
        /// <returns>True if ExpTree is Displaying Hidden Folders, False if not.</returns>
        /// <remarks>Hidden Folders may be Displayed or not Displayed at run-time.</remarks>
        [Browsable(true)]
        [Category("Options")]
        [Description("Show Hidden Directories.")]
        [DefaultValue(true)]
        public bool ShowHiddenFolders
        {
            get
            {
                return m_showHiddenFolders;
            }
            set
            {
                if (m_showHiddenFolders ^ value)
                {
                    m_showHiddenFolders = value;
                    if (_Root is not null)
                        RefreshTree();
                }
            }
        }

        /// <summary>
        /// Gets or sets a collection of items (by their full path or GUID) to exclude from the tree display.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public HashSet<string> ExcludedItems
        {
            get => _excludedItems;
            set => _excludedItems = value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Exposes the normal TreeView ShowRootLines property.
        /// </summary>
        /// <value></value>
        /// <returns>The state of the underlying TreeView property.</returns>
        /// <remarks></remarks>
        [Category("Options")]
        [Description("Allow Collapse of Root Item.")]
        [Browsable(true)]
        public bool ShowRootLines
        {
            get
            {
                return _TreeView?.ShowRootLines ?? false;
            }
            set
            {
                if (!(value == _TreeView?.ShowRootLines))
                {
                    _TreeView?.ShowRootLines = value;
                    _TreeView?.Refresh();
                }
            }
        }

        /// <summary>
        /// Backing field for <see cref="StartUpDirectory"/>. Stores the currently active
        /// <see cref="StartDir"/> value.
        /// </summary>
        private StartDir m_StartUpDirectory = StartDir.None;

        /// <summary>
        /// Sets the initial Root directory of ExpTree.
        /// </summary>
        /// <value>Must be one of the StartDir Enum values.</value>
        /// <returns>Current StartDir value.</returns>
        /// <remarks></remarks>
        [Category("Options")]
        [Description("Sets the Initial Directory of the Tree")]
        [Browsable(true)]
        [DefaultValue(StartDir.None)]
        public StartDir StartUpDirectory
        {
            get
            {
                return m_StartUpDirectory;
            }
            set
            {
                if (Array.IndexOf(Enum.GetValues(value.GetType()), value) >= 0)
                {
                    m_StartUpDirectory = value;
                    OnStartUpDirectoryChanged(value);
                    StartUpDirectoryChanged?.Invoke(value);
                }
                else
                {
                    throw new ApplicationException("Invalid Initial StartUpDirectory");
                }
            }
        }

        #endregion

        #region Constructor/Destructor

        /// <summary>
        /// Initializes a new instance of <see cref="ExpTree"/>, sets up the TreeView image list,
        /// wires Shell item update notifications, and configures the node-expansion hover timer.
        /// </summary>
        public ExpTree() : base()
        {
            ConstructorBase(null);
        }

        public ExpTree(string? rootPath) : base()
        {
            ConstructorBase(rootPath);
        }

        private void ConstructorBase(string? rootPath)
        {
            InitializeComponent();

            expandNodeTimer = new System.Windows.Forms.Timer();

            if (IsInDesignMode)
                return;

            _staRunner = new StaThreadRunner(5, "ExpTreeStaRunner");

            if (rootPath is not null)
            {
                var csi = ShellController.Instance.HierachyManager.FindOrAdd(rootPath.Trim());
                if (csi is null) throw new ArgumentException("ExpTree: root path could not be found.");
                m_StartUpDirectory = StartDir.None;
                Root = csi;
            }

            SetTreeViewImageList(_TreeView, false);

            ShellController.Instance.ShellUpdater.UpdateEvent += OnItemUpdate;
            expandNodeTimer.Tick += ExpandNodeTimer_Tick;
        }

        /// <summary>
        /// Windows Message Handler for receiving Messages associated with a System Menu. 
        /// This is what causes Cascading menus to Display
        /// </summary>
        /// <param name="m">A Windows Message</param>
        /// <remarks>Only Handles Messages relating to Windows Context Menus</remarks>
        protected override void WndProc(ref Message m)
        {
            int hr;
            if (m.Msg == (int)WM.INITMENUPOPUP || m.Msg == (int)WM.MEASUREITEM || m.Msg == (int)WM.DRAWITEM)
            {
                if (m_WindowsContextMenu.cntxMenuExtended is not null)
                {
                    hr = m_WindowsContextMenu.cntxMenuExtended.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                    if (hr == 0)
                    {
                        return;
                    }
                }
            }
            else if (m.Msg == (int)WM.MENUCHAR)
            {
                if (m_WindowsContextMenu.cntxMenuCascading != null)
                {
                    IntPtr plResult = Marshal.AllocHGlobal(IntPtr.Size);
                    try
                    {
                        hr = m_WindowsContextMenu.cntxMenuCascading.HandleMenuMsg2(m.Msg, m.WParam, m.LParam, plResult);
                        if (hr == 0)
                        {
                            m.Result = Marshal.ReadIntPtr(plResult);
                            return;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(plResult);
                    }
                }
            }
            base.WndProc(ref m);
        }

        #endregion

        /// <summary>
        /// P/Invoke declaration for the UxTheme <c>SetWindowTheme</c> function, which applies
        /// a visual style to a window, giving the TreeView the standard Explorer appearance.
        /// </summary>
        /// <param name="hWnd">Handle to the window to restyle.</param>
        /// <param name="pszSubAppName">The application name to use for theming (e.g. <c>"explorer"</c>).</param>
        /// <param name="pszSubIdList">Optional semicolon-delimited list of sub-IDs, or <c>null</c>.</param>
        /// <returns>An HRESULT indicating success or failure.</returns>
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        #region Public Methods

        /// <summary>
        /// Expands TreeNodes from the tree root through the input Path. All intermediate nodes between the
        /// Tree Root and the input Path are Expanded. If the Optional Property SelectExpandedNode is True (the Default),
        /// the Expanded Node will be Selected, Raising a ExpNodeSelected Event. If False, the current Selected Node is unchanged
        /// and no Event is Raised.
        /// </summary>
        /// <param name="newPath">The FileSystem path of the Node node to be Expanded.</param>
        /// <param name="SelectExpandedNode">If True(the Default) then Select the Expanded Node.<br />
        ///                                  If False, Do Not Select the Expanded Node.</param>
        /// <returns>True if Successful, False otherwise.</returns>
        /// <remarks>The preferred method is to use:
        /// <pre lang="vbnet">Public Function ExpandANode(ByVal newItem : CShellItem) As Boolean</pre> 
        /// If the item defined by the input Path does not exist, False is returned.<br />
        /// Calling with SelectExpandedNode = False is useful when it is not desired to Raise an
        /// ExpTreeNodeSelected Event as a result of ExpandaNode.</remarks>
        public bool ExpandANode(string newPath, bool SelectExpandedNode = true)
        {
            bool ExpandANodeRet = default;
            ExpandANodeRet = false;
            CShellItem newItem;
            try
            {
                newItem = ShellController.Instance.HierachyManager.FindOrAdd(newPath);
                if (newItem is null)
                    return ExpandANodeRet;
                if (!newItem.IsFolder)
                    return ExpandANodeRet;
            }
            catch
            {
                return ExpandANodeRet;
            }
            return ExpandANode(newItem, SelectExpandedNode);
        }

        /// <summary>
        /// Asynchronously expands TreeNodes from the tree root through the node identified by
        /// <paramref name="newPath"/>. This is the async counterpart of
        /// <see cref="ExpandANode(string, bool)"/>.
        /// </summary>
        /// <param name="newPath">The file-system path of the node to expand.</param>
        /// <param name="SelectExpandedNode">
        /// If <c>true</c> (the default), the expanded node is selected and an
        /// <see cref="ExpTreeNodeSelected"/> event is raised. If <c>false</c>, the current
        /// selection is unchanged and no event is raised.
        /// </param>
        /// <returns>
        /// A <see cref="Task{Boolean}"/> that resolves to <c>true</c> if the expansion succeeded,
        /// or <c>false</c> if the path could not be resolved or the item is not a folder.
        /// </returns>
        public async Task<bool> ExpandANodeAsync(string newPath, bool SelectExpandedNode = true)
        {
            CShellItem newItem;
            try
            {
                newItem = ShellController.Instance.HierachyManager.FindOrAdd(newPath);
                if (newItem is null)
                    return false;
                if (!newItem.IsFolder)
                    return false;
            }
            catch
            {
                return false;
            }
            return await ExpandANodeAsync(newItem, SelectExpandedNode);
        }

        /// <summary>
        /// Expands TreeNodes from the tree root through the input CShellItem. All intermediate nodes between the
        /// Tree Root and the input CShellItem are Expanded. If the Optional Property SelectExpandedNode is True (the Default),
        /// the Expanded Node will be Selected, Raising a ExpNodeSelected Event. If False, the current Selected Node is unchanged
        /// and no Event is Raised.
        /// </summary>
        /// <param name="newItem">The CShellItem representing the Shell Namespace object whose TreeNode is to
        /// be expanded.</param>
        /// <param name="SelectExpandedNode">If True(the Default) then Select the Expanded Node.<br />
        ///                                  If False, Do Not Select the Expanded Node.</param>
        /// <returns>True if Successful, False otherwise.</returns>
        /// <remarks>This is the preferred method of ExpandANode.<br />
        /// Calling with SelectExpandedNode = False is useful when it is not desired to Raise an
        /// ExpTreeNodeSelected Event as a result of ExpandaNode.</remarks>
        public bool ExpandANode(CShellItem newItem, bool SelectExpandedNode = true)
        {
            bool ExpandANodeRet = default;
            ExpandANodeRet = false;
            var baseNode = _Root;
            if (baseNode == null)
            {
                if (_rootLoadCts != null && !_rootLoadCts.IsCancellationRequested)
                {
                    _pendingExpansionItem = newItem;
                    _pendingSelectExpandedNode = SelectExpandedNode;
                    return true;
                }
                return false;
            }

            if (baseNode.Tag == null)
            {
                throw new InvalidOperationException("baseNode.Tag cannot be null.");
            }

            CShellItem baseItem = (CShellItem)baseNode.Tag;
            IntPtr basePidl = baseItem.PIDL;
            int lim = CPidl.SegmentCount(newItem.PIDL) - CPidl.SegmentCount(basePidl);

            try
            {
                _TreeView.BeginUpdate();

                baseNode.Expand();

                while (lim > 0)
                {
                    bool continueDo = false;
                    foreach (TreeNode testNode in baseNode.Nodes)
                    {
                        if (CPidl.IsAncestorOf((CShellItem)testNode.Tag, newItem, false))
                        {
                            baseNode = testNode;
                            baseNode.Expand();
                            lim -= 1;
                            continueDo = true;
                            break;
                        }
                    }

                    if (continueDo)
                    {
                        continue;
                    }
                    goto XIT;     // on falling thru For, we can't find it, so get out
                }
                // after falling thru here, we have found & expanded the node
                _TreeView.HideSelection = false;
                Select();
                if (SelectExpandedNode)
                    _TreeView.SelectedNode = baseNode;
                ExpandANodeRet = true;
            XIT:
                baseNode.EnsureVisible();
                return ExpandANodeRet;
            }
            finally
            {
                _TreeView.EndUpdate();
            }
        }

        /// <summary>
        /// Asynchronously expands TreeNodes from the tree root through the node represented by
        /// <paramref name="newItem"/>, populating lazy-loaded (dummy) nodes on demand.
        /// This is the preferred async counterpart of <see cref="ExpandANode(CShellItem, bool)"/>.
        /// </summary>
        /// <param name="newItem">
        /// The <see cref="CShellItem"/> whose corresponding TreeNode should be expanded and
        /// optionally selected.
        /// </param>
        /// <param name="SelectExpandedNode">
        /// If <c>true</c> (the default), the target node is selected after expansion and an
        /// <see cref="ExpTreeNodeSelected"/> event is raised. If <c>false</c>, the current
        /// selection is unchanged.
        /// </param>
        /// <returns>
        /// A <see cref="Task{Boolean}"/> that resolves to <c>true</c> if the node was found
        /// and expanded successfully, or <c>false</c> otherwise.
        /// </returns>
        public async Task<bool> ExpandANodeAsync(CShellItem newItem, bool SelectExpandedNode = true)
        {
            var baseNode = _Root;
            if (baseNode == null)
            {
                // If a load is in progress, store as pending
                if (_rootLoadCts != null && !_rootLoadCts.IsCancellationRequested)
                {
                    _pendingExpansionItem = newItem;
                    _pendingSelectExpandedNode = SelectExpandedNode;
                    return true;
                }
                return false;
            }

            // Get the pidl value from baseNode.Tag by casting to CShellItem
            if (baseNode.Tag == null)
            {
                throw new InvalidOperationException("baseNode.Tag cannot be null.");
            }

            CShellItem baseItem = (CShellItem)baseNode.Tag;
            IntPtr basePidl = baseItem.PIDL;
            int lim = CPidl.SegmentCount(newItem.PIDL) - CPidl.SegmentCount(basePidl);

            try
            {
                // do the drill down -- Node to expand must be included in tree
                if (baseNode.Nodes.Count == 1 && baseNode.Nodes[0].Text == " : ")
                {
                    await PopulateNodeAsync(baseNode);
                }
                _TreeView.BeginUpdate();
                baseNode.Expand();
                _TreeView.EndUpdate();

                while (lim > 0)
                {
                    bool continueDo = false;
                    foreach (TreeNode testNode in baseNode.Nodes)
                    {
                        if (CPidl.IsAncestorOf((CShellItem)testNode.Tag, newItem, false))
                        {
                            baseNode = testNode;
                            if (baseNode.Nodes.Count == 1 && baseNode.Nodes[0].Text == " : ")
                            {
                                await PopulateNodeAsync(baseNode);
                            }
                            _TreeView.BeginUpdate();
                            baseNode.Expand();
                            _TreeView.EndUpdate();
                            lim -= 1;
                            continueDo = true;
                            break;
                        }
                    }

                    if (continueDo)
                    {
                        continue;
                    }
                    baseNode.EnsureVisible();
                    return false;
                }

                _TreeView.BeginUpdate();
                _TreeView.HideSelection = false;
                Select();
                if (SelectExpandedNode)
                    _TreeView.SelectedNode = baseNode;

                baseNode.EnsureVisible();
                _TreeView.EndUpdate();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in ExpandANodeAsync: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Collapses all nodes in the TreeView.
        /// </summary>
        /// <param name="collapse">
        /// When <c>true</c> (the default), all nodes are collapsed.
        /// Passing <c>false</c> is a no-op (reserved for future expansion).
        /// </param>
        public void ExpCollapseAll(bool collapse = true)
        {
            if (collapse == true)
            {
                _TreeView.CollapseAll();
            }
        }

        #region Navigation

        /// <summary>
        /// Navigates back to the previous folder in the history.
        /// </summary>
        public async void GoBack()
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(_lastSelectedCSI);
                var prev = _backHistory.Pop();
                _isNavigatingHistory = true;
                try
                {
                    await ExpandANodeAsync(prev, true);
                }
                finally
                {
                    _isNavigatingHistory = false;
                }
            }
        }

        /// <summary>
        /// Navigates forward to the next folder in the history.
        /// </summary>
        public async void GoForward()
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(_lastSelectedCSI);
                var next = _forwardHistory.Pop();
                _isNavigatingHistory = true;
                try
                {
                    await ExpandANodeAsync(next, true);
                }
                finally
                {
                    _isNavigatingHistory = false;
                }
            }
        }

        /// <summary>
        /// Navigates to the parent folder of the currently loaded folder.
        /// </summary>
        public async void GoUp()
        {
            if (_lastSelectedCSI?.Parent != null)
            {
                await ExpandANodeAsync(_lastSelectedCSI.Parent, true);
            }
        }

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate back to.
        /// </summary>
        public bool CanGoBack => _backHistory.Count > 0;

        /// <summary>
        /// Gets a value indicating whether there is a folder to navigate forward to.
        /// </summary>
        public bool CanGoForward => _forwardHistory.Count > 0;

        /// <summary>
        /// Gets a value indicating whether the current folder has a parent folder to navigate to.
        /// </summary>
        public bool CanGoUp => _lastSelectedCSI?.Parent != null;

        #endregion

        #endregion

        #region Dynamic Update Handler

        /// <summary>
        /// Handles Shell item update notifications from <see cref="CShellItemUpdater"/>.
        /// Responds to folder creation, deletion, rename, media change, and general update events
        /// by adding, removing, or refreshing the corresponding TreeNode in the tree.
        /// Only folder-related events are processed; non-folder events are silently ignored.
        /// </summary>
        /// <param name="sender">The source of the update event.</param>
        /// <param name="e">
        /// A <see cref="ShellItemUpdateEventArgs"/> containing the affected <see cref="CShellItem"/>
        /// and the type of update that occurred.
        /// </param>
        private void OnItemUpdate(object sender, ShellItemUpdateEventArgs e)
        {
            // Debug.WriteLine("Enter ExpTree OnItemUpdate -- " & e.Item.DisplayName & " - " & e.UpdateType.ToString)
            if (e.Item is not null && e.Item.IsFolder)  // no interest in non-folder events (or UpdateDir)
            {
                try
                {
                    CShellItem parent = e.Item.Parent;
                    TreeNode? pNode = default(TreeNode);
                    if (GetTreeNode(parent, ref pNode))
                    {
                        switch (e.UpdateType)
                        {
                            case CShItemUpdateType.Created:  // A new Dir has been created under Parent/pNode
                                {
                                    if (IsExcluded(e.Item)) break;
                                    var Node = MakeNode(e.Item);
                                    InsertNode(Node, pNode);
                                    break;
                                }
                            case CShItemUpdateType.Deleted:  // An old Dir has been deleted from Parent/pNode
                                {
                                    bool exitSelect = false;
                                    foreach (TreeNode Node in pNode.Nodes)
                                    {
                                        if (Node.Tag is not null && (ReferenceEquals(Node.Tag, e.Item) || CPidl.ResolvesToSamePathOrName(((CShellItem)Node.Tag).PIDL, e.Item.PIDL)))
                                        {
                                            pNode.Nodes.Remove(Node);
                                            exitSelect = true;
                                            break;
                                        }
                                    }

                                    if (exitSelect)
                                    {
                                        break;
                                    }

                                    break;
                                }
                            // In the Renamed case, pnode is the Parent CShellItem Before the rename,
                            // get the current Parent CShellItem from the renamed CShellItem(e.Item)
                            case CShItemUpdateType.Renamed:  // A directory has been renamed under Parent/pNode
                                {
                                    var curPNode = default(TreeNode);
                                    bool exitSelect1 = false;
                                    foreach (TreeNode Node in pNode.Nodes)
                                    {
                                        if (Node.Tag is not null && (ReferenceEquals(Node.Tag, e.Item) || CPidl.ResolvesToSamePathOrName(((CShellItem)Node.Tag).PIDL, e.Item.PIDL)))
                                        {
                                            bool wasSelected = ReferenceEquals(_TreeView.SelectedNode, Node);
                                            Node.Text = e.Item.DisplayName;
                                            pNode.Nodes.Remove(Node);

                                            if (IsExcluded(e.Item))
                                            {
                                                exitSelect1 = true;
                                                break;
                                            }

                                            if (GetTreeNode(e.Item.Parent, ref curPNode))
                                            {
                                                InsertNode(Node, curPNode);
                                                if (wasSelected)
                                                {
                                                    _TreeView.SelectedNode = Node;
                                                    Node.EnsureVisible();
                                                }
                                            }
                                            exitSelect1 = true;
                                            break;
                                        }
                                    }

                                    if (exitSelect1)
                                    {
                                        break;
                                    }

                                    break;
                                }
                            case CShItemUpdateType.MediaChange:
                                {
                                    bool exitSelect2 = false;
                                    for (int indx = 0, loopTo = pNode.Nodes.Count - 1; indx <= loopTo; indx++)
                                    {
                                        var node = pNode.Nodes[indx];
                                        if (node.Tag is not null && (ReferenceEquals(node.Tag, e.Item) || CPidl.ResolvesToSamePathOrName(((CShellItem)node.Tag).PIDL, e.Item.PIDL)))
                                        {
                                            CShellItem item = (CShellItem)node.Tag;
                                            bool wasExpanded = node.IsExpanded;
                                            if (wasExpanded)
                                            {
                                                node.ImageIndex = item.IconIndexOpen;
                                            }
                                            else
                                            {
                                                node.ImageIndex = item.IconIndexNormal;
                                            }
                                            node.Collapse(false);
                                            node.Nodes.Clear();
                                            if (ShouldHaveDummy(item))
                                            {
                                                node.Nodes.Add(new TreeNode(" : "));
                                            }
                                            if (wasExpanded)
                                                node.Expand();
                                            _TreeView.Invalidate();
                                            if (ReferenceEquals(node, _TreeView.SelectedNode))
                                            {
                                                if (e.Item.FullPath.StartsWith(":"))
                                                {
                                                    ExpTreeNodeSelected?.Invoke(e.Item.DisplayName, e.Item);
                                                }
                                                else
                                                {
                                                    ExpTreeNodeSelected?.Invoke(e.Item.FullPath, e.Item);
                                                }
                                            }
                                            exitSelect2 = true;
                                            break;
                                        }
                                    }

                                    if (exitSelect2)
                                    {
                                        break;
                                    }

                                    break;
                                }
                            case CShItemUpdateType.Updated:
                                {
                                    var UNode = default(TreeNode);
                                    if (GetTreeNode(e.Item, ref UNode))
                                    {
                                        if (UNode.Nodes.Count == 0)
                                        {
                                            if (ShouldHaveDummy(e.Item))
                                            {
                                                UNode.Nodes.Add(new TreeNode(" : "));
                                            }
                                            UNode.Collapse(false);
                                        }
                                        else if (UNode.Nodes.Count == 1 && UNode.Nodes[0].Text.Equals(" : "))
                                        {
                                            if (!ShouldHaveDummy(e.Item))
                                            {
                                                UNode.Nodes.Clear();
                                            }
                                        }
                                    }

                                    break;
                                }

                            default:
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ExpTree Update Error -- " + ex.ToString());
                }
            }
        }

        #endregion

        #region Initial Dir Set Handler

        /// <summary>
        /// Responds to a change in <see cref="StartUpDirectory"/> by clearing the tree (when
        /// <see cref="StartDir.None"/> is selected) or asynchronously reloading it rooted at
        /// the newly specified special folder.
        /// </summary>
        /// <param name="newVal">The new <see cref="StartDir"/> value that was applied.</param>
        private void OnStartUpDirectoryChanged(StartDir newVal)
        {
            if (newVal == StartDir.None)
            {
                _TreeView.BeginUpdate();
                ClearTree();
                _TreeView.EndUpdate();
                return;
            }

            _ = SetRootItemAsync(null, newVal);
        }

        /// <summary>
        /// Populates the root TreeNode's <see cref="TreeNode.Nodes"/> collection from the
        /// sorted array of first-level child <see cref="CShellItem"/> folders.
        /// Hidden items are filtered according to <see cref="ShowHiddenFolders"/>, and
        /// excluded items are filtered via <see cref="IsExcluded"/>.
        /// </summary>
        /// <param name="L1">
        /// The array of first-level child <see cref="CShellItem"/> folders to add as root children.
        /// </param>
        private void BuildTree(CShellItem[] L1)
        {
            Array.Sort(L1); //todo: move further up the call stack
            foreach (var CSI in L1)
            {
                if (!(CSI.IsHidden & !m_showHiddenFolders) && !IsExcluded(CSI))
                {
                    _Root.Nodes.Add(MakeNode(CSI));
                }
            }
        }

        /// <summary>
        /// Creates a TreeNode whose .Text is the DisplayName of the CShellItem.<br />
        /// Sets the IconIndexes for that TreeNode from the CShellItem.<br />
        /// Sets the Tag of the TreeNode to the CShellItem<br />
        /// If the CShellItem (a Folder) has or may have sub-Folders (see Remarks), adds a Dummy node to
        ///   the TreeNode's .Nodes collection. This is always done if the input CShellItem represents a Removable device. Checking
        ///   further on such devices may cause unacceptable delays.
        /// Returns the complete TreeNode.
        /// </summary>
        /// <param name="item">The CShellItem to make a TreeNode to represent.</param>
        /// <returns>A TreeNode set up to represent the CShellItem.</returns>
        /// <remarks>
        /// This routine will not be called if the CShellItem (a Folder) is Hidden and ExpTree's ShowHidden Property is False.<br />
        /// If the Folder is Hidden and ShowHidden is True, then this routine will be called.<br />
        /// If the Folder is Hidden and it only contains Hidden Folders (files are not considered here), then, 
        /// the HasSubFolders attribute may be returned False even though Hidden Folders exist. In that case, we 
        /// must make an extra check to ensure that the TreeNode is expandable.<br />
        /// 
        /// There are additional complication with HasSubFolders. 
        /// <ul>
        /// <li>
        /// On XP and earlier systems, HasSubFolders was always
        /// returned True if the Folder was on a Remote system. On Vista and above, the OS would check and return an 
        /// accurate value. This extra check can take a long time on Remote systems - approximately the same amount of time as checking
        /// item.GetDirectories.Count. Versions 2.12 and above of ExpTreeLib have a modified HasSubFolders Property which will always
        /// return True if the Folder is on a Remote system, restoring XP behavior.</li>
        /// <li>
        /// On XP and earlier systems, compressed files (.zip, .cab, etc) were treated as files. On Vista and above, they are treated
        /// as Folders. ExpTreeLib continues to treat such files as files. The HasSubfolder attribute will report a Folder which
        /// contains only compressed files as True. In MakeNode, I simply accept the Vista and above interpretation, setting a dummy
        /// node in such a Folder. An attempt to expand such a TreeNode will just turn off the expansion marker.
        /// </li>
        /// </ul>
        /// </remarks>
        private TreeNode MakeNode(CShellItem item)
        {
            var newNode = new TreeNode(item.DisplayName)
            {
                Tag = item,
                ImageIndex = GetIconIndex(item, false),
                SelectedImageIndex = GetIconIndex(item, true)
            };

            if (ShouldHaveDummy(item))
            {
                newNode.Nodes.Add(new TreeNode(" : "));
            }
            return newNode;
        }

        /// <summary>
        /// Determines if a TreeNode for the given CShellItem should have a dummy node (expansion arrow).
        /// </summary>
        /// <param name="item">The CShellItem to check.</param>
        /// <returns>True if it should have a dummy node, false otherwise.</returns>
        private bool ShouldHaveDummy(CShellItem item)
        {
            if (!item.IsFolder) return false;

            // Fast-path: rely on shell hints (warmed up in background) to maintain responsiveness.
            return item.HasSubFolders || (item.IsHidden && item.DirCount > 0);
        }

        /// <summary>
        /// Removes all nodes from the TreeView and resets <see cref="_Root"/> to <c>null</c>,
        /// effectively clearing the entire tree display.
        /// </summary>
        private void ClearTree()
        {
            _TreeView.Nodes.Clear();
            _Root = null;
        }

        #endregion

        #region Event Handling


        /// <summary>
        /// Handles the TreeView's <c>HandleCreated</c> event. Initialises the drag source wrapper,
        /// optionally creates the drop target wrapper, and applies the Explorer visual theme.
        /// </summary>
        /// <param name="sender">The TreeView whose handle was created.</param>
        /// <param name="e">Event data (unused).</param>
        private void Tv1_HandleCreated(object sender, EventArgs e)
        {
            if (IsInDesignMode)
                return;

            //should this stuff be moved to load?
            DragHandler = new ExpControlsLib.CDragWrapper(_TreeView); 
            if (m_AllowDrop)
                DropHandler = new CtvDropWrapper(_TreeView);
            SetWindowTheme(_TreeView.Handle, "explorer", null);
        }

        /// <summary>
        /// Handles the TreeView's <c>HandleDestroyed</c> event.
        /// Reserved for any cleanup that must occur when the underlying window handle is destroyed.
        /// </summary>
        /// <param name="sender">The TreeView whose handle was destroyed.</param>
        /// <param name="e">Event data (unused).</param>
        private void Tv1_HandleDestroyed(object sender, EventArgs e)
        {
        }


        /// <summary>
        /// Handles the TreeView <c>BeforeExpand</c> event. If the node being expanded contains
        /// only a dummy placeholder node, its real children are loaded asynchronously before
        /// the expansion is allowed to proceed. Also updates the node's icon to the open-folder state.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">
        /// A <see cref="TreeViewCancelEventArgs"/> that can be used to cancel the expansion.
        /// </param>
        private async void Tv1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text.Equals(" : "))
            {
                var oldCursor = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    await PopulateNodeAsync(e.Node);
                }
                finally
                {
                    Cursor = oldCursor;
                }
            }
            e.Node.ImageIndex = ((CShellItem)e.Node.Tag).IconIndexOpen;
        }

        private void Tv1_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (e?.Node?.Tag is null) return;

            CShellItem csi = (CShellItem)e.Node.Tag;
            if (csi is null) return;

            Debug.WriteLine("Tv1_BeforeSelect: item selected: " + csi.Name + " " + sender?.ToString());
        }

        /// <summary>
        /// Handles the TreeView <c>AfterSelect</c> event. Records the selection in the navigation
        /// history, optionally triggers a directory refresh for newly created folders, and raises
        /// the <see cref="ExpTreeNodeSelected"/> event when <see cref="EnableEventPost"/> is <c>true</c>.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="TreeViewEventArgs"/> containing the newly selected node.</param>
        private void Tv1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e?.Node?.Tag is null) return;

            CShellItem csi = (CShellItem)e.Node.Tag;
            if (csi is null) return;

            // record history
            if (!_isNavigatingHistory && _lastSelectedCSI != null && !ReferenceEquals(_lastSelectedCSI, csi))
            {
                _backHistory.Push(_lastSelectedCSI);
                _forwardHistory.Clear();
            }
            _lastSelectedCSI = csi;

            // **********Added by Lukai-2021.12.02, If a folder is created by code "My.Computer.FileSystem.CreateDirectory(folderPath)", then this folder can't be shown automatically, I need to refresh it in here manually
            //if (System.IO.Directory.Exists(csi.FullPath))
            //{
            //    try
            //    {
            //        if (e.Node.GetNodeCount(false) != System.IO.Directory.GetDirectories(csi.FullPath).Length)
            //        {
            //            ShellController.Instance.ShellUpdater.DoUpdateDir(csi, false, true);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Debug.WriteLine("Error reading folder: " + ex.Message);
            //    }
            //}

            if (EnableEventPost) // turned off during RefreshTree
            {
                if (csi.FullPath.StartsWith(":"))
                {
                    ExpTreeNodeSelected?.Invoke(csi.DisplayName, csi);
                }
                else
                {
                    ExpTreeNodeSelected?.Invoke(csi.FullPath, csi);
                }
            }
        }

        /// <summary>
        /// Handles the <c>MouseUp</c> event on the ExpTree control. On a right-click, determines
        /// the TreeNode under the cursor and, if <see cref="UseWindowsContextMenu"/> is enabled,
        /// displays the native Shell context menu for that node. Rename and other Shell verb
        /// commands are dispatched accordingly. Also forwards the event via <see cref="Control.OnMouseUp"/>.
        /// </summary>
        /// <param name="sender">The control raising the event.</param>
        /// <param name="e">A <see cref="MouseEventArgs"/> describing the mouse action.</param>
        private async void Tv1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                TreeNode tn;
                var pt = PointToClient(MousePosition);
                tn = _TreeView.GetNodeAt(pt);
                if (m_useWindowsContextMenu & !(tn == null))
                {
                    var itms = new CShellItem[1];
                    itms[0] = (CShellItem)tn.Tag;
                    CMInvokeCommandInfoEx cmi = default;
                    if (m_WindowsContextMenu.ShowMenu(Handle, itms, MousePosition, m_allowFolderRename, out cmi, m_minimalContextMenu))
                    {
                        int verbId = cmi.lpVerb.ToInt32();

                        // Check for rename
                        var cmdBytes = new byte[257];
                        m_WindowsContextMenu.cntxMenuBase.GetCommandString(verbId, (int)GCS.VERBA, 0, cmdBytes, 256);

                        string cmdName = SzToString(cmdBytes).ToLower();
                        if (cmdName.Equals("rename"))
                        {
                            _TreeView.LabelEdit = true;
                            tn.BeginEdit();
                        }
                        else
                        {
                            IntPtr parentPidl = ReferenceEquals(itms[0], ShellController.DesktopCSI)
                                ? itms[0].PIDL
                                : itms[0].Parent.PIDL;

                            IntPtr relPidl = CPidl.ILFindLastID(itms[0].PIDL);
                            if (relPidl != IntPtr.Zero)
                            {
                                var capturedRelPidl = CPidl.Copy(relPidl);
                                var capturedParentPidl = parentPidl;
                                string strPath = ReferenceEquals(itms[0], ShellController.DesktopCSI)
                                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                                    : itms[0].Parent.FullPath;

                                await _staRunner.EnqueueWork(_ =>
                                {
                                    IShellFolder desktop = null;
                                    IShellFolder parentFolder = null;
                                    IntPtr iUnknownOut = IntPtr.Zero;
                                    IContextMenu? contextMenu = null;

                                    // Create a hidden dummy window on this thread to act as the owner.
                                    using (Control dummy = new Control())
                                    {
                                        IntPtr dummyHandle = dummy.Handle;

                                        try
                                        {
                                            SHGetDesktopFolder(ref desktop);
                                            if (desktop == null) return -1;

                                            if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                                                parentFolder = desktop;
                                            else
                                            {
                                                IntPtr folderPtr = IntPtr.Zero;
                                                if (desktop.BindToObject(capturedParentPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref folderPtr) != S_OK) return -1;
                                                parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(folderPtr, typeof(IShellFolder));
                                                Marshal.Release(folderPtr);
                                            }

                                            IntPtr rgfReserved = IntPtr.Zero;
                                            var relPidls = new IntPtr[] { capturedRelPidl };
                                            if (parentFolder.GetUIObjectOf(IntPtr.Zero, 1, relPidls, IID_IContextMenu, rgfReserved, out iUnknownOut) != S_OK) return -1;

                                            contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(iUnknownOut, typeof(IContextMenu));

                                            var invokeCmi = new CMInvokeCommandInfoEx
                                            {
                                                cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                                                hwnd = dummyHandle,
                                                nShow = (int)SW.SHOWNORMAL,
                                                fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                                                ptInvoke = pt,
                                                lpVerb = (IntPtr)verbId,
                                                lpVerbW = (IntPtr)verbId
                                            };

                                            return contextMenu.InvokeCommand(invokeCmi);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error in background tree context menu invocation: {ex.Message}");
                                            return -1;
                                        }
                                        finally
                                        {
                                            if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                                            if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                                            if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                                            if (desktop != null) Marshal.ReleaseComObject(desktop);
                                            Marshal.FreeCoTaskMem(capturedRelPidl);
                                        }
                                    }
                                });
                            }
                        }
                        m_WindowsContextMenu.ReleaseMenu();
                    }
                }
            }
            OnMouseUp(e);
        }

        /// <summary>
        /// Forwards the TreeView <c>MouseDown</c> event to the base control's
        /// <see cref="Control.OnMouseDown"/> handler.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="MouseEventArgs"/> describing the mouse action.</param>
        private void Tv1_MouseDown(object sender, MouseEventArgs e)
        {
            OnMouseDown(e);
        }

        /// <summary>
        /// Forwards the TreeView <c>MouseMove</c> event to the base control's
        /// <see cref="Control.OnMouseMove"/> handler.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="MouseEventArgs"/> describing the mouse action.</param>
        private void Tv1_MouseMove(object sender, MouseEventArgs e)
        {
            OnMouseMove(e);
        }

        /// <summary>
        /// Forwards the TreeView <c>MouseEnter</c> event to the base control's
        /// <see cref="Control.OnMouseEnter"/> handler.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">Event data (unused).</param>
        private void Tv1_MouseEnter(object sender, EventArgs e)
        {
            OnMouseEnter(e);
        }

        /// <summary>
        /// Forwards the TreeView <c>MouseLeave</c> event to the base control's
        /// <see cref="Control.OnMouseLeave"/> handler.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">Event data (unused).</param>
        private void Tv1_MouseLeave(object sender, EventArgs e)
        {
            OnMouseLeave(e);
        }

        /// <summary>
        /// Handles the TreeView <c>KeyPress</c> event. Suppresses the default warning beep for
        /// Ctrl+C, Ctrl+V, Ctrl+X, and Enter key presses, then forwards the event via
        /// <see cref="Control.OnKeyPress"/>.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="KeyPressEventArgs"/> containing the pressed character.</param>
        private void Tv1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\u0003' | e.KeyChar == '\u0016' | e.KeyChar == '\u0018' | e.KeyChar == '\r')
            {
                e.Handled = true;
            }
            OnKeyPress(e);
        }

        /// <summary>
        /// Handles the TreeView <c>KeyUp</c> event. Expands the selected node on Enter,
        /// invokes the Shell delete verb on Delete, and dispatches cut/copy/paste Shell verbs
        /// for the corresponding Ctrl key combinations. Forwards the event via
        /// <see cref="Control.OnKeyUp"/>.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="KeyEventArgs"/> describing the key that was released.</param>
        private void Tv1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_TreeView.SelectedNode?.GetNodeCount(false) > 0 && _TreeView.SelectedNode.IsExpanded == false)
                {
                    _TreeView.SelectedNode.Expand();
                }
            }
            if (e.KeyCode == Keys.Delete)
            {
                WinMenuCmd(SelectedItem, "delete");
            }
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case var @case when @case == Keys.X:
                        {
                            WinMenuCmd(SelectedItem, "cut");
                            break;
                        }
                    case var case1 when case1 == Keys.C:
                        {
                            WinMenuCmd(SelectedItem, "copy");
                            break;
                        }
                    case var case2 when case2 == Keys.V:
                        {
                            WinMenuCmd(SelectedItem, "paste");
                            break;
                        }
                }
            }
            OnKeyUp(e);
        }

        /// <summary>
        /// Forwards the TreeView <c>KeyDown</c> event to the base control's
        /// <see cref="Control.OnKeyDown"/> handler.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">A <see cref="KeyEventArgs"/> describing the key that was pressed.</param>
        private void Tv1_KeyDown(object sender, KeyEventArgs e)
        {
            OnKeyDown(e);
        }

        /// <summary>When a form containing this control is Hidden and then re-Shown,
        /// the association to the SystemImageList is lost.  Also lost is the
        /// Expanded state of the various TreeNodes. 
        /// The VisibleChanged Event occurs when the form is re-shown (and other times
        /// as well).  
        /// We re-establish the SystemImageList as the ImageList for the TreeView and
        /// restore at least some of the Expansion.</summary>
        private void Tv1_VisibleChanged(object sender, EventArgs e)
        {
            if (_TreeView.Visible)
            {
                SetTreeViewImageList(_TreeView, false);
                if (_Root is not null)
                {
                    _Root.Expand();
                    if (!(_TreeView.SelectedNode == null))
                    {
                        _TreeView.SelectedNode.Expand();
                    }
                    else
                    {
                        _TreeView.SelectedNode = _Root;
                    }
                }
            }
        }

        /// <summary>Should never occur since if the condition tested for is True,
        /// the user should never be able to Collapse the node. However, it is
        /// theoretically possible for the code to request a collapse of this node
        /// If it occurs, cancel it</summary>
        private void Tv1_BeforeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            if (!_TreeView.ShowRootLines && ReferenceEquals(e.Node, _Root))
            {
                e.Cancel = true;
            }
            else
            {
                e.Node.ImageIndex = ((CShellItem)e.Node.Tag).IconIndexNormal;
            }
        }

        /// <summary>
        /// Handles the TreeView <c>BeforeLabelEdit</c> event. Cancels the edit for dummy nodes,
        /// disk roots, non-renameable items, and the My Documents folder. When editing is allowed,
        /// pre-selects only the filename portion (without extension) in the edit control.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">
        /// A <see cref="NodeLabelEditEventArgs"/> that can be used to cancel the label edit.
        /// </param>
        private void Tv1_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e?.Node?.Tag is null) return;

            if (e.Node.Text == " : ")
            {
                e.CancelEdit = true;
                return;
            }

            CShellItem item = (CShellItem)e.Node.Tag;
            
            if (item.FullPath.StartsWith("::") || item.IsDisk || !m_allowFolderRename
                || (item.FullPath ?? "") == (CShellItemFactory.MyDocuments.FullPath ?? "")
                || !item.CanRename)
            {
                System.Media.SystemSounds.Beep.Play();
                e.CancelEdit = true;
            }
            if (e.CancelEdit == false)
            {
                var editWnd = SendMessage(_TreeView.Handle, TVM_GETEDITCONTROL, (IntPtr)0, IntPtr.Zero);
                int textLen = System.IO.Path.GetFileNameWithoutExtension(item.FullPath).Length;
                SendMessage(editWnd, EM_SETSEL, (IntPtr)0, (IntPtr)textLen);
            }
        }

        /// <summary>
        /// Handles the TreeView <c>AfterLabelEdit</c> event. Trims the new label and calls
        /// <c>IShellFolder.SetNameOf</c> to rename the underlying Shell item. Plays a beep and
        /// cancels the edit if the rename fails or the label is empty/whitespace.
        /// </summary>
        /// <param name="sender">The TreeView raising the event.</param>
        /// <param name="e">
        /// A <see cref="NodeLabelEditEventArgs"/> containing the new label text.
        /// </param>
        private void Tv1_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e?.Node?.Tag is null) return;

            CShellItem item = (CShellItem)e.Node.Tag;
            if (string.IsNullOrWhiteSpace(e.Label)) return;
            var NewName = default(string);

            try
            {
                NewName = e.Label.Trim();
            }
            catch (Exception ex)
            {
                e.CancelEdit = true;
                if (string.IsNullOrEmpty(NewName) == false)
                {
                    System.Media.SystemSounds.Beep.Play();
                }
                Debug.WriteLine("Invalid label edit value.  Ex:" + ex.ToString());
                return;
            }

            var newPidl = IntPtr.Zero;
            if (item.Parent.IShlFolder.SetNameOf((int)_TreeView.Handle, CPidl.ILFindLastID(item.PIDL), NewName, SHGDN.NORMAL, ref newPidl) != S_OK)
            {
                System.Media.SystemSounds.Beep.Play();
                e.CancelEdit = true;
            }
        }

        #endregion Event Handling

        #region CtvDropWrapper Event Handling

        /// <summary>
        /// The TreeNode most recently dragged over or dropped onto. Used to track highlight
        /// state and drive the auto-expand timer.
        /// </summary>
        private TreeNode? dropNode;

        /// <summary>
        /// The client-coordinate position of the most recent drag-over event, used by the
        /// auto-expand timer to scroll the TreeView when dragging near its edges.
        /// </summary>
        private Point NodePoint;

        /// <summary>
        /// Timer that fires after a short hover delay to auto-expand the node currently being
        /// dragged over, improving drag-and-drop usability.
        /// </summary>
        private System.Windows.Forms.Timer? expandNodeTimer;

        /// <summary>
        /// Handles the <see cref="expandNodeTimer"/> <c>Tick</c> event. Expands the node
        /// currently being dragged over (<see cref="dropNode"/>) and scrolls the TreeView
        /// if the drag position is near the top or bottom edge.
        /// </summary>
        /// <param name="sender">The timer raising the event.</param>
        /// <param name="e">Event data (unused).</param>
        private void ExpandNodeTimer_Tick(object sender, EventArgs e)
        {
            expandNodeTimer.Stop();
            if (!(dropNode == null))
            {
                DropHandler.ShDragOver -= DragWrapper_ShDragOver;
                try
                {
                    _TreeView.BeginUpdate();
                    dropNode.Expand();
                    int delta = _TreeView.Height - NodePoint.Y;
                    if (delta < _TreeView.Height / 2d & delta > 0)
                    {
                        if (!(dropNode == null) && dropNode.NextVisibleNode is not null)
                        {
                            dropNode.NextVisibleNode.EnsureVisible();
                        }
                    }
                    if (delta > _TreeView.Height / 2d & delta < _TreeView.Height)
                    {
                        if (!(dropNode == null) && dropNode.PrevVisibleNode is not null)
                        {
                            dropNode.PrevVisibleNode.EnsureVisible();
                        }
                    }
                    dropNode.EnsureVisible();
                }
                finally
                {
                    _TreeView.EndUpdate();
                    DropHandler.ShDragOver += DragWrapper_ShDragOver;
                }
            }
        }

        /// <summary>ShDragEnter does nothing. It is here for debug tracking</summary>
        private void DragWrapper_ShDragEnter(IntPtr pDataObj, int grfKeyState, int pdwEffect)
        {
            // Debug.WriteLine("Enter ExpTree ShDragEnter. PdwEffect = " & pdwEffect)
        }

        /// <summary>Drag has left the control. Cleanup what we have to</summary>
        private void DragWrapper_ShDragLeave()
        {
            expandNodeTimer.Stop();
            if (!(dropNode == null))
            {
                ResetTreeviewNodeColor(dropNode);
            }
            dropNode = null;
        }

        /// <summary>ShDragOver manages the appearance of the TreeView.  Management of
        /// the underlying FolderItem is done in CDragWrapper
        /// Credit to Cory Smith for TreeView colorizing technique and code,
        /// at http://addressof.com/blog/archive/2004/10/01/955.aspx
        /// Node expansion based on expandNodeTimer added by me.
        /// </summary>
        private void DragWrapper_ShDragOver(TreeNode Node, Point pt, int grfKeyState, int pdwEffect)
        {
            if (Node == null)
            {
                expandNodeTimer.Stop();
                if (dropNode is not null)
                {
                    ResetTreeviewNodeColor(dropNode);
                    dropNode = null;
                }
            }
            else  // Drag is Over a node - fix color & DragDropEffects
            {
                if (ReferenceEquals(Node, dropNode))
                {
                    return;    // we've already done it all
                }

                expandNodeTimer.Stop(); // not over previous node anymore
                try
                {
                    _TreeView.BeginUpdate();
                    if (!Node.BackColor.Equals(SystemColors.Highlight))
                    {
                        ResetTreeviewNodeColor(_TreeView.Nodes[0]);
                        Node.BackColor = SystemColors.Highlight;
                        Node.ForeColor = SystemColors.HighlightText;
                    }
                }
                finally
                {
                    _TreeView.EndUpdate();
                }
                dropNode = Node;     // dropNode is the Saved Global version of Node
                NodePoint = pt;      // 7/12/2012 NodePoint is the Saved, Form Global Mouse Location (in client coordinates)
                if (!dropNode.IsExpanded)
                {
                    expandNodeTimer.Interval = 500;
                    expandNodeTimer.Start();
                }
            }
        }

        /// <summary>
        /// Handles the Shell drop event on the TreeView. Stops the auto-expand timer and
        /// resets the highlight color of the previously highlighted drop-target node.
        /// </summary>
        /// <param name="Node">The <see cref="TreeNode"/> onto which the item was dropped.</param>
        /// <param name="grfKeyState">The current state of keyboard modifier keys during the drop.</param>
        /// <param name="pdwEffect">The drag-and-drop effect that was applied.</param>
        private void DragWrapper_ShDragDrop(TreeNode Node, int grfKeyState, int pdwEffect)
        {
            expandNodeTimer.Stop();

            if (!(dropNode == null))
            {
                ResetTreeviewNodeColor(dropNode);
            }
            else
            {
                ResetTreeviewNodeColor(_TreeView.Nodes[0]);
            }
            dropNode = null;
        }

        /// <summary>
        /// Recursively resets the background and foreground colors of a TreeNode and its
        /// expanded children back to the default (empty) color, removing any drag-over highlight.
        /// </summary>
        /// <param name="node">The root <see cref="TreeNode"/> whose colors should be reset.</param>
        private void ResetTreeviewNodeColor(TreeNode node)
        {
            if (!node.BackColor.Equals(Color.Empty))
            {
                node.BackColor = Color.Empty;
                node.ForeColor = Color.Empty;
            }
            if (node.FirstNode is not null && node.IsExpanded)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    if (!child.BackColor.Equals(Color.Empty))
                    {
                        child.BackColor = Color.Empty;
                        child.ForeColor = Color.Empty;
                    }
                    if (child.FirstNode is not null && child.IsExpanded)
                    {
                        ResetTreeviewNodeColor(child);
                    }
                }
            }
        }

        #endregion

        #region Context Menu Methods

        /// <summary>
        /// Backing field for <see cref="UseWindowsContextMenu"/>. When <c>true</c>, a right-click
        /// on a TreeNode displays the native Windows Shell context menu.
        /// </summary>
        private bool m_useWindowsContextMenu = true;

        /// <summary>
        /// Backing field for <see cref="MinimalContextMenu"/>. When <c>true</c>, most third-party
        /// Shell extensions are filtered out of the context menu.
        /// </summary>
        private bool m_minimalContextMenu = false;

        /// <summary>
        /// Sets whether or not the control should use Windows System context menu for TreeNode items.
        /// </summary>
        /// <returns>The current setting (True or False).</returns>
        /// <remarks>Setting this Property to False prevents the display and processing of Windows Context Menus on a Right-Click on a TreeNode.</remarks>
        [Category("Behavior")]
        [Description("Whether the control should use windows context menus.")]
        [DefaultValue(true)]
        public bool UseWindowsContextMenu
        {
            get
            {
                return m_useWindowsContextMenu;
            }
            set
            {
                m_useWindowsContextMenu = value;
            }
        }

        /// <summary>
        /// Gets or sets whether to show a minimal context menu by filtering out most 3rd party extensions.
        /// </summary>
        [Category("Behavior")]
        [Description("If true, filters out most 3rd party shell extensions from the context menu.")]
        [DefaultValue(false)]
        public bool MinimalContextMenu
        {
            get => m_minimalContextMenu;
            set => m_minimalContextMenu = value;
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Asynchronously invokes a Shell context menu verb (delete, cut, copy, or paste) for
        /// the specified <see cref="CShellItem"/> on a background STA thread, keeping the UI
        /// thread unblocked while the Shell operation (which may show its own dialog) completes.
        /// </summary>
        /// <param name="csi">
        /// The <see cref="CShellItem"/> on which the Shell verb should be invoked.
        /// If <c>null</c>, the method returns immediately without doing anything.
        /// </param>
        /// <param name="cmd">
        /// The ANSI Shell verb string to invoke (e.g. <c>"delete"</c>, <c>"cut"</c>,
        /// <c>"copy"</c>, or <c>"paste"</c>).
        /// </param>
        private void WinMenuCmd(CShellItem csi, string cmd)
        {
            if (csi is not null)
            {
                IntPtr parentPidl = ReferenceEquals(csi, ShellController.DesktopCSI)
                    ? csi.PIDL
                    : csi.Parent.PIDL;

                IntPtr relPidl = csi.LastPIDL;
                if (relPidl == IntPtr.Zero) return;

                var capturedRelPidl = CPidl.Copy(relPidl);
                var capturedParentPidl = parentPidl; //not sure if we need to copy this

                // Offload shell interaction to background STA thread to make dialog non-blocking (non-modal to UI thread)
                Task task = _staRunner.EnqueueWork(_ =>
                {
                    IShellFolder desktop = null;
                    IShellFolder parentFolder = null;
                    IntPtr iUnknownOut = IntPtr.Zero;
                    IContextMenu? contextMenu = null;
                    IntPtr lpVerbAnsi = IntPtr.Zero;
                    IntPtr lpVerbUni = IntPtr.Zero;

                    // Create a hidden dummy window on this thread to act as the owner.
                    using (Control dummy = new Control())
                    {
                        IntPtr dummyHandle = dummy.Handle;

                        try
                        {
                            // 1. Get Desktop Folder on THIS thread
                            int hr = SHGetDesktopFolder(ref desktop);
                            if (hr != S_OK || desktop == null)
                            {
                                Debug.WriteLine($"InvokeCommand failed with HRESULT: {hr:X}");
                                return hr;
                            }

                            // 2. Bind to Parent Folder on THIS thread
                            if (CPidl.IsShellNamespaceRoot(capturedParentPidl))
                            {
                                parentFolder = desktop;
                            }
                            else
                            {
                                IntPtr folderPtr = IntPtr.Zero;
                                hr = desktop.BindToObject(capturedParentPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref folderPtr);
                                if (hr != S_OK || folderPtr == IntPtr.Zero) return hr;
                                parentFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(folderPtr, typeof(IShellFolder));
                                Marshal.Release(folderPtr);
                            }

                            // 3. Get UI Object (IContextMenu) on THIS thread
                            IntPtr rgfReserved = IntPtr.Zero;
                            var relPidls = new IntPtr[] { capturedRelPidl };
                            hr = parentFolder.GetUIObjectOf(IntPtr.Zero, 1, relPidls, IID_IContextMenu, rgfReserved, out iUnknownOut);

                            if (hr != S_OK || iUnknownOut == IntPtr.Zero)
                            {
                                Debug.WriteLine($"InvokeCommand failed with HRESULT: {hr:X}");
                                return hr;
                            }
                            contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(iUnknownOut, typeof(IContextMenu));

                            // 4. Invoke Command
                            lpVerbAnsi = Marshal.StringToHGlobalAnsi(cmd);
                            lpVerbUni = Marshal.StringToHGlobalUni(cmd);

                            var cmi = new CMInvokeCommandInfoEx
                            {
                                cbSize = Marshal.SizeOf(typeof(CMInvokeCommandInfoEx)),
                                hwnd = dummyHandle,
                                nShow = (int)SW.SHOWNORMAL,
                                fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE | CMIC.ASYNCOK),
                                ptInvoke = new Point(0, 0),
                                lpVerb = lpVerbAnsi,
                                lpVerbW = lpVerbUni
                            };

                            hr = contextMenu.InvokeCommand(cmi);
                            if (hr != S_OK) Debug.WriteLine($"InvokeCommand failed with HRESULT: {hr:X}");
                            return hr;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in background WinMenuCmd: {ex.Message}");
                            return -1;
                        }
                        finally
                        {
                            if (lpVerbAnsi != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbAnsi);
                            if (lpVerbUni != IntPtr.Zero) Marshal.FreeHGlobal(lpVerbUni);
                            if (iUnknownOut != IntPtr.Zero) Marshal.Release(iUnknownOut);
                            if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
                            if (parentFolder != null && parentFolder != desktop) Marshal.ReleaseComObject(parentFolder);
                            if (desktop != null) Marshal.ReleaseComObject(desktop);
                            Marshal.FreeCoTaskMem(capturedRelPidl);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Asynchronously loads and displays the tree rooted at the specified <see cref="CShellItem"/>
        /// or <see cref="StartDir"/> value. Any in-progress load is cancelled before the new one begins.
        /// Child icon and sub-folder data are pre-warmed on a background STA thread to keep the UI responsive.
        /// </summary>
        /// <param name="item">
        /// The <see cref="CShellItem"/> to use as the new tree root, or <c>null</c> if <paramref name="dir"/>
        /// should be used to resolve the root instead.
        /// </param>
        /// <param name="dir">
        /// A <see cref="StartDir"/> value used to resolve the root when <paramref name="item"/> is <c>null</c>.
        /// Defaults to <see cref="StartDir.None"/>.
        /// </param>
        private async Task SetRootItemAsync(CShellItem? item, StartDir dir = StartDir.None)
        {
            _rootLoadCts?.Cancel();
            _rootLoadCts = new CancellationTokenSource();
            var token = _rootLoadCts.Token;

            try
            {
                var result = await _staRunner.EnqueueWork(t =>
                {
                    CShellItem? value = item;
                    if (value == null && dir != StartDir.None)
                    {
                        value = CShellItemFactory.Create((CSIDL)dir);
                    }

                    if (value == null || !value.IsFolder) return null;

                    var flags = SHCONTF.FOLDERS;
                    if (m_showHiddenFolders) flags |= SHCONTF.INCLUDEHIDDEN;
                    var target = ShellController.Instance.LoadFolderContents(value, flags);
                    if (target != null) value = target;

                    var children = value.Directories;
                    // Warming up
                    foreach (var child in children)
                    {
                        if (t.IsCancellationRequested) return null;
                        _ = child.HasSubFolders; // Populates cache
                        _ = child.IconIndexNormal;
                        _ = child.IconIndexOpen;
                    }

                    return new
                    {
                        Children = children,
                        RootItem = value,
                        DisplayName = value.DisplayName,
                        IconIndex = GetIconIndex(value, false)
                    };
                }, token);

                if (token.IsCancellationRequested || result == null) return;

                _TreeView.BeginUpdate();
                try
                {
                    ClearTree();
                    _Root = new TreeNode(result.DisplayName);
                    BuildTree(result.Children);
                    _Root.ImageIndex = result.IconIndex;
                    _Root.SelectedImageIndex = result.IconIndex;
                    _Root.Tag = result.RootItem;

                    _TreeView.Nodes.Add(_Root);
                    _Root.Expand();

                    if (_pendingExpansionItem != null)
                    {
                        var itemToExpand = _pendingExpansionItem;
                        var select = _pendingSelectExpandedNode;
                        _pendingExpansionItem = null;
                        _ = ExpandANodeAsync(itemToExpand, select);
                    }
                    else
                    {
                        _TreeView.SelectedNode = _Root;
                    }
                }
                finally
                {
                    _TreeView.EndUpdate();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in SetRootItemAsync: " + ex.ToString());
            }
        }

        /// <summary>
        /// Determines whether the specified <see cref="CShellItem"/> should be excluded from
        /// the tree display based on the <see cref="ExcludedItems"/> collection.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to test.</param>
        /// <returns>
        /// <c>true</c> if the item's path (stripped of leading/trailing <c>:</c>, <c>{</c>, and <c>}</c>
        /// characters) is found in <see cref="ExcludedItems"/>; otherwise <c>false</c>.
        /// </returns>
        private bool IsExcluded(CShellItem item)
        {
            if (_excludedItems.Count == 0 || item == null) return false;
            // Trim to match the logic used in MainForm's RemoveUselessSpecialLocations
            var path = (item.FullPath ?? "").Trim(':', '{', '}');
            return _excludedItems.Contains(path);
        }

        /// <summary>RefreshTree Method thanks to Calum McLellan</summary>
        [Description("Refresh the Tree and all nodes through the currently selected item")]
        private void RefreshTree(CShellItem? rootCSI = null)
        {
            if (_TreeView is null) return;

            // Modified to use ExpandANode(CShellItem) rather than ExpandANode(path)
            // Set refresh variable for BeforeExpand method
            EnableEventPost = false;
            TreeNode selnode;
            if (_TreeView.SelectedNode == null)
                selnode = _Root;
            else
                selnode = _TreeView.SelectedNode;

            try
            {
                _TreeView.BeginUpdate();
                CShellItem selCSI = (CShellItem)selnode.Tag;
                Task task;
                if (rootCSI == null)
                {
                    task = SetRootItemAsync(Root); 
                }
                else
                {
                    task = SetRootItemAsync(rootCSI);
                }

                task.ContinueWith(antecedent =>
                {
                    if (!ExpandANode(selCSI))
                    {
                        var nodeList = new List<TreeNode>();
                        while (!(selnode.Parent == null))
                        {
                            nodeList.Add(selnode.Parent);
                            selnode = selnode.Parent;
                        }

                        foreach (TreeNode currentSelnode in nodeList)
                        {
                            selnode = currentSelnode;
                            if (ExpandANode((CShellItem)selnode.Tag))
                                break;
                        }
                    }
                });

            }
            finally
            {
                _TreeView.EndUpdate();
            }
            EnableEventPost = true;
            Tv1_AfterSelect(this, new TreeViewEventArgs(_TreeView.SelectedNode));
        }

        /// <summary>
        /// NodePath returns the Text version of the full path of a TreeNode.
        /// </summary>
        /// <param name="node">The TreeNode to return the full path for.</param>
        /// <returns>The full path to the input node within a tree</returns>
        /// <remarks>Used only for some Debug.WriteLine statements.</remarks>
        private string NodePath(TreeNode node)
        {
            var pathlist = new List<TreeNode>() { node };
            while (node.Parent is not null)
            {
                pathlist.Add(node.Parent);
                node = node.Parent;
            }
            pathlist.Reverse();
            var SB = new StringBuilder();
            foreach (TreeNode N in pathlist)
            {
                SB.Append(N.Text);
                SB.Append(@"\");
            }
            return SB.ToString();
        }

        /// <summary>
        /// Called to Populate the TreeNodes of a TreeNode that only contains a Dummy Node.
        /// </summary>
        /// <param name="NodeToFill">The unexpanded TreeNode to Fill</param>
        /// <remarks>Should only be called to populate a TreeNode which only has a Dummy Node.<br />
        /// Refactored code added 8/26/2012 so that this functionality could be used from more than one method.</remarks>
        private void PopulateNode(TreeNode NodeToFill)
        {
            CShellItem csi = (CShellItem)NodeToFill.Tag;

            var flags = SHCONTF.FOLDERS;
            if (m_showHiddenFolders) flags |= SHCONTF.INCLUDEHIDDEN;
            var target = ShellController.Instance.LoadFolderContents(csi, flags);
            if (target != null && !ReferenceEquals(csi, target))
            {
                NodeToFill.Tag = target;
                csi = target;
            }

            List<CShellItem> D;
            if (csi.DirectoryList is null)
            {
                D = new List<CShellItem>(csi.Directories);
            }
            else
            {
                D = new List<CShellItem>(csi.DirectoryList);
            }
            if (D.Count > 0)
            {
                D.Sort();
                NodeToFill.Nodes.Clear();
                foreach (CShellItem Item in D)
                {
                    if (!(Item.IsHidden & !m_showHiddenFolders) && !IsExcluded(Item))
                    {
                        NodeToFill.Nodes.Add(MakeNode(Item));
                    }
                }
            }
            else
            {
                NodeToFill.Nodes.Clear();
            }
        }

        /// <summary>
        /// Asynchronously populates a lazy-loaded TreeNode by fetching its child
        /// <see cref="CShellItem"/> folders on a background STA thread, then updating
        /// the TreeView on the UI thread. Icon and sub-folder data are pre-warmed during
        /// the background pass to keep subsequent expansions responsive.
        /// </summary>
        /// <param name="NodeToFill">
        /// The <see cref="TreeNode"/> containing only a dummy placeholder node that should
        /// be replaced with its real child nodes.
        /// </param>
        private async Task PopulateNodeAsync(TreeNode NodeToFill)
        {
            if (NodeToFill.Tag is not CShellItem csi) return;

            try
            {
                var result = await _staRunner.EnqueueWork(t =>
                {
                    var flags = SHCONTF.FOLDERS;
                    if (m_showHiddenFolders) flags |= SHCONTF.INCLUDEHIDDEN;
                    var target = ShellController.Instance.LoadFolderContents(csi, flags);
                    CShellItem value = target ?? csi;

                    var children = value.Directories;
                    foreach (var child in children)
                    {
                        if (t.IsCancellationRequested) return null;
                        _ = child.HasSubFolders;
                        _ = child.IconIndexNormal;
                        _ = child.IconIndexOpen;
                    }

                    return new
                    {
                        Children = children,
                        Target = target
                    };
                });

                if (result == null) return;

                _TreeView.BeginUpdate();
                try
                {
                    if (result.Target != null) NodeToFill.Tag = result.Target;
                    NodeToFill.Nodes.Clear();

                    var children = result.Children;
                    Array.Sort(children);

                    foreach (CShellItem Item in children)
                    {
                        if (!(Item.IsHidden & !m_showHiddenFolders) && !IsExcluded(Item))
                        {
                            NodeToFill.Nodes.Add(MakeNode(Item));
                        }
                    }
                }
                finally
                {
                    _TreeView.EndUpdate();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in PopulateNodeAsync: " + ex.ToString());
            }
        }

        /// <summary>
        /// Walks the Shell namespace hierarchy of <paramref name="shellItem"/> from the Desktop
        /// downward through the TreeView to locate the corresponding <see cref="TreeNode"/>.
        /// </summary>
        /// <param name="shellItem">
        /// The <see cref="CShellItem"/> whose TreeNode is to be found. If <c>null</c>, the
        /// Desktop <see cref="CShellItem"/> is used.
        /// </param>
        /// <param name="treeNode">
        /// When this method returns <c>true</c>, contains the matching <see cref="TreeNode"/>;
        /// otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if a matching TreeNode was found in the currently displayed tree;
        /// <c>false</c> if the item is not represented by any visible node.
        /// </returns>
        private bool GetTreeNode(CShellItem shellItem, ref TreeNode? treeNode)
        {
            var pathList = new List<CShellItem>();
            if (shellItem is null)
                shellItem = ShellController.DesktopCSI;

            while (shellItem.Parent is not null)
            {
                pathList.Add(shellItem);
                shellItem = shellItem.Parent;
            }
            pathList.Add(shellItem);

            pathList.Reverse();

            if (_TreeView.Nodes.Count < 1)
                return false;
            treeNode = _TreeView.Nodes[0];
            int i = 0;
            bool found = false;
            while (i < pathList.Count)
            {
                if (ReferenceEquals(pathList[i], treeNode.Tag))
                {
                    found = true;
                    break;
                }
                i += 1;
            }
            if (!found)
            {
                treeNode = null;
                return false;
            }
            i += 1;
            while (i < pathList.Count)
            {
                found = false;
                foreach (TreeNode node in treeNode.Nodes)
                {
                    if (node.Tag is not null && ReferenceEquals(node.Tag, pathList[i]))
                    {
                        treeNode = node;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    treeNode = null;
                    return false;
                }
                i += 1;
            }
            return true;
        }

        /// <summary>
        /// Insert a TreeNode into its' Parent's Nodes list in its' proper location
        /// in its' Parent Node's Nodes list.
        /// </summary>
        /// <param name="Node">The Node to be inserted</param>
        /// <param name="ParentNode">The Parent Node of the Node to be inserted.</param>
        /// <remarks>Only called from Dynamic update code when the Parent Node is known (ie displayed).</remarks>
        private void InsertNode(TreeNode Node, TreeNode ParentNode)
        {
            CShellItem Item = (CShellItem)Node.Tag;
            // It is possible that the ParentNode has only a dummy node. Since we are adding a Node,
            // it is necessary to remove that dummy, beforehand. Note that this case cannot occur if all
            // prior references to the ParentNode occur only within ExpTree. In that case, ParentNode.Tag.Directories will not have
            // been Initialized so no Create or Rename messages will be passed to ExpTree - thus no InsertNode call.
            if (ParentNode.Nodes.Count == 1 && ParentNode.Nodes[0].Text.Equals(" : "))
            {
                PopulateNode(ParentNode);
            }
            else
            {
                for (int i = 0, loopTo = ParentNode.Nodes.Count - 1; i <= loopTo; i++)
                {
                    if (((CShellItem)ParentNode.Nodes[i].Tag).CompareTo(Item) > 0)
                    {
                        ParentNode.Nodes.Insert(i, Node);
                        return;
                    }
                }
                // on fall thru, did not find a spot to insert, so it goes at the end
                ParentNode.Nodes.Add(Node);
            }
        }

        /// <summary>
        /// Sorts the Nodes of the input TreeNode
        /// </summary>
        /// <param name="N">The Node whose Nodes.Collection is to be sorted</param>
        /// <remarks></remarks>
        private void SortNodes(TreeNode N)
        {
            if (N.Nodes.Count > 1)
            {
                var tmp = new TreeNode[N.Nodes.Count];
                N.Nodes.CopyTo(tmp, 0);
                Array.Sort(tmp, new WindowsApiLib.Shell.TagComparer());
                N.Nodes.Clear();
                N.Nodes.AddRange(tmp);
            }
        }

        #endregion

        /// <summary>
        /// Cancels and disposes any active root-load cancellation token source,
        /// releasing associated resources.
        /// </summary>
        private void Cleanup()
        {
            _rootLoadCts?.Cancel();
            _rootLoadCts?.Dispose();
            _rootLoadCts = null;
            if (ShellController.Instance?.ShellUpdater != null)
                ShellController.Instance.ShellUpdater.UpdateEvent -= OnItemUpdate;
        }

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && components != null)
                {
                    components.Dispose();
                }
                Cleanup();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// The values representing the System's Special Folders.
        /// </summary>
        /// <remarks>Certain Special Folders are disallowed since they may not exist, or may cause program failure
        /// on certain versions of Windows (primarily the older, unsupported versions).</remarks>
        public enum StartDir : int
        {
            /// <summary>No startup directory specified; the tree will be empty.</summary>
            None = -1,
            /// <summary>The Desktop virtual folder (CSIDL 0x00).</summary>
            Desktop = 0x0,
            /// <summary>The Programs folder in the Start Menu (CSIDL 0x02).</summary>
            Programs = 0x2,
            /// <summary>The Control Panel virtual folder (CSIDL 0x03).</summary>
            Controls = 0x3,
            /// <summary>The Printers virtual folder (CSIDL 0x04).</summary>
            Printers = 0x4,
            /// <summary>The current user's Documents folder (CSIDL 0x05).</summary>
            Personal = 0x5,
            /// <summary>The current user's Favorites folder (CSIDL 0x06).</summary>
            Favorites = 0x6,
            /// <summary>The Startup folder in the Start Menu (CSIDL 0x07).</summary>
            Startup = 0x7,
            /// <summary>The Recent Documents folder (CSIDL 0x08).</summary>
            Recent = 0x8,
            /// <summary>The Send To folder (CSIDL 0x09).</summary>
            SendTo = 0x9,
            /// <summary>The Start Menu folder (CSIDL 0x0B).</summary>
            StartMenu = 0xB,
            /// <summary>The My Documents virtual folder (CSIDL 0x0C).</summary>
            MyDocuments = 0xC,
            /// <summary>The Desktop directory in the file system (CSIDL 0x10).</summary>
            DesktopDirectory = 0x10,
            /// <summary>The My Computer virtual folder (CSIDL 0x11).</summary>
            MyComputer = 0x11,
            /// <summary>The My Network Places virtual folder (CSIDL 0x12).</summary>
            My_Network_Places = 0x12,
            /// <summary>The Application Data roaming folder (CSIDL 0x1A).</summary>
            ApplicatationData = 0x1A,
            /// <summary>The Temporary Internet Files (cache) folder (CSIDL 0x20).</summary>
            Internet_Cache = 0x20,
            /// <summary>The Cookies folder (CSIDL 0x21).</summary>
            Cookies = 0x21,
            /// <summary>The History folder (CSIDL 0x22).</summary>
            History = 0x22,
            /// <summary>The Windows installation directory (CSIDL 0x24).</summary>
            Windows = 0x24,
            /// <summary>The System32 directory (CSIDL 0x25).</summary>
            System = 0x25,
            /// <summary>The Program Files directory (CSIDL 0x26).</summary>
            Program_Files = 0x26,
            /// <summary>The My Pictures folder (CSIDL 0x27).</summary>
            MyPictures = 0x27,
            /// <summary>The current user's profile directory (CSIDL 0x28).</summary>
            Profile = 0x28,
            /// <summary>The 32-bit System directory on a 64-bit OS (CSIDL 0x29).</summary>
            Systemx86 = 0x29,
            /// <summary>The Administrative Tools folder (CSIDL 0x30).</summary>
            AdminTools = 0x30
            // MyMusic = &HD
            // MyVideo = &HE
            // NETHOOD = &H13
            // FONTS = &H14
            // PRINTHOOD = &H1B
        }
    }


}
