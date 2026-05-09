using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// A thread-safe memory pool for CoTaskMem allocations. Reduces allocation overhead
    /// by reusing previously allocated memory blocks instead of allocating and freeing
    /// repeatedly from the operating system.
    /// </summary>
    public class CoTaskMemPool : IDisposable
    {
        private readonly int _blockSize;
        private readonly Stack<IntPtr> _availableBlocks;
        private readonly HashSet<IntPtr> _allAllocatedBlocks;
        private readonly object _lockObj = new object();
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the CoTaskMemPool class.
        /// </summary>
        /// <param name="blockSize">The size in bytes of each memory block to allocate.</param>
        /// <param name="initialPoolSize">The initial number of blocks to pre-allocate (default: 1).</param>
        public CoTaskMemPool(int blockSize, int initialPoolSize = 1)
        {
            if (blockSize <= 0)
                throw new ArgumentException("Block size must be greater than 0.", nameof(blockSize));
            if (initialPoolSize < 0)
                throw new ArgumentException("Initial pool size must be non-negative.", nameof(initialPoolSize));

            _blockSize = blockSize;
            _availableBlocks = new Stack<IntPtr>(initialPoolSize);
            _allAllocatedBlocks = new HashSet<IntPtr>();

            // Pre-allocate initial blocks
            for (int i = 0; i < initialPoolSize; i++)
            {
                try
                {
                    IntPtr block = Marshal.AllocCoTaskMem(blockSize);
                    _availableBlocks.Push(block);
                    _allAllocatedBlocks.Add(block);
                }
                catch
                {
                    // If initial allocation fails, clean up and rethrow
                    FreeAllBlocks();
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets a memory block from the pool. If no blocks are available, allocates a new one.
        /// </summary>
        /// <returns>An IntPtr to a memory block of size BlockSize.</returns>
        /// <remarks>The returned memory block must be returned to the pool via <see cref="ReturnBlock"/>
        /// when no longer needed. Do not use Marshal.FreeCoTaskMem on blocks obtained from the pool.</remarks>
        public IntPtr GetBlock()
        {
            ThrowIfDisposed();

            lock (_lockObj)
            {
                if (_availableBlocks.Count > 0)
                {
                    return _availableBlocks.Pop();
                }
            }

            // No available blocks, allocate a new one
            try
            {
                IntPtr newBlock = Marshal.AllocCoTaskMem(_blockSize);
                lock (_lockObj)
                {
                    _allAllocatedBlocks.Add(newBlock);
                }
                return newBlock;
            }
            catch
            {
                throw new OutOfMemoryException("Failed to allocate CoTaskMem block.");
            }
        }

        /// <summary>
        /// Returns a memory block to the pool for reuse.
        /// </summary>
        /// <param name="block">The IntPtr to the memory block to return.</param>
        /// <remarks>The block must have been obtained from <see cref="GetBlock"/>.
        /// Returning a block that was not obtained from this pool may cause undefined behavior.</remarks>
        public void ReturnBlock(IntPtr block)
        {
            if (block == IntPtr.Zero)
                return;

            ThrowIfDisposed();

            lock (_lockObj)
            {
                if (!_allAllocatedBlocks.Contains(block))
                {
                    throw new ArgumentException("Block was not allocated by this pool.", nameof(block));
                }

                _availableBlocks.Push(block);
            }
        }

        /// <summary>
        /// Gets the size in bytes of each memory block managed by this pool.
        /// </summary>
        public int BlockSize => _blockSize;

        /// <summary>
        /// Gets the number of available blocks currently in the pool.
        /// </summary>
        public int AvailableBlockCount
        {
            get
            {
                ThrowIfDisposed();
                lock (_lockObj)
                {
                    return _availableBlocks.Count;
                }
            }
        }

        /// <summary>
        /// Gets the total number of blocks allocated by this pool (available and in use).
        /// </summary>
        public int TotalBlockCount
        {
            get
            {
                ThrowIfDisposed();
                lock (_lockObj)
                {
                    return _allAllocatedBlocks.Count;
                }
            }
        }

        /// <summary>
        /// Frees all memory blocks currently in the pool. Blocks in use will be freed when disposed.
        /// </summary>
        public void ClearPool()
        {
            ThrowIfDisposed();

            lock (_lockObj)
            {
                while (_availableBlocks.Count > 0)
                {
                    IntPtr block = _availableBlocks.Pop();
                    Marshal.FreeCoTaskMem(block);
                    _allAllocatedBlocks.Remove(block);
                }
            }
        }

        private void FreeAllBlocks()
        {
            lock (_lockObj)
            {
                while (_availableBlocks.Count > 0)
                {
                    IntPtr block = _availableBlocks.Pop();
                    Marshal.FreeCoTaskMem(block);
                }

                foreach (IntPtr block in _allAllocatedBlocks)
                {
                    try
                    {
                        Marshal.FreeCoTaskMem(block);
                    }
                    catch
                    {
                        // Silently ignore errors during cleanup
                    }
                }

                _allAllocatedBlocks.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Releases all resources used by the CoTaskMemPool.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the CoTaskMemPool and optionally releases managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Free managed resources if needed
            }

            // Free unmanaged resources
            FreeAllBlocks();
            _disposed = true;
        }

        /// <summary>
        /// Finalizer to ensure resources are freed if Dispose is not called.
        /// </summary>
        ~CoTaskMemPool()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// A scoped helper for using memory blocks from a CoTaskMemPool with automatic return on disposal.
    /// </summary>
    public class CoTaskMemPoolScope : IDisposable
    {
        private readonly CoTaskMemPool _pool;
        private IntPtr _block;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the CoTaskMemPoolScope class and obtains a block from the pool.
        /// </summary>
        /// <param name="pool">The CoTaskMemPool to obtain a block from.</param>
        public CoTaskMemPoolScope(CoTaskMemPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _block = pool.GetBlock();
        }

        /// <summary>
        /// Gets the memory block pointer.
        /// </summary>
        public IntPtr Block
        {
            get
            {
                ThrowIfDisposed();
                return _block;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Returns the block to the pool and releases resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the CoTaskMemPoolScope.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing && _block != IntPtr.Zero)
            {
                _pool.ReturnBlock(_block);
                _block = IntPtr.Zero;
            }

            _disposed = true;
        }

        /// <summary>
        /// Finalizer to ensure the block is returned to the pool.
        /// </summary>
        ~CoTaskMemPoolScope()
        {
            Dispose(false);
        }
    }
}
