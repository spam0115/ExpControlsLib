using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace ExpControlsLib
{
    /// <summary>
    /// Describes which cached Shell child collections should be loaded by
    /// <see cref="ShellDirectoryLoader"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ShellDirectoryLoadOptions
    {
        public bool IncludeFolders { get; init; } = true;

        public bool IncludeFiles { get; init; } = true;

        public bool IncludeHidden { get; init; }
    }

    /// <summary>
    /// Immutable snapshot of the child Shell items observed for one folder.
    /// The collections are copies and may be sorted or filtered by a control
    /// without mutating the hierarchy manager's cached collections.
    /// </summary>
    
    [SupportedOSPlatform("windows")]
    internal sealed class ShellDirectorySnapshot
    {
        public ShellDirectorySnapshot(
            CShellItem folder,
            IReadOnlyList<CShellItem> folders,
            IReadOnlyList<CShellItem> files)
        {
            Folder = folder;
            Folders = folders;
            Files = files;
        }

        public CShellItem Folder { get; }

        public IReadOnlyList<CShellItem> Folders { get; }

        public IReadOnlyList<CShellItem> Files { get; }
    }

    /// <summary>
    /// Performs the shared, UI-independent portion of directory loading.
    /// Callers must invoke <see cref="Load"/> from an STA worker because Shell
    /// enumeration and the hierarchy cache may access COM objects.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ShellDirectoryLoader
    {
        private readonly ShellController _shellController;

        public ShellDirectoryLoader(ShellController shellController)
        {
            _shellController = shellController ?? throw new ArgumentNullException(nameof(shellController));
        }

        public ShellDirectorySnapshot? Load(
            CShellItem folder,
            ShellDirectoryLoadOptions? options,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(folder);
            options ??= new ShellDirectoryLoadOptions();

            cancellationToken.ThrowIfCancellationRequested();

            var canonicalFolder = _shellController.HierachyManager.FindAndAllowExpansion(folder);
            if (canonicalFolder is null || !canonicalFolder.IsFolder)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var flags = SHCONTF.EMPTY;
            if (options.IncludeFolders)
            {
                flags |= SHCONTF.FOLDERS;
            }

            if (options.IncludeFiles)
            {
                flags |= SHCONTF.NONFOLDERS;
            }

            if (options.IncludeHidden)
            {
                flags |= SHCONTF.INCLUDEHIDDEN;
            }

            if (flags != SHCONTF.EMPTY)
            {
                _shellController.EnsureChildrenPopulatedAndRecent(canonicalFolder, flags);
            }

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<CShellItem> folders = options.IncludeFolders
                ? new List<CShellItem>(CopyItems(canonicalFolder.Directories, cancellationToken))
                : Array.Empty<CShellItem>();

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<CShellItem> files = options.IncludeFiles
                ? new List<CShellItem>(CopyItems(canonicalFolder.Files, cancellationToken))
                : Array.Empty<CShellItem>();

            return new ShellDirectorySnapshot(canonicalFolder, folders, files);
        }

        private static List<CShellItem> CopyItems(
            IEnumerable<CShellItem>? items,
            CancellationToken cancellationToken)
        {
            var copy = new List<CShellItem>();
            if (items is null)
            {
                return copy;
            }

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                copy.Add(item);
            }

            return copy;
        }
    }
}
