using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib;

public partial class ExpTree
{
    /// <summary>Collapses every node currently displayed by the control.</summary>
    public void CollapseAll() => _TreeView.CollapseAll();

    /// <summary>
    /// Legacy name retained for source compatibility. Use <see cref="CollapseAll"/>.
    /// </summary>
    [Obsolete("Use CollapseAll instead.")]
    public void ExpCollapseAll(bool collapse = true)
    {
        if (collapse) CollapseAll();
    }

    /// <summary>
    /// Expands and selects a node without raising <see cref="ExpTreeNodeSelected"/>.
    /// </summary>
    public async Task<bool> SelectNodeSilentlyAsync(CShellItem target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnableEventPost = false;
        try
        {
            return await ExpandANodeAsync(target, SelectExpandedNode: true);
        }
        finally
        {
            EnableEventPost = true;
        }
    }

    /// <summary>
    /// Returns the TreeView's current node collection.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    public TreeNodeCollection? Nodes => _TreeView?.Nodes;
}
