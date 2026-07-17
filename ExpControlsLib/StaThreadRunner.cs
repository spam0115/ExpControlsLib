using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ExpControlsLib
{
    /// <summary>
    /// Runs queued delegates on a pool of dedicated STA (single threaded apartment) threads.
    /// </summary>
    /// <remarks>Use this class to do work on an STA thread, such as when working with certain Windows Forms,
    /// WPF, or COM APIs (e.g. Windows Shell COM) that require STA threading and will fail with MTA context. The
    /// thread is initialized in STA mode and runs a message loop to support message pumping, which is necessary for 
    /// many COM scenarios.
    /// </remarks>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class StaThreadRunner : IDisposable
    {
        private readonly BlockingCollection<IWorkItem> _queue = new();
        private readonly Thread[] _threads;
        private readonly CountdownEvent _ready;
        private readonly string _threadNamePrefix;
        private readonly object _threadStartGate = new();
        private readonly CancellationTokenSource _shutdownCts = new();
        private volatile bool _threadsStarted;
        private volatile bool _disposed;

        public int StaThreadCount => _threads.Length;

        /// <summary>
        /// Token that is canceled when <see cref="Dispose"/> is called. Work items can
        /// check this to cooperatively abort long-running operations during shutdown.
        /// </summary>
        public CancellationToken ShutdownToken => _shutdownCts.Token;

        public StaThreadRunner(int staThreadCount = 1, string threadNamePrefix = null)
        {
            if (staThreadCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(staThreadCount), "Must be >= 1.");

            _threads = new Thread[staThreadCount];
            _ready = new CountdownEvent(staThreadCount);
            _threadNamePrefix = string.IsNullOrWhiteSpace(threadNamePrefix) ? "STA Runner" : threadNamePrefix;
        }

        /// <summary>
        /// Queues work to run on the dedicated STA thread and returns a Task for its result.
        /// </summary>
        /// <typeparam name="T">The return type produced by <paramref name="work"/>.</typeparam>
        /// <param name="work">
        /// Delegate to execute on the STA thread. This delegate runs serially with other queued delegates.
        /// </param>
        /// <returns>
        /// A task that completes when the delegate has executed on the STA thread.
        /// The task result is the value returned by <paramref name="work"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">
        /// Thrown if the runner has already been disposed and no more work can be queued.
        /// </exception>
        /// <remarks>
        /// Use this for COM/Shell operations that must run in STA context.
        /// Any exception thrown by <paramref name="work"/> is captured and rethrown when awaiting the returned task.
        /// </remarks>
        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return EnqueueWork(_ => work(), CancellationToken.None);
        }

        /// <summary>
        /// Queues void work to run on the dedicated STA thread and returns a Task that completes when done.
        /// </summary>
        /// <param name="work">
        /// Delegate to execute on the STA thread. This delegate runs serially with other queued delegates.
        /// </param>
        /// <returns>
        /// A task that completes when <paramref name="work"/> has finished executing on the STA thread.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">
        /// Thrown if the runner has already been disposed and no more work can be queued.
        /// </exception>
        /// <remarks>
        /// Convenience overload for non-returning operations. Internally forwards to <see cref="InvokeAsync{T}(Func{T})"/>.
        /// Any exception thrown by <paramref name="work"/> is captured and rethrown when awaiting the returned task.
        /// </remarks>
        public Task EnqueueWork(Action work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return EnqueueWork(_ => work(), CancellationToken.None);
        }

        public Task<T> EnqueueWork<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            ThrowIfDisposed();

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            EnsureThreadsStarted();

            var item = new WorkItem<T>(work, cancellationToken);

            try
            {
                _queue.Add(item);
            }
            catch (InvalidOperationException)
            {
                // Adding was completed between ThrowIfDisposed and Add.
                throw new ObjectDisposedException(nameof(StaThreadRunner));
            }

            return item.Task;
        }

        public Task EnqueueWork(Action<CancellationToken> work, CancellationToken cancellationToken = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            return EnqueueWork(token =>
            {
                work(token);
                return true;
            }, cancellationToken);
        }

        /// <summary>
        /// Cancels all pending (not-yet-started) items currently in the queue.
        /// </summary>
        /// <returns>Number of items canceled.</returns>
        public int CancelPending()
        {
            ThrowIfDisposed();

            int canceled = 0;
            while (_queue.TryTake(out var item))
            {
                item.Cancel();
                canceled++;
            }

            return canceled;
        }

        private void EnsureThreadsStarted()
        {
            if (_threadsStarted)
                return;

            lock (_threadStartGate)
            {
                if (_threadsStarted)
                    return;

                ThrowIfDisposed();
                Debug.WriteLine("Starting StaThreadRunner...");

                for (int i = 0; i < _threads.Length; i++)
                {
                    int workerIndex = i;
                    var thread = new Thread(() => ThreadMain(workerIndex))
                    {
                        IsBackground = true,
                        Name = $"{_threadNamePrefix} #{workerIndex}"
                    };

                    thread.SetApartmentState(ApartmentState.STA);
                    _threads[i] = thread;
                    thread.Start();
                    Debug.WriteLine("\tthread started." + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                _ready.Wait();
                _threadsStarted = true;
            }
        }

        private void ThreadMain(int workerIndex)
        {
            int hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            bool comInitialized = hr >= 0; // S_OK or S_FALSE

            // Signal startup regardless, so constructor does not deadlock if COM init fails.
            _ready.Signal();

            try
            {
                while (!_queue.IsCompleted)
                {
                    if (_queue.TryTake(out var item, 50))
                    {
                        item.Execute();
                    }

                    // Each STA worker pumps its own message queue.
                    PumpMessages();
                }
            }
            finally
            {
                if (comInitialized)
                    CoUninitialize();
            }
        }

        private static void PumpMessages()
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Signal work items to cooperatively abort any long-running operations.
            _shutdownCts.Cancel();

            _queue.CompleteAdding();

            foreach (var thread in _threads)
            {
                if (thread == null)
                    continue;

                // Use a timeout instead of indefinite Join. If a thread is stuck in
                // COM work that needs to marshal to the UI thread (which called Dispose),
                // Join would deadlock. The timeout lets us bail out — the thread is a
                // background thread and will exit when the process terminates.
                thread.Join(TimeSpan.FromSeconds(2));
            }

            _shutdownCts.Dispose();
            _queue.Dispose();
            _ready.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StaThreadRunner));
        }

        private interface IWorkItem
        {
            void Execute();
            void Cancel();
        }

        private sealed class WorkItem<T> : IWorkItem
        {
            private readonly Func<CancellationToken, T> _work;
            private readonly CancellationToken _token;
            private readonly TaskCompletionSource<T> _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public WorkItem(Func<CancellationToken, T> work, CancellationToken token)
            {
                _work = work;
                _token = token;
            }

            public Task<T> Task => _tcs.Task;

            public void Execute()
            {
                // If already canceled while queued, skip execution.
                if (_tcs.Task.IsCompleted || _token.IsCancellationRequested)
                {
                    TrySetCanceled();
                    return;
                }

                try
                {
                    T result = _work(_token);
                    _tcs.TrySetResult(result);
                }
                catch (OperationCanceledException oce)
                {
                    if (oce.CancellationToken.CanBeCanceled)
                        _tcs.TrySetCanceled(oce.CancellationToken);
                    else
                        _tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    _tcs.TrySetException(ex);
                }
            }

            public void Cancel() => TrySetCanceled();

            private void TrySetCanceled()
            {
                if (_token.CanBeCanceled)
                    _tcs.TrySetCanceled(_token);
                else
                    _tcs.TrySetCanceled();
            }
        }

        private const int COINIT_APARTMENTTHREADED = 0x2;
        private const uint PM_REMOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(
            out MSG lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax,
            uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    }
}