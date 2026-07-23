using System;
using System.Drawing;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    /// <summary>
    /// Identifies one thumbnail request as it moves from the UI-side manager to
    /// the background provider. The generation prevents results from a previous
    /// folder from being applied, while <see cref="RequestId"/> correlates the
    /// provider callback with the request that admitted it.
    /// </summary>
    public sealed class ThumbnailRequestArgs
    {
        public CShellItem? Item { get; set; }
        public string? FilePath { get; set; }
        public int Size { get; set; }
        public int Index { get; set; } = -1;
        /// <summary>Folder/cache generation associated with the request.</summary>
        public int Generation { get; set; }

        /// <summary>Unique identity assigned when the manager admits the request.</summary>
        public Guid RequestId { get; set; }
    }


    /// <summary>
    /// Event arguments for thumbnail-ready notifications. The generation and
    /// request identity allow the consumer to reject results that belong to an
    /// obsolete folder or a request that has already been consumed.
    /// </summary>
    public class ThumbnailReadyEventArgs : EventArgs
    {
        //public string FilePath { get; }
        public CShellItem Item { get; }
        public Image Thumbnail { get; }
        public int Index { get; }
        public object? Tag { get; }

        public int Size { get; }
        public int Generation { get; }
        public Guid RequestId { get; }

        public ThumbnailReadyEventArgs(
            CShellItem shellItem,
            Image thumbnail,
            int size,
            int index = -1,
            object? tag = null,
            int generation = 0,
            Guid requestId = default)
        {
            Item = shellItem;
            Thumbnail = thumbnail;
            Size = size;
            Index = index;
            Tag = tag;
            Generation = generation;
            RequestId = requestId;
        }
    }

}
