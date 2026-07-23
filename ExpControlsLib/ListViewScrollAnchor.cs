using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    internal static class ListViewScrollAnchor
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(
            IntPtr hWnd,
            out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public readonly record struct Anchor(
            int ItemIndex,
            double HiddenFraction);

        public static Anchor? Capture(ListView listView)
        {
            int itemCount = listView.VirtualMode
                ? listView.VirtualListSize
                : listView.Items.Count;

            if (itemCount == 0)
                return null;

            int viewportTop = GetItemsViewportTop(listView);

            int bestIndex = -1;
            Rectangle bestRect = Rectangle.Empty;

            int index = -1;

            while (true)
            {
                index = SendMessage(
                    listView.Handle,
                    LVM_GETNEXTITEM,
                    new IntPtr(index),
                    new IntPtr(LVNI_VISIBLEONLY)).ToInt32();

                if (index < 0)
                    break;

                Rectangle rect;

                try
                {
                    rect = listView.GetItemRect(
                        index,
                        ItemBoundsPortion.Entire);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                // Choose the uppermost visible item, and then the leftmost
                // item when several thumbnails occupy the same row.
                if (bestIndex < 0 ||
                    rect.Top < bestRect.Top ||
                    (rect.Top == bestRect.Top && rect.Left < bestRect.Left))
                {
                    bestIndex = index;
                    bestRect = rect;
                }
            }

            if (bestIndex < 0)
                return null;

            double hiddenFraction = 0;

            if (bestRect.Height > 0 && bestRect.Top < viewportTop)
            {
                hiddenFraction =
                    (viewportTop - bestRect.Top) /
                    (double)bestRect.Height;

                hiddenFraction = Math.Clamp(
                    hiddenFraction,
                    0.0,
                    1.0);
            }

            return new Anchor(bestIndex, hiddenFraction);
        }

        public static void Restore(
            ListView listView,
            Anchor anchor)
        {
            int itemCount = listView.VirtualMode
                ? listView.VirtualListSize
                : listView.Items.Count;

            if (itemCount == 0)
                return;

            int index = Math.Clamp(
                anchor.ItemIndex,
                0,
                itemCount - 1);

            // FALSE means the item should be entirely visible.
            SendMessage(
                listView.Handle,
                LVM_ENSUREVISIBLE,
                new IntPtr(index),
                IntPtr.Zero);

            int viewportTop = GetItemsViewportTop(listView);

            // Two passes compensate for report-view row rounding
            // and scrollbar clamping near the end of the list.
            for (int pass = 0; pass < 2; pass++)
            {
                Rectangle rect = listView.GetItemRect(
                    index,
                    ItemBoundsPortion.Entire);

                int clippedPixels = (int)Math.Round(
                    anchor.HiddenFraction * rect.Height);

                int desiredTop = viewportTop - clippedPixels;

                // If the item is below desiredTop, scroll downward.
                int dy = rect.Top - desiredTop;

                if (dy == 0)
                    break;

                SendMessage(
                    listView.Handle,
                    LVM_SCROLL,
                    IntPtr.Zero,
                    new IntPtr(dy));
            }
        }

        private static int GetItemsViewportTop(ListView listView)
        {
            if (listView.View != View.Details ||
                listView.HeaderStyle == ColumnHeaderStyle.None)
            {
                return 0;
            }

            IntPtr header = SendMessage(
                listView.Handle,
                LVM_GETHEADER,
                IntPtr.Zero,
                IntPtr.Zero);

            if (header == IntPtr.Zero ||
                !GetWindowRect(header, out RECT rect))
            {
                return 0;
            }

            Point headerBottom = listView.PointToClient(
                new Point(rect.Left, rect.Bottom));

            return headerBottom.Y;
        }
    }
}