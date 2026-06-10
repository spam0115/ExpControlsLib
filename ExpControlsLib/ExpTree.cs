using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning; // Added to annotate platform support
using System.Text;
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
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public partial class ExpTree
    {

        private TreeNode Root;

        /// <summary>
        /// StartUpDirectoryChanged is raised when the root of the TreeView is changed via StartUpDirectory
        /// Property. 
        /// </summary>
        /// <param name="newVal">One of the StartDir Enum values that represent the possible Start Up Directories.</param>
        /// <remarks>Seldom listened for since, in typical use, the Method which set the StartUpDirectory value
        /// is the only Method which is interested. It is also true that a by-product of setting the StartUpDirectory 
        /// value is the Selection of the new root node.  That change in SelectedNode will cause an ExpTreeNodeSelected
        /// Event to be raised.</remarks>
        public event StartUpDirectoryChangedEventHandler StartUpDirectoryChanged;

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

        public delegate void ExpTreeNodeSelectedEventHandler(string SelPath, CShellItem Item);

        private bool EnableEventPost = true; // flag to supress ExpTreeNodeSelected raising during refresh and 

        private Stack<CShellItem> _backHistory = new Stack<CShellItem>();
        private Stack<CShellItem> _forwardHistory = new Stack<CShellItem>();
        private bool _isNavigatingHistory = false;
        private CShellItem _lastSelectedCSI = null;

        private CtvDropWrapper _DropHandler;

        private CtvDropWrapper DropHandler
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

        private CDragWrapper DragHandler;

        private bool m_showHiddenFolders = true;

        private readonly ContextMenu m_windowsContextMenu = new ContextMenu();

        //[DllImport("user32", CharSet = CharSet.Auto)]
        //private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
        // For ExpFileList label text selection
        private const int EM_SETSEL = 0xB1;
        private const int TVM_FIRST = 0x1100;
        private const int TVM_GETEDITCONTROL = TVM_FIRST + 15;

        #region  Constructor/Destructor

        public ExpTree() : base()
        {
            // This call is required by the Windows Form Designer.
            InitializeComponent();

            // expandNodeTimer is used to expand a node that is hovered over, with a delay
            expandNodeTimer = new System.Windows.Forms.Timer();

            // Add any initialization after the InitializeComponent() call

            // setting the imagelist here allows many good things to happen, but
            // also one bad thing -- the "tooltip" like display of selectednode.text
            // is made invisible.  This remains a problem to be solved.
            SetTreeViewImageList(_TreeView, false);

            StartUpDirectoryChanged += OnStartUpDirectoryChanged;

            CShellItemUpdater.UpdateEvent += OnItemUpdate;
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
            // For send to menu in the explorer context menu
            int hr;
            if (m.Msg == (long)WM.INITMENUPOPUP | m.Msg == (long)WM.MEASUREITEM | m.Msg == (long)WM.DRAWITEM)
            {
                if (m_windowsContextMenu.cntxMenuExtended is not null)
                {
                    hr = m_windowsContextMenu.cntxMenuExtended.HandleMenuMsg(m.Msg, m.WParam, m.LParam);
                    if (hr == 0)
                    {
                        return;
                    }
                }
            }
            else if (m.Msg == (long)WM.MENUCHAR)
            {
                if (m_windowsContextMenu.cntxMenuCascading is not null)
                {
                    hr = m_windowsContextMenu.cntxMenuCascading.HandleMenuMsg2(m.Msg, m.WParam, m.LParam, IntPtr.Zero);
                    if (hr == 0)
                    {
                        return;
                    }
                }
            }
            base.WndProc(ref m);
        }

        #endregion


        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private void Tv1_HandleCreated(object sender, EventArgs e)
        {
            DragHandler = new ExpControlsLib.CDragWrapper(_TreeView);
            if (m_AllowDrop)
                DropHandler = new CtvDropWrapper(_TreeView); // 7/11/2012
            SetWindowTheme(_TreeView.Handle, "explorer", null);
            // Update: Check against nothing as when the treeview is used in a modal 
            // dialog and shown more than once a duplicate call causes a horizontal 
            // scrollBar to appear in the control???
            // If tv1.TreeViewNodeSorter Is Nothing Then tv1.TreeViewNodeSorter = New TagComparer  '5/9/2012 Removed - not needed and can cause extreme delays
        }

        private void Tv1_HandleDestroyed(object sender, EventArgs e)
        {

        }


        #region    Public Properties

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
                    if (_TreeView.IsHandleCreated)
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

        public override Color ForeColor
        {
            get
            {
                return _TreeView.ForeColor;
            }
            set
            {
                if (value != _TreeView.ForeColor)
                {
                    _TreeView.ForeColor = value;
                }
            }
        }

        public override Color BackColor
        {
            get
            {
                return _TreeView.BackColor;
            }
            set
            {
                if (value != _TreeView.ForeColor)
                {
                    _TreeView.BackColor = value;
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
        public CShellItem RootItem
        {
            get
            {
                if (Root is null || Root.Tag is null)  // 11/05/2013
                {
                    return ShellController.DesktopCSI;                       // 11/05/2013
                }
                else                                                // 11/05/2013
                {
                    return (CShellItem)Root.Tag;
                }                                              // 11/05/2013
            }
            set
            {
                if (value is null) return;

                if (value.IsFolder)
                {
                    _TreeView.BeginUpdate();
                    ClearTree();

                    var target = ShellController.Instance.LoadFolderContents(value);
                    if (target != null) value = target;

                    CShellItem[] CSI = value.Directories;
                    Root = new TreeNode(value.DisplayName);
                    BuildTree(CSI);
                    Root.ImageIndex = GetIconIndex(value, false);
                    Root.SelectedImageIndex = Root.ImageIndex;
                    Root.Tag = value;
                    _TreeView.Nodes.Add(Root);
                    Root.Expand();
                    _TreeView.SelectedNode = Root;
                    _TreeView.EndUpdate();
                }
            }
        }

        /// <summary>
        /// Run-time only Property which returns the CShellItem underlying the SelectedNode of the TreeView.
        /// </summary>
        /// <returns>The underlying CShellItem of the TreeView.SelectedNode. If none Selected, returns Nothing.</returns>
        [Browsable(false)]
        public CShellItem SelectedItem
        {
            get
            {
                if (!(_TreeView.SelectedNode == null))
                {
                    return (CShellItem)_TreeView.SelectedNode.Tag;
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
        public TreeNodeCollection Nodes => _TreeView.Nodes;

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
                    if (Root is not null)
                        RefreshTree(); // Fix 2/5/2012
                }
            }
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
                return _TreeView.ShowRootLines;
            }
            set
            {
                if (!(value == _TreeView.ShowRootLines))
                {
                    _TreeView.ShowRootLines = value;
                    _TreeView.Refresh();
                }
            }
        }

        /// <summary>
        /// The values representing the System's Special Folders.
        /// </summary>
        /// <remarks>Certain Special Folders are disallowed since they may not exist, or may cause program failure
        /// on certain versions of Windows (primarily the older, unsupported versions).</remarks>
        public enum StartDir : int
        {
            None = -1,
            Desktop = 0x0,
            Programs = 0x2,
            Controls = 0x3,
            Printers = 0x4,
            Personal = 0x5,
            Favorites = 0x6,
            Startup = 0x7,
            Recent = 0x8,
            SendTo = 0x9,
            StartMenu = 0xB,
            MyDocuments = 0xC,
            // MyMusic = &HD
            // MyVideo = &HE
            DesktopDirectory = 0x10,
            MyComputer = 0x11,
            My_Network_Places = 0x12,
            // NETHOOD = &H13
            // FONTS = &H14
            ApplicatationData = 0x1A,
            // PRINTHOOD = &H1B
            Internet_Cache = 0x20,
            Cookies = 0x21,
            History = 0x22,
            Windows = 0x24,
            System = 0x25,
            Program_Files = 0x26,
            MyPictures = 0x27,
            Profile = 0x28,
            Systemx86 = 0x29,
            AdminTools = 0x30
        }

        private StartDir m_StartUpDirectory = StartDir.None;

        // 11/04/2012 Removed DefaultValue Property from this declaration.
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
                    StartUpDirectoryChanged?.Invoke(value);
                }
                else
                {
                    throw new ApplicationException("Invalid Initial StartUpDirectory");
                }
            }
        }

        #endregion


        #region    Public Methods

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
        /// <pre lang="vbnet">Public Function ExpandANode(ByVal newItem As CShellItem) As Boolean</pre> 
        /// If the item defined by the input Path does not exist, False is returned.<br />
        /// Calling with SelectExpandedNode = False is useful when it is not desired to Raise an
        /// ExpTreeNodeSelected Event as a result of ExpandaNode.</remarks>
        public bool ExpandANode(string newPath, bool SelectExpandedNode = true)  // 7/13/2012
        {
            bool ExpandANodeRet = default;
            ExpandANodeRet = false;     // assume failure
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
            return ExpandANode(newItem, SelectExpandedNode); // 7/13/2012
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
        public bool ExpandANode(CShellItem newItem, bool SelectExpandedNode = true)   // 7/13/2012
        {
            bool ExpandANodeRet = default;
            ExpandANodeRet = false;     // assume failure
            var baseNode = Root;
            if (baseNode == null) return false;
            _TreeView.BeginUpdate();

            // do the drill down -- Node to expand must be included in tree
            baseNode.Expand(); // Ensure base is filled in

            // Get the pidl value from baseNode.Tag by casting to CShellItem
            if (baseNode.Tag == null)
            {
                throw new InvalidOperationException("baseNode.Tag cannot be null.");
            }

            CShellItem baseItem = (CShellItem)baseNode.Tag;
            IntPtr basePidl = baseItem.PIDL;
            int lim = CPidl.SegmentCount(newItem.PIDL) - CPidl.SegmentCount(basePidl);

            // TODO: Test ExpandARow again on XP to ensure that the CP problem is fixed
            while (lim > 0)
            {
                bool continueDo = false;
                foreach (TreeNode testNode in baseNode.Nodes)
                {
                    if (CPidl.IsAncestorOf((CShellItem)testNode.Tag, newItem, false))
                    {
                        baseNode = testNode;
                        // RefreshNode(baseNode)   'ensure up-to-date
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
            NEXLEV:
                ;
            }
            // after falling thru here, we have found & expanded the node
            _TreeView.HideSelection = false;
            Select();
            if (SelectExpandedNode)
                _TreeView.SelectedNode = baseNode; // 7/13/2012
            ExpandANodeRet = true;
        XIT:
            ;
            _TreeView.EndUpdate();
            baseNode.EnsureVisible();       // 12/18/13
            return ExpandANodeRet;
        }

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
        public void GoBack()
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(_lastSelectedCSI);
                var prev = _backHistory.Pop();
                _isNavigatingHistory = true;
                try
                {
                    ExpandANode(prev, true);
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
        public void GoForward()
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(_lastSelectedCSI);
                var next = _forwardHistory.Pop();
                _isNavigatingHistory = true;
                try
                {
                    ExpandANode(next, true);
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
        public void GoUp()
        {
            if (_lastSelectedCSI?.Parent != null)
            {
                ExpandANode(_lastSelectedCSI.Parent, true);
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

        #region    Dynamic Update Handler

        // Private WithEvents DeskTopItem As CShellItem = CShellItem.GetDeskTop     '7/1/2012

        private void OnItemUpdate(object sender, ShellItemUpdateEventArgs e)
        {
            // Debug.WriteLine("Enter ExpTree OnItemUpdate -- " & e.Item.DisplayName & " - " & e.UpdateType.ToString)
            if (e.Item is not null && e.Item.IsFolder)  // no interest in non-folder events (or UpdateDir)
            {
                try
                {
                    //CShellItem parent = (CShellItem)sender;
                    CShellItem parent = e.Item.Parent;
                    TreeNode? pNode = default(TreeNode);
                    if (GetTreeNode(parent, ref pNode))
                    {
                        // Debug.WriteLine("Located Parent Node " & pNode.Text & " of Item " & e.Item.Path)
                        _TreeView.BeginUpdate();
                        switch (e.UpdateType)
                        {
                            case CShItemUpdateType.Created:  // A new Dir has been created under Parent/pNode
                                {
                                    var Node = MakeNode(e.Item);
                                    // Debug.WriteLine("Adding Node " & NodePath(Node))
                                    InsertNode(Node, pNode); // 6/25/2012
                                                             // pNode.Nodes.Add(Node)  '6/25/2012
                                                             // tv1.Invalidate()   '6/18/2012 - Trust tv1 to do right thing on an Add
                                    break;
                                }
                            case CShItemUpdateType.Deleted:  // An old Dir has been deleted from Parent/pNode
                                {
                                    bool exitSelect = false;
                                    foreach (TreeNode Node in pNode.Nodes)
                                    {
                                        if (Node.Tag is not null && (ReferenceEquals(Node.Tag, e.Item) || CPidl.ResolvesToSamePathOrName(((CShellItem)Node.Tag).PIDL, e.Item.PIDL)))
                                        {
                                            // Debug.WriteLine("Removing Node " & NodePath(Node))
                                            pNode.Nodes.Remove(Node);
                                            // tv1.Invalidate()   '6/18/2012 - Trust tv1 to do right thing on a Delete
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
                                            if (GetTreeNode(e.Item.Parent, ref curPNode))
                                            {
                                                InsertNode(Node, curPNode); // 6/25/2012
                                                                            // curPNode.Nodes.Add(Node)  '6/25/2012
                                                if (wasSelected)     // 6/25/2012
                                                {
                                                    _TreeView.SelectedNode = Node;
                                                    Node.EnsureVisible();
                                                }
                                            }
                                            // tv1.Invalidate()   '6/18/2012 - Trust tv1 to do right thing on an Add or Delete
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
                            case CShItemUpdateType.MediaChange:  // Media has been added/removed
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
                            case CShItemUpdateType.Updated:  // 5/24/2012 - In this case, it is the Item that had some change. Check if Expandability has changed
                                {
                                    var UNode = default(TreeNode);
                                    if (GetTreeNode(e.Item, ref UNode))    // otherwise don't care
                                    {
                                        // If UNode.IsExpanded Then        'Earlier msgs will update the nodes
                                        // SortNodes(UNode)
                                        // Else    '6/5/2012 - check Expandable - in case a Folder added or Deleted which may happen without another message (Async ops)
                                        if (UNode.Nodes.Count == 0)     // Was not Expandable, should it be? (Folder may have been added)
                                        {
                                            if (ShouldHaveDummy(e.Item))
                                            {
                                                UNode.Nodes.Add(new TreeNode(" : "));
                                            }
                                            UNode.Collapse(false);   // 02/12/2014 can only have 0 or 1 (dummy) node - collapse to avoid showing dummy
                                        }
                                        // 02/12/2014 ElseIf Block recast and now uses DirCount rather than Directories
                                        else if (UNode.Nodes.Count == 1 && UNode.Nodes[0].Text.Equals(" : ")) // Should it still have dummy? (Folder may have been Deleted)
                                        {
                                            if (!ShouldHaveDummy(e.Item))
                                            {
                                                UNode.Nodes.Clear();
                                            }
                                        }
                                        // End If
                                    }

                                    break;
                                }

                            default:
                                {
                                    break;
                                }
                                // Don't care about any other type of change
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ExpTree Update Error -- " + ex.ToString());
                }
                finally
                {
                    _TreeView.EndUpdate();
                }
            }
            else { } // no find means that node not expanded and therefore of no interest
        }

        #endregion

        #region    Initial Dir Set Handler

        private void OnStartUpDirectoryChanged(StartDir newVal)
        {
            if (newVal == StartDir.None)
            {
                _TreeView.BeginUpdate();
                ClearTree();
                _TreeView.EndUpdate();
                return;
            }

            _TreeView.BeginUpdate();
            ClearTree();
            CShellItem special;
            special = CShellItemFactory.CreateCShItem((CSIDL)newVal);

            var target = ShellController.Instance.LoadFolderContents(special);
            if (target != null) special = target;

            Root = new TreeNode(special.DisplayName)
            {
                Tag = special,
                ImageIndex = GetIconIndex(special, false)
            };
            Root.SelectedImageIndex = Root.ImageIndex;
            BuildTree(special.Directories);
            _TreeView.Nodes.Add(Root);
            Root.Expand();
            _TreeView.EndUpdate();
        }
        private void BuildTree(CShellItem[] L1)
        {
            Array.Sort(L1);
            foreach (var CSI in L1)
            {
                if (!(CSI.IsHidden & !m_showHiddenFolders))
                {
                    Root.Nodes.Add(MakeNode(CSI));
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

            // Determine if we should perform the accurate lookahead check.
            // Rules from user: 
            // 1. Accurate check for the root node (Desktop) and all file system objects.
            // 2. Skip slow devices (Network, Removable) and virtual locations (other than the root).
            
            bool performAccurateCheck = false;
            if (ReferenceEquals(item, RootItem) || ReferenceEquals(item, ShellController.DesktopCSI))
                performAccurateCheck = true;
            else if (item.IsFileSystem && !item.IsRemote && !item.IsRemovable && !item.IsNetworkDrive)
                performAccurateCheck = true;

            if (performAccurateCheck)
            {
                // Force a one-level lookahead to ensure the arrow accurately reflects visible contents.
                // Added hierarchy registration
                var target = ShellController.Instance.LoadFolderContents(item);
                if (target != null) item = target;

                // item.Directories will perform/retrieve the enumeration.
                foreach (CShellItem sub in item.Directories)
                {
                    if (m_showHiddenFolders || !sub.IsHidden)
                    {
                        return true;
                    }
                }
                // If we reach here, we've verified that there are no visible subfolders.
                return false;
            }
            else
            {
                // Fast-path: rely on shell hints for complex/slow items to maintain responsiveness.
                return item.HasSubFolders || (item.IsHidden && item.DirCount > 0);
            }
        }

        private void ClearTree()
        {
            _TreeView.Nodes.Clear();
            Root = null;
        }
        #endregion


        #region Event Handling


        private void Tv1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var oldCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text.Equals(" : "))
            {
                PopulateNode(e.Node);            // 8/26/2012
            }
            e.Node.ImageIndex = ((CShellItem)e.Node.Tag).IconIndexOpen;
            Cursor = oldCursor;
        }

        private void Tv1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            CShellItem CSI = (CShellItem)e.Node.Tag;

            // record history
            if (!_isNavigatingHistory && _lastSelectedCSI != null && !ReferenceEquals(_lastSelectedCSI, CSI))
            {
                _backHistory.Push(_lastSelectedCSI);
                _forwardHistory.Clear();
            }
            _lastSelectedCSI = CSI;

            // **********Added by Lukai-2021.12.02, If a folder is created by code "My.Computer.FileSystem.CreateDirectory(folderPath)", then this folder can't be shown automatically, I need to refresh it in here manually
            if (System.IO.Directory.Exists(CSI.FullPath))
            {
                try
                {
                    if (e.Node.GetNodeCount(false) != System.IO.Directory.GetDirectories(CSI.FullPath).Length)
                    {
                        ShellController.Instance.ShellUpdater.DoUpdateDir(CSI, false, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error reading folder: " + ex.Message);
                }
            }
            // **********

            if (EnableEventPost) // turned off during RefreshTree
            {
                if (CSI.FullPath.StartsWith(":"))
                {
                    ExpTreeNodeSelected?.Invoke(CSI.DisplayName, CSI);
                }
                else
                {
                    ExpTreeNodeSelected?.Invoke(CSI.FullPath, CSI);
                }
            }
        }
        private void ExpTree_MouseUp(object sender, MouseEventArgs e)
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
                    if (m_windowsContextMenu.ShowMenu(Handle, itms, MousePosition, m_allowFolderRename, out cmi, m_minimalContextMenu))
                    {
                        // Check for rename
                        var cmdBytes = new byte[257];
                        m_windowsContextMenu.cntxMenuBase.GetCommandString(cmi.lpVerb.ToInt32(), (int)GCS.VERBA, 0, cmdBytes, 256);

                        string cmdName = SzToString(cmdBytes).ToLower();
                        if (cmdName.Equals("rename"))
                        {
                            _TreeView.LabelEdit = true;
                            tn.BeginEdit();
                        }
                        else
                        {
                            string strPath;
                            if (ReferenceEquals(itms[0], ShellController.DesktopCSI))
                            {
                                strPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            }
                            else
                            {
                                strPath = itms[0].Parent.FullPath;
                            }
                            m_windowsContextMenu.InvokeCommand(m_windowsContextMenu.cntxMenuBase, (uint)cmi.lpVerb, strPath, pt);
                        }
                        // Marshal.ReleaseComObject(m_windowsContextMenu.winMenu)
                        m_windowsContextMenu.ReleaseMenu();
                    }
                }
            }
            OnMouseUp(e);
        }

        private void Tv1_MouseDown(object sender, MouseEventArgs e)
        {
            OnMouseDown(e);
        }

        private void Tv1_MouseMove(object sender, MouseEventArgs e)
        {
            OnMouseMove(e);
        }

        private void Tv1_MouseEnter(object sender, EventArgs e)
        {
            OnMouseEnter(e);
        }

        private void Tv1_MouseLeave(object sender, EventArgs e)
        {
            OnMouseLeave(e);
        }
        private void Tv1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\u0003' | e.KeyChar == '\u0016' | e.KeyChar == '\u0018' | e.KeyChar == '\r')  // Ctrl + C
                                                                                                            // Ctrl + V
                                                                                                            // Ctrl + X
                                                                                                            // Enter
            {
                e.Handled = true;  // Eliminate warning sound
            }
            OnKeyPress(e);
        }

        private void Tv1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_TreeView.SelectedNode.GetNodeCount(false) > 0 & _TreeView.SelectedNode.IsExpanded == false)
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

        private void Tv1_KeyDown(object sender, KeyEventArgs e)
        {
            OnKeyDown(e);
        }

        // Public Event TreeKeyDown(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyEventArgs)

        // Private Sub tv1_KeyDown(ByVal sender, ByVal e As System.Windows.Forms.KeyEventArgs) Handles tv1.KeyDown
        // Debug.WriteLine("KeyDown in ExpTree Char = " & e.KeyData.ToString)
        // If e.KeyData = Keys.Escape Then e.Handled = True
        // RaiseEvent TreeKeyDown(sender, e)
        // End Sub

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
                if (Root is not null)
                {
                    Root.Expand();
                    if (!(_TreeView.SelectedNode == null))
                    {
                        _TreeView.SelectedNode.Expand();
                    }
                    else
                    {
                        _TreeView.SelectedNode = Root;
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
            if (!_TreeView.ShowRootLines && ReferenceEquals(e.Node, Root))
            {
                e.Cancel = true;
            }
            else
            {
                e.Node.ImageIndex = ((CShellItem)e.Node.Tag).IconIndexNormal;
            }
        }


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
                _TreeView.LabelEdit = value;
            }
        }

        // Newest code from Calum for Before and After LabelEdit. His remarks are:
        // I also made some changes to ExpTree, I added a check for a dummy node as after renaming a folder 
        // that hadn't been expanded I would receive an error as a BeforeLabelEdit event was fired for the 
        // dummyx node. This only happened with SharePoint folders 
        // (Note that SharePoint folder ALWAYS return true for HasSubFolders and this was happening on 
        // folders without subfolders...) I also replaced the IsFileSystem check (always false for SharePoint) 
        // with a check for a special folder path - this seemed to cover everthing that CanRename didn't cover.
        // I removed the character check in AfterLabelEdit as SetNameOf shows the user a message with illegal 
        // characters if there are any. 
        private void Tv1_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e.Node.Text == " : ")
            {
                e.CancelEdit = true;
                return;
            }
            CShellItem item = (CShellItem)e.Node.Tag;
            // If item.Path.StartsWith("::") Or item.IsDisk Or (Not m_allowFolderRename) Or _
            // item.Path = CShellItem.GetCShItem(CSIDL.MYDOCUMENTS).Path Or _
            // Not (item.CanRename) Then
            // Changed 11/28/2010
            if (item.FullPath.StartsWith("::") || item.IsDisk || !m_allowFolderRename
                || (item.FullPath ?? "") == (CShellItemFactory.CreateCShItem(CSIDL.MYDOCUMENTS).FullPath ?? "")
                || !item.CanRename)
            {
                System.Media.SystemSounds.Beep.Play();
                e.CancelEdit = true;
            }
            // **********Added by Lukai-2020.06.19, only select the label without file name extension
            if (e.CancelEdit == false)
            {
                var editWnd = SendMessage(_TreeView.Handle, TVM_GETEDITCONTROL, (IntPtr)0, IntPtr.Zero);
                int textLen = System.IO.Path.GetFileNameWithoutExtension(item.FullPath).Length;
                SendMessage(editWnd, EM_SETSEL, (IntPtr)0, (IntPtr)textLen);
            }
        }

        private void Tv1_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
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
                // **********Added by Lukai-2020.06.19
                if (string.IsNullOrEmpty(NewName) == false)
                {
                    System.Media.SystemSounds.Beep.Play();
                }
                // System.Media.SystemSounds.Beep.Play()
                return;
            }

            var newPidl = IntPtr.Zero;
            if (item.Parent.IShlFolder.SetNameOf((int)_TreeView.Handle, CPidl.ILFindLastID(item.PIDL), NewName, SHGDN.NORMAL, ref newPidl) == S_OK)
            {
            }
            // the following line is not needed since use of SetNameOf will cause a renamed WM_Notify msg 
            // which will be handled thru normal change notification processes
            // item.Update(newPidl, CShItemUpdater.CShItemUpdateType.Renamed)
            else
            {
                System.Media.SystemSounds.Beep.Play();
                e.CancelEdit = true;
            }
        }

        #endregion Event Handling


        #region    CtvDropWrapper Event Handling

        // dropNode is the TreeNode that most recently was DraggedOver or
        // Dropped onto.  
        private TreeNode dropNode;

        private Point NodePoint;
        private System.Windows.Forms.Timer expandNodeTimer;

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
                    // 7/12/2012 - The following block of code relocated from ShDragOver
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
                    // 7/12/2012 - end of relocated block
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
            expandNodeTimer.Stop();    // shut off the dragging over nodes timer
                                       // Debug.WriteLine("Enter ExpTree ShDragLeave")
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
            // Debug.WriteLine("Enter ExpTree ShDragOver. PdwEffect = " & pdwEffect)
            // Debug.WriteLine(vbTab & "Over node: " & CType(Node, TreeNode).Text)

            if (Node == null)  // clean up node stuff & fix color. Leave Draginfo alone-cleaned up on DragLeave
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
                    // 7/12/2012 - the following block relocated to expandNodeTime.Tick
                    // Dim delta As Integer = tv1.Height - pt.Y
                    // If delta < tv1.Height / 2 And delta > 0 Then
                    // If Not IsNothing(Node) AndAlso Not (Node.NextVisibleNode Is Nothing) Then
                    // Node.NextVisibleNode.EnsureVisible()
                    // End If
                    // End If
                    // If delta > tv1.Height / 2 And delta < tv1.Height Then
                    // If Not IsNothing(Node) AndAlso Not (Node.PrevVisibleNode Is Nothing) Then
                    // Node.PrevVisibleNode.EnsureVisible()
                    // End If
                    // End If
                    // 7/12/2012 - end of relocated block
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
                    expandNodeTimer.Interval = 500;  // 7/12/2012 - reduced from 1200
                    expandNodeTimer.Start();
                }
            }
        }

        private void DragWrapper_ShDragDrop(TreeNode Node, int grfKeyState, int pdwEffect)
        {
            expandNodeTimer.Stop();
            // Debug.WriteLine("Enter ExpTree ShDragDrop. PdwEffect = " & pdwEffect)
            // Debug.WriteLine(vbTab & "Over node: " & CType(Node, TreeNode).Text)

            if (!(dropNode == null))
            {
                ResetTreeviewNodeColor(dropNode);
            }
            else
            {
                ResetTreeviewNodeColor(_TreeView.Nodes[0]);
            }
            dropNode = null;
            // Debug.WriteLine("Leaving ExpTree ShDragDrop")
        }

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


        #region    Context Menu Methods
        // Credit Calum 

        private bool m_useWindowsContextMenu = true;
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

        /// <summary>RefreshTree Method thanks to Calum McLellan</summary>
        [Description("Refresh the Tree and all nodes through the currently selected item")]
        private void RefreshTree(CShellItem rootCSI = null)
        {
            // Modified to use ExpandANode(CShellItem) rather than ExpandANode(path)
            // Set refresh variable for BeforeExpand method
            EnableEventPost = false;
            // Begin Calum's change -- With some modification
            TreeNode Selnode;
            if (_TreeView.SelectedNode == null)
            {
                Selnode = Root;
            }
            else
            {
                Selnode = _TreeView.SelectedNode;
            }
            // End Calum's change
            try
            {
                _TreeView.BeginUpdate();
                CShellItem SelCSI = (CShellItem)Selnode.Tag;
                // Set root node
                if (rootCSI == null)
                {
                    RootItem = RootItem;
                }
                else
                {
                    RootItem = rootCSI;
                }
                // Try to expand the node
                if (!ExpandANode(SelCSI))
                {
                    var nodeList = new List<TreeNode>();
                    while (!(Selnode.Parent == null))
                    {
                        nodeList.Add(Selnode.Parent);
                        Selnode = Selnode.Parent;
                    }

                    foreach (TreeNode currentSelnode in nodeList)
                    {
                        Selnode = currentSelnode;
                        if (ExpandANode((CShellItem)Selnode.Tag))
                            break;
                    }
                    // Reset refresh variable for BeforeExpand method
                }
            }
            finally
            {
                _TreeView.EndUpdate();
            }
            EnableEventPost = true;
            // We suppressed EventPosting during refresh, so give it one now
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
            var pathlist = new List<TreeNode>() { node };  // pathlist.Add(node)
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
        private void PopulateNode(TreeNode NodeToFill)          // 8/26/2012
        {
            CShellItem CSI = (CShellItem)NodeToFill.Tag;

            // Added to ensure item is in hierarchy
            var target = ShellController.Instance.LoadFolderContents(CSI);
            if (target != null && !ReferenceEquals(CSI, target))
            {
                NodeToFill.Tag = target;
                CSI = target;
            }

            // 02/12/2014 - Setting of D changed at suggestion of Michael Ruby
            List<CShellItem> D;
            if (CSI.DirectoryList is null)
            {
                D = new List<CShellItem>(CSI.Directories); //todo: remove this conversion
            }
            else
            {
                D = new List<CShellItem>(CSI.DirectoryList);  //todo: remove this conversion
            }
            if (D.Count > 0)
            {
                D.Sort();    // uses the class comparer
                NodeToFill.Nodes.Clear();    // 11/03/2012 DO NOT Clear out the dummy prior to calling .Directories which forces a UpdateRefresh!
                foreach (CShellItem Item in D)
                {
                    if (!(Item.IsHidden & !m_showHiddenFolders))
                    {
                        NodeToFill.Nodes.Add(MakeNode(Item));
                    }
                }
            }
            else        // 11/03/2012 BUT DO get rid of any unnessesary Dummy
            {
                NodeToFill.Nodes.Clear();
            }
        }

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
                return false; // 11/05/2012
            treeNode = _TreeView.Nodes[0];
            int i = 0;
            // since pathList starts from Desktop and the tree may start somewhere below that, first locate
            // the tree base in the path
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
            if (!found)           // failed to find match between top of tree and top of pathlist -- so exit
            {
                treeNode = null;
                return false;
            }
            // have top of tree and pathList(i) as equal -- find actual node, down from top of tree
            i += 1;
            while (i < pathList.Count)
            {
                found = false;         // reset for next loop
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
        private void InsertNode(TreeNode Node, TreeNode ParentNode)    // 6/25/2012
        {
            CShellItem Item = (CShellItem)Node.Tag;
            // It is possible that the ParentNode has only a dummy node. Since we are adding a Node,
            // it is necessary to remove that dummy, beforehand. Note that this case cannot occur if all
            // prior references to the ParentNode occur only within ExpTree. In that case, ParentNode.Tag.Directories will not have
            // been Initialized so no Create or Rename messages will be passed to ExpTree - thus no InsertNode call.
            if (ParentNode.Nodes.Count == 1 && ParentNode.Nodes[0].Text.Equals(" : "))
            {
                PopulateNode(ParentNode);        // 8/26/2012 - PopulateNode will insert the node correctly
            }
            else                                // 8/26/2012 - Otherwise Insert Node in correct location
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
            }                              // 8/26/2012
        }

        /// <summary>
        /// Sorts the Nodes of the input TreeNode
        /// </summary>
        /// <param name="N">The Node whose Nodes.Collection is to be sorted</param>
        /// <remarks></remarks>
        private void SortNodes(TreeNode N)
        {
            if (N.Nodes.Count > 1)     // if not, why sort
            {
                var tmp = new TreeNode[N.Nodes.Count];
                N.Nodes.CopyTo(tmp, 0);
                Array.Sort(tmp, new WindowsApiLib.Shell.TagComparer());
                // tv1.BeginUpdate()      '6/18/2012 - not needed already in BeginUpdate when this rtn called
                N.Nodes.Clear();
                N.Nodes.AddRange(tmp);
                // tv1.EndUpdate()        '6/18/2012 - not needed already in BeginUpdate when this rtn called
            }
        }

        #endregion


        // Only for delete, cut, copy, paste
        private void WinMenuCmd(CShellItem CSI, string cmd)
        {
            if (CSI is not null)
            {
                int HR;
                int prgf = 0;
                var iunk = IntPtr.Zero;
                IShellFolder folder = null;
                if (ReferenceEquals(CSI, ShellController.DesktopCSI))
                {
                    folder = CSI.IShlFolder;
                }
                else
                {
                    folder = CSI.Parent.IShlFolder;
                }

                var relPidl = CPidl.ILFindLastID(CSI.PIDL);
                var IID_IContextMenu = ShellAPI.IID_IContextMenu;
                HR = folder.GetUIObjectOf(IntPtr.Zero, 1, new IntPtr[] { relPidl }, ref IID_IContextMenu, prgf, out iunk);
                #if DEBUG
                if (!(HR == S_OK))
                {
                    Marshal.ThrowExceptionForHR(HR);
                }
                #endif
                m_windowsContextMenu.cntxMenuBase = (IContextMenu)Marshal.GetObjectForIUnknown(iunk);
                var cmi = new CMInvokeCommandInfoEx();
                cmi.cbSize = Marshal.SizeOf(cmi);
                cmi.nShow = (int)SW.SHOWNORMAL;
                cmi.fMask = (int)(CMIC.UNICODE | CMIC.PTINVOKE);
                cmi.ptInvoke = new Point(0, 0);
                cmi.lpVerb = Marshal.StringToHGlobalAnsi(cmd);
                cmi.lpVerbW = Marshal.StringToHGlobalUni(cmd);

                HR = m_windowsContextMenu.cntxMenuBase.InvokeCommand(ref cmi);
                m_windowsContextMenu.ReleaseMenu();
                #if DEBUG 
                if (!(HR == S_OK))
                {
                    Marshal.ThrowExceptionForHR(HR);
                }
                #endif

            }
        }
    }
}