using System.Drawing;
using System.Windows.Forms;

namespace ExpControlsLib;

/// <summary>Owns transient drag-over state and the delayed auto-expansion timer.</summary>
internal sealed class ExpTreeDragDropState : System.IDisposable
{
    public TreeNode? DropNode { get; set; }
    public Point NodePoint { get; set; }
    public Timer ExpandTimer { get; } = new();

    public void Dispose()
    {
        DropNode = null;
        ExpandTimer.Dispose();
    }
}
