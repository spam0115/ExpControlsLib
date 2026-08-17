using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using WindowsApiLib;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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

        [TestMethod]
        public async Task UpdateDir_StaleInitializedFolder_RefreshesImmediatelyAndClearsDirty()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory)
                {
                    AllowUpdates = true
                };

                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                var folder = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(folder, "Expected mock Windows folder in hierarchy.");

                folder.Directories = new CShellItemCollection(folder);
                folder.DirsCollectionTimestamp = DateTime.Now - TimeSpan.FromSeconds(ShellController.FolderTimeout + 1);

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = folder.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEDIR;
                    return new IntPtr(1);
                };

                int updateDirEventCount = 0;
                logic.UpdateEvent += (s, e) =>
                {
                    if (e.UpdateType == CShItemUpdateType.UpdateDir && ReferenceEquals(e.Item, folder))
                    {
                        updateDirEventCount++;
                    }
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);

                Assert.IsFalse(folder.IsDirty, "Dirty flag should be cleared after immediate stale refresh.");
                Assert.AreEqual(1, updateDirEventCount, "Immediate stale refresh should raise one folder-level UpdateDir event.");

                Marshal.FreeCoTaskMem(pNotifyStruct);
                logic.DisposeDirtyFolderRefreshTimers();
            });
        }

        [TestMethod]
        public async Task UpdateDir_FreshInitializedFolder_DefersRefreshThenClearsDirty()
        {
            var updateDirRaised = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CShellItem? folder = null;
            IntPtr pNotifyStruct = IntPtr.Zero;
            CShellItemUpdateLogic<MockPidl>? logic = null;

            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory)
                {
                    AllowUpdates = true
                };

                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                folder = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(folder, "Expected mock Windows folder in hierarchy.");

                folder.Directories = new CShellItemCollection(folder);
                folder.DirsCollectionTimestamp = DateTime.Now - TimeSpan.FromSeconds(Math.Max(1, ShellController.FolderTimeout - 1));

                pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = folder.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEDIR;
                    return new IntPtr(1);
                };

                logic.UpdateEvent += (s, e) =>
                {
                    if (e.UpdateType == CShItemUpdateType.UpdateDir && ReferenceEquals(e.Item, folder))
                    {
                        updateDirRaised.TrySetResult(true);
                    }
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);
                Assert.IsTrue(folder.IsDirty, "Folder should stay dirty until deferred refresh executes.");
            });

            var finished = await Task.WhenAny(updateDirRaised.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(updateDirRaised.Task, finished, "Deferred UPDATEDIR refresh did not execute within expected time.");
            Assert.IsFalse(folder!.IsDirty, "Dirty flag should be cleared after deferred refresh executes.");

            if (pNotifyStruct != IntPtr.Zero) Marshal.FreeCoTaskMem(pNotifyStruct);
            logic!.DisposeDirtyFolderRefreshTimers();
        }

        [TestMethod]
        public async Task UpdateDir_FreshInitializedFolder_CoalescesMultipleNotifications()
        {
            var updateDirCount = 0;
            CShellItem? folder = null;
            IntPtr pNotifyStruct = IntPtr.Zero;
            CShellItemUpdateLogic<MockPidl>? logic = null;

            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory)
                {
                    AllowUpdates = true
                };

                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                folder = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(folder, "Expected mock Windows folder in hierarchy.");

                folder.Directories = new CShellItemCollection(folder);
                folder.DirsCollectionTimestamp = DateTime.Now - TimeSpan.FromSeconds(Math.Max(1, ShellController.FolderTimeout - 1));

                pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = folder.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEDIR;
                    return new IntPtr(1);
                };

                logic.UpdateEvent += (s, e) =>
                {
                    if (e.UpdateType == CShItemUpdateType.UpdateDir && ReferenceEquals(e.Item, folder))
                    {
                        Interlocked.Increment(ref updateDirCount);
                    }
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);
                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);
            });

            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, updateDirCount, "Multiple UPDATEDIR notifications should coalesce to one deferred folder refresh.");
            Assert.IsFalse(folder!.IsDirty, "Dirty flag should be cleared after the coalesced deferred refresh.");

            if (pNotifyStruct != IntPtr.Zero) Marshal.FreeCoTaskMem(pNotifyStruct);
            logic!.DisposeDirtyFolderRefreshTimers();
        }

        [TestMethod]
        public async Task UpdateDir_WithMarshalCallback_PostsDeferredRefreshWithoutProcessingOnTimerThread()
        {
            var deferredRefreshPosted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var updateDirRaised = false;
            CShellItem? folder = null;
            IntPtr pNotifyStruct = IntPtr.Zero;
            CShellItemUpdateLogic<MockPidl>? logic = null;

            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                logic = new CShellItemUpdateLogic<MockPidl>(
                    manager,
                    mockApi,
                    null,
                    mockFactory,
                    key => { deferredRefreshPosted.TrySetResult(key); })
                {
                    AllowUpdates = true
                };

                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                folder = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(folder, "Expected mock Windows folder in hierarchy.");

                folder.Directories = new CShellItemCollection(folder);
                folder.DirsCollectionTimestamp = DateTime.Now - TimeSpan.FromSeconds(Math.Max(1, ShellController.FolderTimeout - 1));

                pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = folder.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEDIR;
                    return new IntPtr(1);
                };

                logic.UpdateEvent += (s, e) =>
                {
                    if (e.UpdateType == CShItemUpdateType.UpdateDir && ReferenceEquals(e.Item, folder))
                    {
                        updateDirRaised = true;
                    }
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);
            });

            var posted = await Task.WhenAny(deferredRefreshPosted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(deferredRefreshPosted.Task, posted, "Timer should post the deferred refresh key through the marshal callback.");
            Assert.IsFalse(updateDirRaised, "Timer callback should not process the refresh directly when a marshal callback is supplied.");
            Assert.IsTrue(folder!.IsDirty, "Folder should remain dirty until the posted refresh is processed on the updater thread.");

            logic!.ProcessDeferredDirtyFolderRefresh(await deferredRefreshPosted.Task);

            Assert.IsTrue(updateDirRaised, "Processing the posted key should raise the folder-level UpdateDir event.");
            Assert.IsFalse(folder.IsDirty, "Processing the posted key should clear the dirty flag.");

            if (pNotifyStruct != IntPtr.Zero) Marshal.FreeCoTaskMem(pNotifyStruct);
            logic.DisposeDirtyFolderRefreshTimers();
        }

        [TestMethod]
        public async Task UpdateDir_InitializedCollectionWithNullTimestamp_InitializesTimestampWithoutRefreshing()
        {
            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory)
                {
                    AllowUpdates = true
                };

                var windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                var folder = manager.Find(windowsPidl);
                Marshal.FreeCoTaskMem(windowsPidl);
                Assert.IsNotNull(folder, "Expected mock Windows folder in hierarchy.");

                folder.Directories = new CShellItemCollection(folder);
                folder.DirsCollectionTimestamp = null;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = folder.PIDL, dwItem2 = IntPtr.Zero };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.UPDATEDIR;
                    return new IntPtr(1);
                };

                int updateDirCount = 0;
                logic.UpdateEvent += (s, e) =>
                {
                    if (e.UpdateType == CShItemUpdateType.UpdateDir && ReferenceEquals(e.Item, folder))
                    {
                        updateDirCount++;
                    }
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);

                Assert.IsNotNull(folder.DirsCollectionTimestamp, "Initialized folder collection should receive a missing timestamp.");
                Assert.AreEqual(0, updateDirCount, "Missing timestamp should not force an immediate refresh.");
                Assert.IsTrue(folder.IsDirty, "Folder should remain dirty until the timeout elapses.");

                Marshal.FreeCoTaskMem(pNotifyStruct);
                logic.DisposeDirtyFolderRefreshTimers();
            });
        }

        [TestMethod]
        public async Task DoUpdateDir_DifferentFolders_CanRunConcurrently()
        {
            CShellItem? folderA = null;
            CShellItem? folderB = null;
            CShellItemUpdateLogic<MockPidl>? logic = null;
            BlockingShellItemFactory? blockingFactory = null;

            await Runner.EnqueueWork(() =>
            {
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                IntPtr windowsPidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.WINDOWS));
                IntPtr profilePidl = MockPidl.BytesToPidl(MockPidlFactory.CreateMockPidl(CSIDL.PROFILE));

                folderA = manager.Find(windowsPidl);
                folderB = manager.Find(profilePidl);

                Marshal.FreeCoTaskMem(windowsPidl);
                Marshal.FreeCoTaskMem(profilePidl);

                Assert.IsNotNull(folderA, "Expected first folder in mock hierarchy.");
                Assert.IsNotNull(folderB, "Expected second folder in mock hierarchy.");

                folderA.Files = new CShellItemCollection(folderA);
                folderB.Files = new CShellItemCollection(folderB);

                blockingFactory = new BlockingShellItemFactory();
                logic = new CShellItemUpdateLogic<MockPidl>(
                    manager,
                    new MockShellApi(),
                    new MockFileSystem(),
                    blockingFactory);
            });

            try
            {
                var taskA = Task.Run(() => logic!.DoUpdateDir(folderA!));
                var taskB = Task.Run(() => logic!.DoUpdateDir(folderB!));

                bool bothEntered = SpinWait.SpinUntil(
                    () => Volatile.Read(ref blockingFactory!.CallCount) >= 2,
                    TimeSpan.FromSeconds(2));

                blockingFactory!.Release();

                await Task.WhenAll(taskA, taskB);

                Assert.IsTrue(bothEntered, "Both folder updates should enter shell enumeration concurrently.");
                Assert.AreEqual(2, Volatile.Read(ref blockingFactory.CallCount), "Each folder should run its own DoUpdateDir path.");
            }
            finally
            {
                blockingFactory?.Dispose();
            }
        }

        private sealed class BlockingShellItemFactory : IShellItemFactoryWrapper, IDisposable
        {
            private readonly ManualResetEventSlim _releaseGate = new(false);
            public int CallCount;

            public List<IntPtr> GetPidlsOfFolder(CShellItem csi, SHCONTF flags)
            {
                Interlocked.Increment(ref CallCount);
                _releaseGate.Wait(TimeSpan.FromSeconds(3));
                return new List<IntPtr>();
            }

            public CShellItem Create(IntPtr pidl, CShellItem parent = null)
            {
                var csi = new CShellItem();
                csi.m_Pidl = MockPidl.Clone(pidl);
                csi.Parent = parent;
                csi.m_IsFolder = true;
                return csi;
            }

            public string GetFullPath(CShellItem csi)
            {
                return csi.FullPath ?? csi.DisplayName;
            }

            public void Release()
            {
                _releaseGate.Set();
            }

            public void Dispose()
            {
                _releaseGate.Dispose();
            }
        }
    }
}
