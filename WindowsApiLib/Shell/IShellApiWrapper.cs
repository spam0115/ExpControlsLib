using System;
using System.Runtime.InteropServices;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// Abstraction over Windows Shell API functions and PIDL helper operations,
    /// enabling testability and decoupling from static P/Invoke declarations.
    /// </summary>
    public interface IShellApiWrapper
    {
        /// <summary>
        /// Registers a window to receive file system and Shell change notifications.
        /// </summary>
        /// <param name="hwnd">Handle of the window that receives the notifications.</param>
        /// <param name="fSources">Flags specifying the types of events to receive (see <see cref="SHCNRF"/>).</param>
        /// <param name="fEvents">Bitmask of events to receive (see <see cref="SHCNE"/>).</param>
        /// <param name="wMsg">Message to be posted to the window when a change occurs.</param>
        /// <param name="cEntries">Number of entries in the <paramref name="pfsne"/> array.</param>
        /// <param name="pfsne">Array of <see cref="SHChangeNotifyEntry"/> structures that describe the items to receive notifications for.</param>
        /// <returns>A registration handle (SHChangeNotifyEntry ID) on success; otherwise zero.</returns>
        int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne);

        /// <summary>
        /// Unregisters a previously registered Shell change notification client.
        /// </summary>
        /// <param name="hNotify">The registration handle returned by <see cref="SHChangeNotifyRegister"/>.</param>
        /// <returns><c>true</c> if the client was successfully unregistered; otherwise <c>false</c>.</returns>
        bool SHChangeNotifyDeregister(int hNotify);

        /// <summary>
        /// Locks the shared memory associated with a Shell change notification and retrieves the PIDLs and event type.
        /// </summary>
        /// <param name="hChange">Handle to the change notification, passed as a window message <c>lParam</c>.</param>
        /// <param name="dwProcId">The process ID of the process that generated the notification.</param>
        /// <param name="pppidl">Receives a pointer to an array of two PIDLs (<see cref="SHNOTIFYSTRUCT"/>) relevant to the notification.</param>
        /// <param name="plEvent">Receives the <see cref="SHCNE"/> event ID that describes the change.</param>
        /// <returns>A lock handle to pass to <see cref="SHChangeNotification_Unlock"/>; or <see cref="IntPtr.Zero"/> on failure.</returns>
        IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent);

        /// <summary>
        /// Unlocks shared memory that was locked by <see cref="SHChangeNotification_Lock"/>.
        /// </summary>
        /// <param name="hLock">The lock handle returned by <see cref="SHChangeNotification_Lock"/>.</param>
        /// <returns>Zero on success; a nonzero value on failure.</returns>
        int SHChangeNotification_Unlock(IntPtr hLock);

        /// <summary>
        /// Places a message in the message queue of the specified window and returns without waiting for the window to process the message.
        /// </summary>
        /// <param name="hWnd">Handle of the window whose message queue is to receive the message. Use <see cref="IntPtr.Zero"/> for the current thread.</param>
        /// <param name="Msg">The message to post.</param>
        /// <param name="wParam">Additional message-specific information.</param>
        /// <param name="lParam">Additional message-specific information.</param>
        /// <returns><c>true</c> if the message was successfully posted; otherwise <c>false</c>.</returns>
        bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    }

    /// <summary>
    /// Default implementation of <see cref="IShellApiWrapper"/> that delegates to
    /// the static <see cref="ShellAPI"/> P/Invoke methods and <see cref="CPidl"/> helpers.
    /// </summary>
    public class ShellApiWrapper : IShellApiWrapper
    {
        /// <inheritdoc />
        public int SHChangeNotifyRegister(IntPtr hwnd, SHCNRF fSources, SHCNE fEvents, WM wMsg, int cEntries, SHChangeNotifyEntry[] pfsne)
        {
            return ShellAPI.SHChangeNotifyRegister(hwnd, fSources, fEvents, wMsg, cEntries, pfsne);
        }

        /// <inheritdoc />
        public bool SHChangeNotifyDeregister(int hNotify)
        {
            return ShellAPI.SHChangeNotifyDeregister(hNotify);
        }

        /// <inheritdoc />
        public IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId, ref IntPtr pppidl, ref SHCNE plEvent)
        {
            return ShellAPI.SHChangeNotification_Lock(hChange, dwProcId, ref pppidl, ref plEvent);
        }

        /// <inheritdoc />
        public int SHChangeNotification_Unlock(IntPtr hLock)
        {
            return ShellAPI.SHChangeNotification_Unlock(hLock);
        }


        /// <inheritdoc />
        public bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam)
        {
            return ShellAPI.PostMessage(hWnd, Msg, wParam, lParam);
        }

    }
}
