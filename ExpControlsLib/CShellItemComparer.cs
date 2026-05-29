using System;
using System.Collections.Generic;
using System.Windows.Forms;
using WindowsApiLib;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Implements IComparer for CShellItem to support custom sorting in ExpList, 
    /// especially for virtual mode.
    /// </summary>
    public class CShellItemComparer : IComparer<CShellItem>
    {
        private readonly int _column;
        private readonly SortOrder _order;
        private readonly ColumnHeader _columnHeader;
        private readonly string _mapping;

        /// <summary>
        /// Initializes a new instance of the CShellItemComparer class.
        /// </summary>
        /// <param name="column">The index of the column to sort on.</param>
        /// <param name="order">The sort order.</param>
        /// <param name="columnHeader">The ColumnHeader associated with the column.</param>
        public CShellItemComparer(int column, SortOrder order, ColumnHeader columnHeader)
        {
            _column = column;
            _order = order;
            _columnHeader = columnHeader;
            _mapping = columnHeader?.Tag?.ToString().Trim() ?? string.Empty;
        }

        /// <summary>
        /// Compares two CShellItems based on the specified column and order.
        /// </summary>
        public int Compare(CShellItem x, CShellItem y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            if (_order == SortOrder.None) return 0;

            int result = CompareInternal(x, y);

            if (_order == SortOrder.Descending)
                return -result;

            return result;
        }

        private int CompareInternal(CShellItem x, CShellItem y)
        {
            // 1. Try Tag Mapping (same logic as ExpList.GetColumnData)
            if (_mapping.StartsWith("."))
            {
                string propName = _mapping.Substring(1);
                switch (propName)
                {
                    case "DisplayName":
                        return StringLogicalComparer.CompareStrings(x.DisplayName, y.DisplayName);
                    case "TypeName":
                        return string.Compare(x.TypeName, y.TypeName, StringComparison.OrdinalIgnoreCase);
                    case "Size": // Maps to Length in GetColumnData
                        return x.Length.CompareTo(y.Length);
                    case "LastWriteTime":
                        return x.LastWriteTime.CompareTo(y.LastWriteTime);
                    case "CreationTime":
                        return x.CreationTime.CompareTo(y.CreationTime);
                }
            }

            // 2. Default to ColumnDic using the column text as key
            return CompareColumnDic(x, y);
        }

        private int CompareColumnDic(CShellItem x, CShellItem y)
        {
            string colText = _columnHeader.Text;

            bool xHas = x.ColumnDic.TryGetValue(colText, out var xData);
            bool yHas = y.ColumnDic.TryGetValue(colText, out var yData);

            if (!xHas && !yHas)
            {
                // Fallback for column 0 if no ColumnDic data
                if (_column == 0)
                    return StringLogicalComparer.CompareStrings(x.DisplayName, y.DisplayName);
                return 0;
            }

            if (!xHas) return -1;
            if (!yHas) return 1;

            // Sort based on Tag if it's IComparable (float, boolean, string, etc.)
            if (xData.Tag is IComparable cx && yData.Tag is IComparable cy && cx.GetType() == cy.GetType()) //todo: safe but slow.  remove checks and live by the seat of your pants
            {
                return cx.CompareTo(cy);
            }

            // Fallback to Text comparison
            return StringLogicalComparer.CompareStrings(x.DisplayName, y.DisplayName);
        }
    }
}
