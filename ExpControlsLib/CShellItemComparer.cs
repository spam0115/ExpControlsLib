using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Implements IComparer for CShellItem to support custom sorting in ExpList, 
    /// especially for virtual mode.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class CShellItemComparer : IComparer<CShellItem>
    {
        private readonly ExpList _expList;
        private readonly int _column;
        private readonly SortOrder _order;
        private readonly string _columnText;
        private readonly string _mapping;
        private readonly CShellItemComparer _secondaryComparer;

        /// <summary>
        /// Initializes a new instance of the CShellItemComparer class.
        /// </summary>
        /// <param name="expList">The ExpList instance to fetch data from.</param>
        /// <param name="column">The index of the column to sort on.</param>
        /// <param name="order">The sort order.</param>
        /// <param name="columnHeader">The ColumnHeader associated with the column.</param>
        /// <param name="secondaryComparer">The secondary comparer to use when primary comparison is equal.</param>
        public CShellItemComparer(ExpList expList, int column, SortOrder order, ColumnHeader columnHeader, CShellItemComparer secondaryComparer = null)
        {
            _expList = expList;
            _column = column;
            _order = order;
            _columnText = columnHeader?.Text ?? string.Empty;
            _mapping = columnHeader?.Tag?.ToString().Trim() ?? string.Empty;
            _secondaryComparer = secondaryComparer;
        }

        /// <summary>
        /// Compares two CShellItems based on the specified column and order.
        /// </summary>
        public int Compare(CShellItem? x, CShellItem? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            if (_order == SortOrder.None) return 0;

            // Maintain standard Windows Explorer behavior: Folders always come before files
            // (or after if descending, but usually folders are grouped).
            // Here we group folders first regardless of column (except maybe when specifically requested otherwise).
            if (x.IsFolder != y.IsFolder)
            {
                return x.IsFolder ? -1 : 1;
            }

            int result = CompareInternal(x, y);

            if (_order == SortOrder.Descending)
                result = -result;

            if (result == 0 && _secondaryComparer != null)
            {
                result = _secondaryComparer.Compare(x, y);
            }

            return result;
        }

        private int CompareInternal(CShellItem x, CShellItem y)
        {
            // 1. Try built in fields
            if (_mapping.StartsWith("."))
            {
                switch (_mapping)
                {
                    case ".Checked":
                        return x.Checked.CompareTo(y.Checked);
                    case ".ID":
                        return x.ID.CompareTo(y.ID);
                    case ".DisplayName":
                        return StringLogicalComparer.CompareStrings(x.DisplayName, y.DisplayName);
                    case ".TypeName":
                        return string.Compare(x.TypeName, y.TypeName, StringComparison.OrdinalIgnoreCase);
                    case ".Size": 
                        return x.Length.CompareTo(y.Length);
                    case ".LastWriteTime":
                        return x.LastWriteTime.CompareTo(y.LastWriteTime);
                    case ".CreationTime":
                        return x.CreationTime.CompareTo(y.CreationTime);
                }
            }

            // 2. Default to ColumnDic using GetColumnData to ensure data is fetched
            var xData = _expList.GetColumnData(x, _columnText, _column, _mapping);
            var yData = _expList.GetColumnData(y, _columnText, _column, _mapping);

            // Sort based on Tag if it's IComparable (float, boolean, string, etc.)
            if (xData.Tag is IComparable cx && yData.Tag is IComparable cy && cx.GetType() == cy.GetType())
            {
                return cx.CompareTo(cy);
            }

            // Fallback to text comparison (natural)
            int res = StringLogicalComparer.CompareStrings(xData.Text, yData.Text);
            if (res == 0 && _secondaryComparer == null)
                res = StringLogicalComparer.CompareStrings(x.DisplayName, y.DisplayName);
            return res;
        }
    }
}
