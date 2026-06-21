using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;
using System;
using System.Reflection;
using System.Threading.Tasks;
using ExpControlsLib;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    [TestClass]
    public class CShellItemUpdaterTests
    {
        private StaThreadRunner Runner => AssemblyInitializer.Runner;

        private CShellItemUpdater CreateUpdater(uint eventFlags = 0)
        {
            var manager = MockShellItemFactory.CreateMockHierarchyManager();
            return new CShellItemUpdater(manager, eventFlags);
        }

        private ShellItemUpdateEventArgs CreateEventArgs(CShItemUpdateType type = CShItemUpdateType.Updated)
        {
            var item = MockShellItemFactory.CreateMockShellItem(CSIDL.MYDOCUMENTS);
            return new ShellItemUpdateEventArgs(item, type);
        }

        #region Constructor / OS Interaction

        [TestMethod]
        public async Task Constructor_HandleCreated()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                Assert.AreNotEqual(IntPtr.Zero, updater.Handle, "Handle should be created on background thread");
            });
        }

        [TestMethod]
        public async Task Constructor_RegistersShellNotification()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                var field = typeof(CShellItemUpdater).GetField("m_notifyId", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(field, "m_notifyId field should exist");
                int notifyId = (int)field.GetValue(updater);
                Assert.IsTrue(notifyId >= 0, "m_notifyId should be non-negative after construction");
            });
        }

        #endregion

        #region AllowUpdates Property

        [TestMethod]
        public async Task AllowUpdates_DefaultIsFalse()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                Assert.IsFalse(updater.AllowUpdates, "AllowUpdates should default to false");
            });
        }

        [TestMethod]
        public async Task AllowUpdates_SetAndGet()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                updater.AllowUpdates = true;
                Assert.IsTrue(updater.AllowUpdates, "AllowUpdates should be true after setting");

                updater.AllowUpdates = false;
                Assert.IsFalse(updater.AllowUpdates, "AllowUpdates should be false after resetting");
            });
        }

        #endregion

        #region RaiseUpdateEvent

        [TestMethod]
        public async Task RaiseUpdateEvent_NoHandlers_DoesNotThrow()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                var args = CreateEventArgs();
                updater.RaiseUpdateEvent(this, args);
            });
        }

        [TestMethod]
        public async Task RaiseUpdateEvent_WithHandler_EventIsRaised()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                bool eventRaised = false;
                updater.UpdateEvent += (s, e) => { eventRaised = true; };

                updater.RaiseUpdateEvent(this, CreateEventArgs());

                Assert.IsTrue(eventRaised, "Event handler should have been invoked");
            });
        }

        [TestMethod]
        public async Task RaiseUpdateEvent_HandlerThrows_DoesNotPropagate()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                updater.UpdateEvent += (s, e) => throw new InvalidOperationException("test");

                updater.RaiseUpdateEvent(this, CreateEventArgs());
            });
        }

        [TestMethod]
        public async Task RaiseUpdateEvent_WithMultipleHandlers_AllInvoked()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                int callCount = 0;
                updater.UpdateEvent += (s, e) => { callCount++; };
                updater.UpdateEvent += (s, e) => { callCount++; };

                updater.RaiseUpdateEvent(this, CreateEventArgs());

                Assert.AreEqual(2, callCount, "Both handlers should be invoked");
            });
        }

        #endregion

        #region OnMoveItem Null Guards

        [TestMethod]
        public async Task OnMoveItem_NullItem_DoesNotThrow()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                updater.OnMoveItem((CShellItem)null, "C:\\some\\path");
            });
        }

        [TestMethod]
        public async Task OnMoveItem_NullNewPath_DoesNotThrow()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                var item = MockShellItemFactory.CreateMockShellItem(CSIDL.MYDOCUMENTS);
                updater.OnMoveItem(item, (string)null);
            });
        }

        [TestMethod]
        public async Task OnMoveItem_NullNewParent_DoesNotThrow()
        {
            await Runner.EnqueueWork(() =>
            {
                using var updater = CreateUpdater();
                var item = MockShellItemFactory.CreateMockShellItem(CSIDL.MYDOCUMENTS);
                updater.OnMoveItem(item, (CShellItem)null);
            });
        }

        #endregion

        #region Dispose

        [TestMethod]
        public async Task Dispose_CleansUpHandle()
        {
            await Runner.EnqueueWork(() =>
            {
                var updater = CreateUpdater();
                Assert.AreNotEqual(IntPtr.Zero, updater.Handle, "Handle should exist before dispose");

                updater.Dispose();

                Assert.AreEqual(IntPtr.Zero, updater.Handle, "Handle should be zero after dispose");
            });
        }

        [TestMethod]
        public async Task Dispose_MultipleCallsDoesNotThrow()
        {
            await Runner.EnqueueWork(() =>
            {
                var updater = CreateUpdater();
                updater.Dispose();
                updater.Dispose();
            });
        }

        #endregion
    }
}
