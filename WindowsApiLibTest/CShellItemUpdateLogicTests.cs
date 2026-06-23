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
            public int SHGetRealIDL(IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal)
            {
                if (OnGetRealIDL != null) return OnGetRealIDL(psf, pidlSimple, out ppidlReal);
                ppidlReal = IntPtr.Zero;
                return 0;
            }
            public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam) => true;

            public string GetPidlName(IntPtr pidl) => MockPidl.ToString(pidl);

            public (IntPtr ParentPidl, IntPtr ChildPidl) SplitPidl(IntPtr pidl)
            {
                var result = MockPidl.Split(pidl);
                return (result.ParentPidl, result.ChildPidl);
            }

            public IntPtr ConcatenatePidls(IntPtr pidl1, IntPtr pidl2) => MockPidl.Concatenate(pidl1, pidl2);

            public IntPtr TrimLastPidl(IntPtr pidl) => MockPidl.TrimLast(pidl);

            public int GetPidlSegmentCount(IntPtr pidl) => MockPidl.SegmentCount(pidl);
        }

        private class MockFileSystem : IFileSystem
        {
            public List<IFileInfo> Files = new List<IFileInfo>();
            public IEnumerable<IFileInfo> GetFiles(string path) => Files;
        }

        private class MockFileInfo : IFileInfo
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
        public async Task TestHandleCreateNotification_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = MockShellItemFactory.CreateMockHierarchyManager();
                var documents = MockShellItemFactory.CreateMockShellItem(CSIDL.MYDOCUMENTS);
                var userfolder = MockShellItemFactory.CreateMockShellItem(CSIDL.PROFILE);

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = documents.PIDL, dwItem2 = IntPtr.Zero };
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

                logic.HandleNotification(documents.PIDL, IntPtr.Zero);

                Assert.IsTrue(eventRaised, "Created event should be raised");
                Assert.AreEqual(1, userfolder.FilesList?.Count, "Parent should have 1 child in FileList");
                
                Marshal.FreeCoTaskMem(pNotifyStruct);
                Marshal.FreeCoTaskMem(documents.PIDL);
                Marshal.FreeCoTaskMem(userfolder.PIDL);
            });
        }

        [TestMethod]
        public async Task TestRemoveItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = new CShellItemHierachyManager(desktop);

                var parent = new CShellItem();
                parent.m_IsFolder = true;
                parent.Directories = new CShellItemCollection(parent);
                parent.Files = new CShellItemCollection(parent);
                //parent.FoldersInitialized = true; //uneeded - FoldersInitialized has no backing field and just test m_Directories for non-null values
                //parent.FilesInitialized = true; //uneeded - FilesInitialized has no backing field and just test m_Files for non-null values

                var child = new CShellItem();
                child.Parent = parent;
                parent.Files.Add(child);

                var logic = new CShellItemUpdateLogic<MockPidl>(manager);
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
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = new CShellItemHierachyManager(desktop);
                
                var parentPidl = CreateValidPidl();
                var parent = new CShellItem();
                parent.m_Pidl = parentPidl;
                parent.m_IsFolder = true;
                parent.Files = new CShellItemCollection(parent);
                manager.Add(parent);

                var relativeChildPidl = CreateValidPidl();
                var childPidl = MockPidl.Concatenate(parentPidl, relativeChildPidl);
                var child = new CShellItem();
                child.m_Pidl = childPidl;
                child.Parent = parent;
                parent.Files.Add(child);

                var mockApi = new MockShellApi();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = childPidl, dwItem2 = IntPtr.Zero };
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
                Assert.AreEqual(0, parent.Files.Count, "Child should be removed from parent's FileList");
                
                Marshal.FreeCoTaskMem(pNotifyStruct);
                Marshal.FreeCoTaskMem(childPidl);
                Marshal.FreeCoTaskMem(relativeChildPidl);
            });
        }

        [TestMethod]
        public async Task TestHandleRenameItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = new CShellItemHierachyManager(desktop);
                
                var parentPidl = CreateValidPidl();
                var parent = new CShellItem();
                parent.m_Pidl = parentPidl;
                parent.m_IsFolder = true;
                manager.Add(parent);

                var relativeChildPidl = CreateValidPidl();
                var oldChildPidl = MockPidl.Concatenate(parentPidl, relativeChildPidl);
                var child = new CShellItem();
                child.m_Pidl = oldChildPidl;
                child.Parent = parent;
                manager.Add(child);

                var mockApi = new MockShellApi();
                var mockFactory = new MockShellItemFactory();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi, null, mockFactory);
                logic.AllowUpdates = true;

                var relativeNewChildPidl = CreateValidPidl();
                var newChildPidl = MockPidl.Concatenate(parentPidl, relativeNewChildPidl);
                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = oldChildPidl, dwItem2 = newChildPidl };
                Marshal.StructureToPtr(sns, pNotifyStruct, false);

                mockApi.OnLock = (IntPtr h, uint id, ref IntPtr pppidl, ref SHCNE plEvent) =>
                {
                    pppidl = pNotifyStruct;
                    plEvent = SHCNE.RENAMEITEM;
                    return new IntPtr(1);
                };

                mockApi.OnGetRealIDL = (IShellFolder psf, IntPtr pidlSimple, out IntPtr ppidlReal) =>
                {
                    ppidlReal = MockPidl.Clone(pidlSimple);
                    return 0;
                };

                bool eventRaised = false;
                logic.UpdateEvent += (s, e) => {
                    if (e.UpdateType == CShItemUpdateType.Renamed) eventRaised = true;
                };

                logic.HandleNotification(IntPtr.Zero, IntPtr.Zero);

                Assert.IsTrue(eventRaised, "Renamed event should be raised");
                
                Marshal.FreeCoTaskMem(pNotifyStruct);
                Marshal.FreeCoTaskMem(oldChildPidl);
                Marshal.FreeCoTaskMem(newChildPidl);
                Marshal.FreeCoTaskMem(relativeChildPidl);
                Marshal.FreeCoTaskMem(relativeNewChildPidl);
            });
        }

        [TestMethod]
        public async Task TestHandleUpdateItem_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = new CShellItemHierachyManager(desktop);
                
                var parentPidl = CreateValidPidl();
                var parent = new CShellItem();
                parent.m_Pidl = parentPidl;
                parent.m_IsFolder = true;
                manager.Add(parent);

                var relativeItemPidl = CreateValidPidl();
                var itemPidl = MockPidl.Concatenate(parentPidl, relativeItemPidl);
                var item = new CShellItem();
                item.m_Pidl = itemPidl;
                item.Parent = parent;
                manager.Add(item);

                var mockApi = new MockShellApi();
                var logic = new CShellItemUpdateLogic<MockPidl>(manager, mockApi);
                logic.AllowUpdates = true;

                IntPtr pNotifyStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(SHNOTIFYSTRUCT)));
                var sns = new SHNOTIFYSTRUCT { dwItem1 = itemPidl, dwItem2 = IntPtr.Zero };
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
                Marshal.FreeCoTaskMem(itemPidl);
                Marshal.FreeCoTaskMem(relativeItemPidl);
            });
        }

        [TestMethod]
        public async Task TestSelectiveFolderUpdate_NoChanges_HappyPath()
        {
            await Runner.EnqueueWork(() =>
            {
                var desktop = new CShellItem();
                desktop.m_IsFolder = true;
                var manager = new CShellItemHierachyManager(desktop);
                
                var parentPidl = CreateValidPidl();
                var folder = new CShellItem();
                folder.m_Pidl = parentPidl;
                folder.m_IsFolder = true;
                folder.Directories = new CShellItemCollection(folder);
                manager.Add(folder);
                
                var relativeChildPidl = CreateValidPidl();
                var childPidl = MockPidl.Concatenate(parentPidl, relativeChildPidl);
                var child = new CShellItem();
                child.m_Pidl = childPidl;
                child.Parent = folder;
                folder.Files.Add(child);

                var mockFactory = new MockShellItemFactory();
                mockFactory.Pidls.Add(relativeChildPidl); // Same PIDL exists in folder

                var logic = new CShellItemUpdateLogic<MockPidl>(manager, null, null, mockFactory);
                
                int count = logic.DoUpdateDir(folder);
                
                Assert.AreEqual(0, count, "Should report 0 changes when PIDLs match");
                
                Marshal.FreeCoTaskMem(childPidl);
                Marshal.FreeCoTaskMem(relativeChildPidl);
            });
        }
    }
}
