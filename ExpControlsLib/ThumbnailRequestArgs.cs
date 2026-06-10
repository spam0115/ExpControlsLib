using System;
using System.Drawing;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    public sealed class ThumbnailRequestArgs
    {
        public CShellItem? Item { get; set; }
        public string? FilePath { get; set; }
        public int Size { get; set; }
        public int Index { get; set; } = -1;
        public int Generation { get; set; }
    }


    /// <summary>
    /// Event arguments for thumbnail ready notifications
    /// </summary>
    public class ThumbnailReadyEventArgs : EventArgs
    {
        //public string FilePath { get; }
        public CShellItem Item { get; }
        public Image Thumbnail { get; }
        public int Index { get; }
        public object? Tag { get; }

        public int Size { get; }

        public ThumbnailReadyEventArgs(CShellItem shellItem, Image thumbnail, int size, int index = -1, object? tag = null)
        {
            Item = shellItem;
            Thumbnail = thumbnail;
            Size = size;
            Index = index;
            Tag = tag;
        }
    }

}
