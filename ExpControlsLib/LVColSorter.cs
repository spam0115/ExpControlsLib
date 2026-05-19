using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ExpControlsLib
{
    /// <summary>
    /// LVColSorter is a Class to be used as a ListViewItemSorter. 
    /// LVColSorter may be used as the ListViewItemSorter for ListViews populated by any means, but works
    /// best when the .Tag properties of the ListViewItems and SubItems are set as described in Remarks.
    /// </summary>
    /// <remarks>LVColSorter uses the .Tag properties of ListViewItems and SubItems to Sort the contents of a
    /// ListView based on the underlying data. If no .Tag properties are set, or if the value of those
    /// properties do not implement the IComparable interface, then LVColSorter sorts based on the .Text
    /// properties of the SubItems of the ListViewItems.
    /// <para>If the .Text properties of SubItems are structured properly for Sorting, then no .Tag information
    /// is required for Sorting. Most .Text is not properly structured for Sorting. In that case, 
    /// the .Tag property may be set to provide this class with the information needed for a proper Sort.</para>
    /// <para>Set the .Tags as follows:
    /// <list type="table">
    /// <item><term>Each ListViewItem</term><description>The Class instance or DataRow from which the ListViewItem is built.
    ///                                                  The instance should support the IComparable Interface,
    ///                                                  if not, it is ignored for Sort purposes and may be omitted.</description></item>
    /// <item><term>Each SubItem</term><description>If the .Text property will not Sort correctly, then the .Tag should be
    ///                                set to the original Value (Date, Double, etc.)</description></item>
    /// </list>See the documentation of the Compare Method of this Class for the actual Sort rules.</para>
    /// <para>Class Properties
    /// or DataRow Fields whose Value is a String will Sort based on that String. String
    /// Properties that have been Formatted in a non-Sortable Format in the original data will not Sort correctly. 
    /// The application
    /// will have to deal with that case separately by setting the SubItem.Tags to a Sortable Value.</para>
    /// <para>Each instance of LVColSorter will handle the ListView.ColumnClick event for the associated
    /// ListView. The using application <i>should not</i> Handle that Event. When a new ListViewItemSorter
    /// is assigned to a ListView, any prior instances of LVColSorter will remove themselves from the 
    /// EventHandler list of that ListView. In other words, multiple ListViewItemSorters may be assigned
    /// to a ListView without causing prior instances to attempt to handle ColumnClick.</para></remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only

    public class LVColSorter : IComparer
    {
        /// <summary>
        /// Occurs when the sort column or order has changed.
        /// </summary>
        public event EventHandler SortOrderChanged;

        /// <summary>
        /// Compares two ListViewItems from the same ListView in accordance to the Sort rules of the Class.
        /// </summary>
        /// <param name="x">First ListViewItem to be Compared.</param>
        /// <param name="y">Second ListViewItem to be Compared.</param>
        /// <returns><list type="table">
        /// <item><term>-1</term><description>If the First item is Less than the Second.</description></item>
        /// <item><term>0</term><description>If the two items are Equal.</description></item>
        /// <item><term>1</term><description>If the First item is Greater than the Second.</description></item>
        /// </list></returns>
        /// <remarks>Odd numbers of clicks on a column will sort Ascending, even numbers will sort 
        /// Descending (click 1 Ascending, Click 2 Descending ...). The Sort rules are:
        /// <list type="number">
        /// <item><description>If the Clicked column is Column 0 and the ListViewItem's Tag supports the
        ///                    Icomparable Interface, then Compare the ListViewItems Tags.</description></item>
        /// <item><description>Otherwise, or if the ListViewItems .Tags Compare Equal, then continue with
        ///                    the following rules.</description></item>
        /// <item><description>If the Clicked column's ListViewItem.SubItem's Tag property supports the
        ///                    IComparable Interface, then use CompareTo to compare the .Tags</description></item>
        /// <item><description>If the Clicked column's ListViewItem.SubItem's Tag property is Nothing or
        ///                    does not support the Icomparable Interface, Compare the .Text properties.</description></item>
        /// <item><description>If the items Compare Equal and the Clicked column is not Column 0 then
        ///                    continue the Comparison based on the Column 0 rules above. This has
        ///                    the effect of using the either the source Class instances or the contents of
        ///                    column 0 as a secondary key for the Sort.</description></item>
        /// <item><description>The result of the comparison is toggled according to if the sort is
        ///                    Ascending or Decending. This sort order is determined by reversing the
        ///                    sort order of the last click on this column.</description></item>
        /// </list></remarks>
        /// 
        public int Compare(object x, object y)
        {
            if (x == null || y == null) return 0; //if you browse to network, you will get a null value for unknown reasons

            int CompareRet = default;

            // If m_ColOrder(m_Col) = 0 Then Exit Function 'First time thru with no columnclick. Retain original order 6/13/2012 - Allow use as standalone ie Insert with no Col Click
            CompareRet = 0;
            ListViewItem LVX = (ListViewItem)x;
            ListViewItem LVY = (ListViewItem)y;

            if (m_Col == 0 && OKToCompare(LVX.Tag, LVY.Tag))
            {
                CompareRet = CompareUsingCompareTo(LVX.Tag, LVY.Tag);
            }
            if (CompareRet == 0)
            {
                // Note that in some cases the SubItem Tags may not yet be set up (eg doing set up of some lvi's  in background thread)
                // in other words, the first lvi may have tags but not all lvi's have tags yet.
                if (OKToCompare(LVX.SubItems[m_Col].Tag, LVY.SubItems[m_Col].Tag))
                {
                    CompareRet = CompareUsingCompareTo(LVX.SubItems[m_Col].Tag, LVY.SubItems[m_Col].Tag);
                }
                else
                {
                    CompareRet = string.Compare(LVX.SubItems[m_Col].Text, LVY.SubItems[m_Col].Text);
                }
            }
            if (CompareRet == 0 && m_Col != 0)      // always use the original ordering as a second key (if not the primary)
            {
                if (m_Col == 0 && OKToCompare(LVX.Tag, LVY.Tag))
                {
                    CompareRet = CompareUsingCompareTo(LVX.Tag, LVY.Tag);
                }
                else if (OKToCompare(LVX.SubItems[0].Tag, LVY.SubItems[0].Tag))   // 6/13/2012 - fixed coding error
                {
                    CompareRet = CompareUsingCompareTo(LVX.SubItems[0].Tag, LVY.SubItems[0].Tag);
                }
                else
                {
                    CompareRet = LVX.SubItems[0].Text.CompareTo(LVY.SubItems[0].Text);
                }
            }
            if (m_ColOrder.Length == 0) return CompareRet;
            if (m_ColOrder[m_Col] != 0)
                CompareRet *= m_ColOrder[m_Col]; // 6/13/2012 - Allow use as standalone ie Insert with no Col Click
            return CompareRet;
        }

        #region    Private Fields
        private readonly ListView m_View;
        private readonly int[] m_ColOrder;
        private int m_Col;

        #endregion

        #region    Constructor

        /// <summary>
        /// Creates a new instance of LVColSorter based on a fully populated
        /// ListView, with ColumnHeaders defined. Assigns its own Handler for ListView.ColumnClick Events.
        /// </summary>
        /// <param name="lv">A fully populated ListView, preferably set up by SetUpListView, which will
        /// be using this instance as the ListViewItemSorter.</param>
        /// <remarks></remarks>
        public LVColSorter(ListView lv)
        {
            m_View = lv;
            m_ColOrder = new int[lv.Columns.Count];
            for (int i = 0, loopTo = lv.Columns.Count - 1; i <= loopTo; i++)
                ListViewSortGlyph.SetSortIcon(lv, i, SortOrder.None);
            lv.ListViewItemSorter = null;
            lv.ColumnClick += ListView_ColumnClick;
        }

        private bool OKToCompare(object X, object Y)
        {
            if (Y == null) {
                //Debug.WriteLine("Can't compare null object.");
                return false; 
            }

            bool OKToCompareRet = default;
            if (CompareOK(X))
            {
                OKToCompareRet = ReferenceEquals(X.GetType(), Y.GetType());
            }
            else
            {
                OKToCompareRet = false;
            }

            return OKToCompareRet;
        }

        private bool CompareOK(object obj)
        {
            bool CompareOKRet = false; // assume not OK
            if (obj is null)
                return CompareOKRet;
            Type[] IInfo = obj.GetType().GetInterfaces();
            if (IInfo is null)
                return CompareOKRet;
            foreach (Type Inter in IInfo)
            {
                if (Inter.Name.ToLower().StartsWith("icomparable"))
                {
                    return true;
                }
            }

            return CompareOKRet;
        }

        /// <summary>
        /// Use reflection to call an object's CompareTo method (works for generic and non-generic IComparable)
        /// </summary>
        private int CompareUsingCompareTo(object a, object b)
        {
            if (a is null)
                return (b is null) ? 0 : -1;
            try
            {
                var methods = a.GetType().GetMethods();
                foreach (var m in methods)
                {
                    if (m.Name == "CompareTo" && m.GetParameters().Length == 1)
                    {
                        var res = m.Invoke(a, new object[] { b });
                        return Convert.ToInt32(res);
                    }
                }
            }
            catch
            {
                // fall through and return 0 on error
            }
            return 0;
        }
        #endregion

        #region    Public Properties
        /// <summary>
        /// The order in which the ListView was last sorted.
        /// </summary>
        /// <returns>A SortOrder indicating the order in which the ListView was last sorted.</returns>
        /// <remarks>A return of SortOrder.None indicates that the ListView has never been sorted.
        /// The Properties OrderOfSort and SortColumn may be used if the application wishes to Draw Sort
        /// glyphs on the ColumnHeaders.
        /// </remarks>
        public SortOrder OrderOfSort
        {
            get
            {
                if (m_ColOrder.Length == 0 || m_Col >= m_ColOrder.Length) return SortOrder.None;
                if (m_ColOrder[m_Col] == 1) return SortOrder.Ascending;
                if (m_ColOrder[m_Col] == -1) return SortOrder.Descending;
                return SortOrder.None;
            }
        }
        /// <summary>
    /// The ListView column on which the ListView was last sorted. Setting this property to a valid
    /// value will cause the ListView to be sorted on that column in the order based on OrderOfSort rules.
    /// Specifically, the column will be sorted in reverse of the order it was last sorted.
    /// </summary>
    /// <returns>The ListView column on which the ListView was last sorted.</returns>
    /// <remarks>Unsorted ListViews will return 0 for the SortColumn.
    /// The Properties OrderOfSort and SortColumn may be used if the application wishes to Draw Sort
    /// glyphs on the ColumnHeaders.
    /// </remarks>
        public int SortColumn
        {
            get
            {
                return m_Col;
            }
            set
            {
                if (value > -1 && value < m_View.Columns.Count)
                {
                    m_Col = value;
                    ListView_ColumnClick(m_View, new ColumnClickEventArgs(m_Col));
                }
            }
        }

        /// <summary>
        /// Sets the sort column and order without toggling the existing order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="order">The sort order.</param>
        public void SetSort(int column, SortOrder order)
        {
            if (column < 0 || column >= m_View.Columns.Count) return;

            m_Col = column;
            if (order == SortOrder.None)
            {
                m_ColOrder[m_Col] = 0;
            }
            else if (order == SortOrder.Ascending)
            {
                m_ColOrder[m_Col] = 1;
            }
            else
            {
                m_ColOrder[m_Col] = -1;
            }

            m_View.Sort();
            ListViewSortGlyph.SetSortIcon(m_View, m_Col, order);
            SortOrderChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region    ColumnClick Handler
        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            ListView LV = (ListView)sender;   // simplify code a bit -- will throw exception if sender is not a ListView
                                              // Check that this instance of ListViewColumnSorter is still the operative one
                                              // if Me is not the operative ListViewColumnSorter, then remove this instance's Handler and exit
                                              // Debug.WriteLine("LVSorter ColumnClick on " & e.Column)
            if (LV.ListViewItemSorter is null || !ReferenceEquals(LV.ListViewItemSorter, this))
            {
                LV.ColumnClick -= ListView_ColumnClick;
                return;
            }
            m_Col = e.Column;

            if (m_ColOrder.Length == 0) return;
            
            if (m_ColOrder[m_Col] == 0)
            {
                m_ColOrder[m_Col] = 1;
            }
            else
            {
                m_ColOrder[m_Col] *= -1;
            }
            LV.Sort();
            SortOrder Order;

            if (m_ColOrder.Length == 0) return;

            if (m_ColOrder[m_Col] > 0)
            {
                Order = SortOrder.Ascending;
            }
            else
            {
                Order = SortOrder.Descending;
            }
            ListViewSortGlyph.SetSortIcon(LV, m_Col, Order);
            SortOrderChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion
    }

    /// <summary>
    /// Set the Sort Glyph on a ListView Column.
    /// Obtained from <a href="http://stackoverflow.com/questions/254129/how-to-i-display-a-sort-arrow-in-the-header-of-a-list-view-column-using-c">here</a>
    /// and converted to VB.Net by JDP using
    /// <a href="http://www.developerfusion.com/tools/convert/csharp-to-vb/">The tools at DeveloperFusion.com</a>
    /// JDP also added all XML comments.
    /// 
    /// The only Public member is the Shared Sub SetIcon.
    /// </summary>
    /// <remarks>
    /// This Class is included here for the use of the LVColSorter Class. However, it may used with any ListViewColumnSorter that calls it.
    /// <para>Normally the Caller will set the Glyph to point to the direction that the Column will be Sorted on the NEXT ColumnClick</para></remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public sealed class ListViewSortGlyph
    {
        private ListViewSortGlyph()
        {
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LVCOLUMN
        {
            public int mask;
            public int cx;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPTStr)]
            public string pszText;
            public IntPtr hbm;
            public int cchTextMax;
            public int fmt;
            public int iSubItem;
            public int iImage;
            public int iOrder;
        }

        private const int HDI_FORMAT = 0x4;
        private const int HDF_SORTUP = 0x400;
        private const int HDF_SORTDOWN = 0x200;
        private const int LVM_GETHEADER = 0x101F;
        private const int HDM_GETITEM = 0x120B;
        private const int HDM_SETITEM = 0x120C;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessageLVCOLUMN(IntPtr hWnd, int Msg, IntPtr wParam, ref LVCOLUMN lPLVCOLUMN);


        // <System.Runtime.CompilerServices.Extension> _ --- This version is Not implemented as an Extension Method
        /// <summary>
        /// Set the input ordering Sort Glyph on the input Column of the input ListView, and clears the Sort Glyph from all other Columns.
        /// </summary>
        /// <param name="ListViewControl">The ListView Control containing the Column</param>
        /// <param name="ColumnIndex">The Index of the Column to receive the Sort Glyph</param>
        /// <param name="Order">The SortOrder designator of the desired Glyph</param>
        /// <remarks></remarks>
        public static void SetSortIcon(ListView ListViewControl, int ColumnIndex, SortOrder Order)
        {
            var ColumnHeader = SendMessage(ListViewControl.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);

            for (int ColumnNumber = 0, loopTo = ListViewControl.Columns.Count - 1; ColumnNumber <= loopTo; ColumnNumber++)
            {
                var ColumnPtr = new IntPtr(ColumnNumber);
                var lvColumn = new LVCOLUMN() { mask = HDI_FORMAT };
                SendMessageLVCOLUMN(ColumnHeader, HDM_GETITEM, ColumnPtr, ref lvColumn);

                if (!(Order == SortOrder.None) && ColumnNumber == ColumnIndex)
                {
                    switch (Order)
                    {
                        case SortOrder.Ascending:
                            {
                                lvColumn.fmt = lvColumn.fmt & ~HDF_SORTDOWN;
                                lvColumn.fmt = lvColumn.fmt | HDF_SORTUP;
                                break;
                            }
                        case SortOrder.Descending:
                            {
                                lvColumn.fmt = lvColumn.fmt & ~HDF_SORTUP;
                                lvColumn.fmt = lvColumn.fmt | HDF_SORTDOWN;
                                break;
                            }
                    }
                }
                else
                {
                    lvColumn.fmt = lvColumn.fmt & ~HDF_SORTDOWN & ~HDF_SORTUP;
                }

                SendMessageLVCOLUMN(ColumnHeader, HDM_SETITEM, ColumnPtr, ref lvColumn);
            }
        }
    }
}