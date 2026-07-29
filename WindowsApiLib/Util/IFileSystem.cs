using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using WindowsApiLib.Shell;

namespace WindowsApiLib.Util
{
    public interface IFileSystem
    {
        IEnumerable<IFileInfo> GetFiles(string path);

        IEnumerable<IFileSystemEntry> GetFileSystemInfos(string path);

        void CreateFolderViaShell(string parentFolderPath);

    }

    public interface IFileInfo
    {
        string Name { get; }
        DateTime LastWriteTime { get; }
    }

    public interface IFileSystemEntry
    {
        string Name { get; }
        DateTime LastWriteTime { get; }
    }

    public class FileSystemWrapper : IFileSystem
    {
        public IEnumerable<IFileInfo> GetFiles(string path)
        {
            var di = new DirectoryInfo(path);
            foreach (var fi in di.GetFiles())
            {
                yield return new FileInfoWrapper(fi);
            }
        }

        public IEnumerable<IFileSystemEntry> GetFileSystemInfos(string path)
        {
            var di = new DirectoryInfo(path);
            foreach (var fsi in di.GetFileSystemInfos())
            {
                yield return new FileSystemEntryWrapper(fsi);
            }
        }

        public void CreateFolderViaShell(string newFolderFullName)
        {
            const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
            // FOFX_SHOWELEVATIONPROMPT | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            const uint FOF_NO_UI = 0x0004 /*FOF_SILENT*/
                                 | 0x0010 /*FOF_NOCONFIRMATION*/
                                 | 0x0400 /*FOF_NOERRORUI*/;

            var iidShellItem = typeof(IShellItem).GUID;

            (string parentFolderPath, string newFolderName) = Utils.SplitPathAndFileName(newFolderFullName);
            parentFolderPath = Utils.RemoveTrailingDirectorySeparator(parentFolderPath);

            // Must be STA + COM initialized. If you're not already on such a thread,
            // marshal via your existing StaThreadRunner:
            //   AssemblyInitializer.Runner.EnqueueWork(() => CreateFolderViaShell(...));
            var parent = ShellAPI.SHCreateItemFromParsingName(
                parentFolderPath, IntPtr.Zero, ref iidShellItem);

            IFileOperation op = (IFileOperation)new FileOperation();
            try
            {
                op.SetOperationFlags(FOF_NO_UI);
                // pszTemplateName = null => create an empty item of the given attributes.
                // FILE_ATTRIBUTE_DIRECTORY makes it a folder.
                op.NewItem(parent, FILE_ATTRIBUTE_DIRECTORY, newFolderName, null, null);
                var result = op.PerformOperations();     // <-- this is what actually does the work
                                            //     and fires SHCNE_MKDIR
            }
            finally
            {
                if (op != null)
                    Marshal.ReleaseComObject(op);
                if (parent != null)
                    Marshal.ReleaseComObject(parent);
            }
        }

    }

    public class FileSystemEntryWrapper : IFileSystemEntry
    {
        private readonly FileSystemInfo _info;

        public FileSystemEntryWrapper(FileSystemInfo info)
        {
            _info = info;
        }

        public string Name => _info.Name;
        public DateTime LastWriteTime => _info.LastWriteTime;
    }

    public class FileInfoWrapper : IFileInfo
    {
        private readonly FileInfo _fileInfo;

        public FileInfoWrapper(FileInfo fileInfo)
        {
            _fileInfo = fileInfo;
        }

        public string Name => _fileInfo.Name;
        public DateTime LastWriteTime => _fileInfo.LastWriteTime;
    }
}
