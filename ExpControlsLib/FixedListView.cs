using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace ExpControlsLib
{
    public class FixedListView : ListView
    {
        private ListViewItem _anchorItem;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var hit = this.HitTest(e.Location);
            if (hit.Item != null)
            {
                if ((ModifierKeys & Keys.Shift) == 0)
                    _anchorItem = hit.Item; // update anchor only on non-shift clicks
            }
            base.OnMouseDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDOWN && (ModifierKeys & Keys.Shift) != 0
                && (this.View == View.LargeIcon || this.View == View.SmallIcon))
            {
                int x = (short)(m.LParam.ToInt32() & 0xFFFF);
                int y = (short)(m.LParam.ToInt32() >> 16);
                var hit = this.HitTest(x, y);
                if (hit.Item != null)
                {
                    HandleShiftClick(hit.Item);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void HandleShiftClick(ListViewItem clickedItem)
        {
            int anchorIndex = _anchorItem?.Index ?? 0;
            int clickedIndex = clickedItem.Index;

            int start = Math.Min(anchorIndex, clickedIndex);
            int end = Math.Max(anchorIndex, clickedIndex);

            this.BeginUpdate();
            for (int i = 0; i < this.Items.Count; i++)
                this.Items[i].Selected = (i >= start && i <= end);
            this.EndUpdate();
        }
    }
}
