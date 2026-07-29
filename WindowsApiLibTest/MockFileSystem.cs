using System;
using System.Collections.Generic;
using System.Text;
using WindowsApiLib.Util;

namespace WindowsApiLibTest
{
    public class MockFileSystem : IFileSystem
    {
        public List<IFileSystemEntry> Files = new List<IFileSystemEntry>();
        public IEnumerable<IFileInfo> GetFiles(string path) => Enumerable.Empty<IFileInfo>();
        public IEnumerable<IFileSystemEntry> GetFileSystemInfos(string path) => Files;
        public void CreateFolderViaShell(string parentFolderPath)
        {
            throw new NotImplementedException("MockFileSystem.CreateFolderViaShell is not implemnted.");
        }
    }

    public class MockFileInfo : IFileInfo
    {
        public string Name { get; set; }
        public DateTime LastWriteTime { get; set; }
    }

    public class MockFileSystemEntry : IFileSystemEntry
    {
        public string Name { get; set; }
        public DateTime LastWriteTime { get; set; }
    }

}
