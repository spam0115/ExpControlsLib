using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WindowsApiLib.Util;
using ExpControlsLib;
using System.IO;
using System.Linq;
using static WindowsApiLib.Shell.ShellAPI;
using System.Net.Http.Headers;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemUpdateLogicTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        private class MockShellApi : IShellApiWrapper
        {
            public delegate IntPtr LockDelegate(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent);
            public LockDelegate OnLock;
            
            public delegate int GetRealIDLDelegate(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal);
            public GetRealIDLDelegate OnGetRealIDL;
            
            public int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne) => 0;
            public bool SHChangeNotifyDeregister(int hNotify) => true;
            public IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent) 
                => OnLock?.Invoke(hChange, dwProcId, ref pppidl, ref plEvent) ?? IntPtr.Zero;
            public int SHChangeNotification_Unlock(IntPtr hLock) => 1;

            public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam) => true;

        }

        private class MockFileSystem : IFileSystem
        {
            public List<IFileSystemEntry> Files = new List<IFileSystemEntry>();
            public IEnumerable<IFileInfo> GetFiles(string path) => Enumerable.Empty<IFileInfo>();
            public IEnumerable<IFileSystemEntry> GetFileSystemInfos(string path) => Files;
        }

        private class MockFileInfo : IFileInfo
        {
            public string Name { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        private class MockFileSystemEntry : IFileSystemEntry
        {
            public string Name { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        //private class MockShellItemFactory : IShellItemFactoryWrapper
        //{
        //    public List<IntPtr> Pidls = new List<IntPtr>();
        //    public List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags) => Pidls;
        //    public CShellItem Create(IntPtr pidl, CShellItem parent = null)
        //    {
        //        var csi = new CShellItem();
        //        csi.m_Pidl = MockPidl.Clone(pidl);
        //        csi.m_Parent = parent;
        //        return csi;
        //    }
        //    public CShellItem FindOrAdd(IntPtr pidl) => Create(pidl);
        //    public string GetFullPath(CShellItem csi) => "C:\\MockPath\\" + csi.DisplayName;
        //}

        private int _pidlCounter = 0;
        private IntPtr CreateValidPidl()
        {
            _pidlCounter++;
            return MockPidl.PathToPidl($"Folder{_pidlCounter}");
        }

        [TestMethod]
        public async Task TestMockHandleCreateNotification_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                // Find the profile item in the hierarchy (under Desktop → Drives → C:\ → Profile)
                var profileSearchPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.PROFILE));
                var profile = manager.Find(profileSearchPidl);
                Marshal.FreeCoTaskMem(profileSearchPidl);
                Assert.IsNotNull(profile, "Profile should be found in mock hierarchy");

                // Build a compound PIDL: profile + new child folder
                var childPidl = MockPidl.PathToPidl("NewFolder");
                var fullPidl = MockPidl.Concatenate(profile.PIDL, childPidl);

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = fullPidl, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.CREATE;
                    return new IntPtr(1);
                };

                mockApi.OnGetRealIDL = (IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal) =>
                {
                    ppidlReal = MockPidl.Clone(pidlSimple);
                    return 0;
                };

                bool eventRaised = false;
                logic.UpdateEvent += (s, e) => {
                    if (e.UpdateType == CShItemUpdateType.Created) eventRaised = true;
                };

                int dirsBefore = profile.DirectoriesList?.Count ?? 0;

                logic.HandleNotification(fullPidl, IntPtr.Zero);

                Assert.IsTrue(eventRaised, "Created event should be raised");
                Assert.AreEqual(dirsBefore + 1, profile.DirectoriesList?.Count, "Parent should have 1 more child in DirectoriesList");

                Marshal.FreeCoTaskMem(pNotifyStruct);
            });
        }

        [TestMethod]
        public async Task TestRemoveItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                var parent = MockShellItemFactory.CreateMockShellItem("MockParent");
                parent.Files = new CShellItemCollection(parent);

                var child = new CShellItem();
                child.Parent = parent;
                parent.Files.Add(child);

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                bool removed = logic.RemoveItem(parent, child);

                Assert.IsTrue(removed, "RemoveItem should return true");
                Assert.IsFalse(parent.Files.Contains(child), "Child should be removed from parent's file list");
            });
        }

        [TestMethod]
        public async Task TestHandleDeleteNotification_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                // Use the Windows folder and notepad.exe already in the mock hierarchy
                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                var windows = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(windows, "Windows folder should be found in mock hierarchy");

                var notepad = windows.Files.Items.FirstOrDefault(f => f.DisplayName == "notepad.exe");
                Assert.IsNotNull(notepad, "notepad.exe should exist under Windows in mock hierarchy");
                notepad.m_IsFolder = false; // notepad is a file, not a folder

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = notepad.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.DELETE;
                    return new IntPtr(1);
                };

                bool eventRaised = false;
                logic.UpdateEvent += (s, e) => {
                    if (e.UpdateType == CShItemUpdateType.Deleted) eventRaised = true;
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);

                Assert.IsTrue(eventRaised, "Deleted event should be raised");
                Assert.IsFalse(windows.Files.Contains(notepad), "notepad should be removed from Windows files");
                
                Marshal.FreeCoTaskMem(pNotifyStruct);
            });
        }

        [TestMethod]
        public async Task FolderDeletion_ReceivesRmdirNotificationAsDeletedEvent()
        {
            string folderPath = Path.Combine(
                Path.GetTempPath(),
                "ShellRmdirNotification_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folderPath);
            int callCount = 0;
            ShellController controller = null;
            CShellItem folderItem = null;
            var observedFolderEvents = new List<string>();
            object observedFolderEventsLock = new();
            var deletedEvent = new TaskCompletionSource<ShellItemUpdateEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            CShellItemUpdater.CShItemUpdateEventHandler handler = (sender, args) =>
            {
                callCount++;
                if (!args.Item.IsFolder)
                {
                    return;
                }

                string eventDescription = $"{args.UpdateType}: {args.Item.FullPath}";
                lock (observedFolderEventsLock)
                {
                    observedFolderEvents.Add(eventDescription);
                }

                if (args.UpdateType == CShItemUpdateType.Deleted &&
                    (ReferenceEquals(args.Item, folderItem) ||
                     string.Equals(args.Item.FullPath, folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    deletedEvent.TrySetResult(args);
                }
            };

            try
            {
                await Runner.EnqueueWork(() =>
                {
                    controller = ShellController.Instance;
                    controller.ShellUpdater.AllowUpdates = true;

                    var tempFolder = controller.HierachyManager.FindAndAllowExpansion(Path.GetTempPath());
                    Assert.IsNotNull(tempFolder, "The temporary folder should be present in the shell hierarchy.");
                    tempFolder.LoadFolderContents(false, true);

                    folderItem = controller.HierachyManager.FindAndAllowExpansion(folderPath);
                    Assert.IsNotNull(folderItem, "The test folder should be present in the shell hierarchy.");
                    Assert.IsTrue(folderItem.IsFolder, "The test item should be a folder.");

                    controller.ShellUpdater.UpdateEvent += handler;
                });

                // Capture PIDL BEFORE deletion (ILCreateFromPathW returns NULL for missing paths)
                // and then explicitly fire SHChangeNotify. Programmatic Directory.Delete does
                // not, by itself, guarantee that Windows broadcasts SHCNE_RMDIR to registered
                // listeners; the shell only reliably fires these when the change goes through
                // SHFileOperation/IFileOperation or is announced via SHChangeNotify.
                IntPtr rmdirPidl = WindowsApiLib.Shell.ShellAPI.ILCreateFromPathW(folderPath);
                try
                {
                    Directory.Delete(folderPath);
                    WindowsApiLib.Shell.ShellAPI.SHChangeNotify(
                        (int)WindowsApiLib.Shell.ShellAPI.SHCNE.RMDIR,
                        0x1000 /* SHCNF_IDLIST | SHCNF_FLUSH */,
                        rmdirPidl,
                        IntPtr.Zero);
                }
                finally
                {
                    if (rmdirPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(rmdirPidl);
                }

                var completed = await Task.WhenAny(
                    deletedEvent.Task,
                    Task.Delay(TimeSpan.FromSeconds(10)));

                Assert.IsGreaterThan(0, callCount,
                    $"No events were observed.  Event handling wiring is probably incorrect.");

                Assert.IsTrue(
                    deletedEvent.Task.IsCompleted,
                    $"Deleting a real folder should result in a translated Deleted event from the RMDIR notification path. Observed folder events: {string.Join(", ", observedFolderEvents)}");

                var args = await deletedEvent.Task;
                Assert.AreEqual(CShItemUpdateType.Deleted, args.UpdateType);
                Assert.IsTrue(args.Item.IsFolder);
                Assert.AreEqual(folderPath, args.Item.FullPath, true);
            }
            finally
            {
                if (controller is not null)
                    controller.ShellUpdater.UpdateEvent -= handler;

                if (Directory.Exists(folderPath))
                    Directory.Delete(folderPath, true);
            }
        }

        [TestMethod]
        public async Task TestHandleRenameItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                // DoRenameOrMove calls CShellItemFactory.Exists and ReloadInfo which
                // require real Shell PIDLs, so we use actual temp files.

                string tempBase = Path.Combine(Path.GetTempPath(), "RenameTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempBase);
                string oldFilePath = Path.Combine(tempBase, "oldname.txt");
                File.WriteAllText(oldFilePath, "test");

                try
                {
                    IntPtr dirPidl = ShellAPI.ILCreateFromPathW(tempBase);
                    IntPtr oldPidl = ShellAPI.ILCreateFromPathW(oldFilePath);

                    // Root the local hierarchy manager at the just-created temp folder rather
                    // than at the shared DesktopCSI. This isolates the test's tree walk from any
                    // stale/cached state that other parallel tests may have introduced in the
                    // singleton DesktopCSI tree (e.g. Temp folder's Directories cache not yet
                    // reflecting our brand-new subfolder), which was causing manager.Add(oldPidl)
                    // to silently fail and HandleRenameItem to skip raising the event.
                    var rootCsi = CShellItemFactory.Create(CPidl.Clone(dirPidl));
                    var manager = new CShellItemHierachyManager(CShellItemFactory.DesktopCSI, rootCsi);
                    manager.Add(oldPidl);

                    // Rename the actual file to get a real new PIDL
                    string newFilePath = Path.Combine(tempBase, "newname.txt");
                    File.Move(oldFilePath, newFilePath);
                    IntPtr newPidl = ShellAPI.ILCreateFromPathW(newFilePath);

                    var mockApi = new MockShellApi();
                    var mockFactory = new MockShellItemFactory();
                    var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                    logic.AllowUpdates = true;

                    IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                    var sns = new SHNOTIFYSTRUCT { dwItem1 = oldPidl, dwItem2 = newPidl };
                    Marshal.StructureToPtr(sns, pNotifyStruct, false);

                    mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                    {
                        pppidl = pNotifyStruct;
                        plEvent = SHCNE.RENAMEITEM;
                        return new IntPtr(1); //dummy handle
                    };

                    bool eventRaised = false;
                    logic.UpdateEvent += (s, e) => {
                        if (e.UpdateType == CShItemUpdateType.Renamed) eventRaised = true;
                    };

                    logic.HandleNotification(oldPidl, newPidl); //doesn't actually matter what is passed in

                    Assert.IsTrue(eventRaised, "Renamed event should be raised");

                    Marshal.FreeCoTaskMem(pNotifyStruct);
                    Marshal.FreeCoTaskMem(newPidl);
                }
                finally
                {
                    if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
                }
            });
        }

        [TestMethod]
        public async Task TestHandleUpdateItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                // Use the Windows folder and notepad.exe already in the mock hierarchy
                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                var windows = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(windows, "Windows folder should be found in mock hierarchy");

                var notepad = windows.Files.Items.FirstOrDefault(f => f.DisplayName == "notepad.exe");
                Assert.IsNotNull(notepad, "notepad.exe should exist under Windows in mock hierarchy");
                notepad.m_IsFolder = false; // notepad is a file, not a folder

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = notepad.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEITEM;
                    return new IntPtr(1);
                };

                bool eventRaised = false;
                logic.UpdateEvent += (s, e) => {
                    if (e.UpdateType == CShItemUpdateType.Updated) eventRaised = true;
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);

                Assert.IsTrue(eventRaised, "Updated event should be raised");
                
                Marshal.FreeCoTaskMem(pNotifyStruct);
            });
        }

        [TestMethod]
        public async Task TestSelectiveFolderUpdate_NoChanges_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();

                // Create a folder with only files, using mock PIDLs
                var folder = MockShellItemFactory.CreateMockShellItem("TestFolder");
                folder.Files = new CShellItemCollection(folder);
                folder.m_LastWriteTime = new DateTime(2024, 6, 1);
                manager.Root.Directories.Add(folder);
                folder.Parent = manager.Root;

                // Create a child with a single-segment mock PIDL (not compound)
                var childPidlBytes = MockPidlFactory.CreateMockPidlFromPath("child.txt");
                var childPidl = MockPidl.BytesToPidl(childPidlBytes);
                var child = new CShellItem();
                child.m_Pidl = childPidl;
                child.m_DisplayName = "child.txt";
                child.m_IsFolder = false;
                child.m_IsFileSystem = true;
                child.ImageIndex = 1;
                child.Parent = folder;
                child.m_LastWriteTime = DateTime.Now;
                folder.Files.Add(child);

                // Mock filesystem returns the child with an older timestamp
                var mockFileSystem = new MockFileSystem();
                mockFileSystem.Files.Add(new MockFileSystemEntry { Name = "child.txt", LastWriteTime = child.m_LastWriteTime });

                // Mock factory returns the relative PIDL matching the child
                var mockFactory = new MockShellItemFactory();
                mockFactory.Pidls.Add(MockPidl.Clone(childPidl));

                var mockApi = new MockShellApi();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, mockFileSystem, mockFactory);
                
                int count = logic.DoUpdateDir(folder);
                
                Assert.AreEqual(0, count, "Should report 0 changes when PIDLs match and timestamps are current");
                
                Marshal.FreeCoTaskMem(childPidl);
            });
        }
    }
}
