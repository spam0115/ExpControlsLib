using System.Collections;
using System.Windows.Forms;

namespace WindowsApiLib.Shell
{
    /// <summary> It is sometimes useful to sort a list of TreeNodes,
    /// ListViewItems, or other objects in an order based on CShItems in their Tag.
    /// TagComparer is a Icomparer Class for that situation. Sorting is based on CShellItem.CompareTo
    /// </summary>
    public class TagComparer : IComparer
    {
        /// <summary>
        /// Compares the .Tags of two Objects, which must be CShItems.
        /// </summary>
        /// <param name="x">First Object with a CShellItem in its' .Tag</param>
        /// <param name="y">Second Object with a CShellItem in its' .Tag</param>
        /// <returns>-1, 0, or 1 depending on the results of comparing the two CShItems</returns>
        /// <remarks>See CShellItem.CompareTo for discussion of the Comparison of two CShItems</remarks>
        public int Compare(object? x, object? y)
        {
            CShellItem xTag = null;
            CShellItem yTag = null;

            // Try common WinForms types first
            if (x is TreeNode xtn) xTag = xtn.Tag as CShellItem;
            else if (x is ListViewItem xlvi) xTag = xlvi.Tag as CShellItem;
            else //resort to using reflection to try to get a Tag property, if it exists
            {
                var px = x?.GetType().GetProperty("Tag");
                if (px != null) xTag = px.GetValue(x) as CShellItem;
            }

            if (y is TreeNode ytn) yTag = ytn.Tag as CShellItem;
            else if (y is ListViewItem ylvi) yTag = ylvi.Tag as CShellItem;
            else
            {
                var py = y?.GetType().GetProperty("Tag");
                if (py != null) yTag = py.GetValue(y) as CShellItem;
            }

            // Null handling: consider null < non-null
            if (xTag is null && yTag is null) return 0;
            if (xTag is null) return -1;
            if (yTag is null) return 1;

            return xTag.CompareTo(yTag);
        }
    }
}
