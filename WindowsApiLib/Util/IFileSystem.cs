using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsApiLib.Util
{
    public interface IFileSystem
    {
        IEnumerable<IFileInfo> GetFiles(string path);

        IEnumerable<IFileSystemEntry> GetFileSystemInfos(string path);

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
