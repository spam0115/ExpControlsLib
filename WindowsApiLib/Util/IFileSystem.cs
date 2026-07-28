using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsApiLib.Util
{
    public interface IFileSystem
    {
        IEnumerable<IFileInfo> GetFiles(string path);

        IEnumerable<FileSystemInfo> GetFileSystemInfos(string path);

    }

    public interface IFileInfo
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

        public IEnumerable<FileSystemInfo> GetFileSystemInfos(string path)
        {
            var di = new DirectoryInfo(path);
            return di.GetFileSystemInfos();
        }

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
