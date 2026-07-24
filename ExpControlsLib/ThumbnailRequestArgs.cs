using System;
using System.Drawing;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Identifies one thumbnail request as it moves from the UI-side manager to
    /// the background provider. <see cref="RequestId"/> correlates the provider
    /// callback with the request that admitted it.
    /// </summary>
    public sealed class ThumbnailRequestArgs
    {
        public CShellItem? Item { get; set; }
        public string? FilePath { get; set; }
        public int Size { get; set; }
        public int Index { get; set; } = -1;
        /// <summary>Unique identity assigned when the manager admits the request.</summary>
        public Guid RequestId { get; set; }
    }


    /// <summary>
    /// Event arguments for thumbnail-ready notifications. The request identity
    /// allows the consumer to reject stale or already-consumed results.
    /// </summary>
    public class ThumbnailReadyEventArgs : EventArgs
    {
        //public string FilePath { get; }
        public CShellItem Item { get; }
        public Image Thumbnail { get; }
        public int Index { get; }
        public object? Tag { get; }

        public int Size { get; }
        public Guid RequestId { get; }

        public ThumbnailReadyEventArgs(
            CShellItem shellItem,
            Image thumbnail,
            int size,
            int index = -1,
            object? tag = null,
            Guid requestId = default)
        {
            Item = shellItem;
            Thumbnail = thumbnail;
            Size = size;
            Index = index;
            Tag = tag;
            RequestId = requestId;
        }
    }

}
