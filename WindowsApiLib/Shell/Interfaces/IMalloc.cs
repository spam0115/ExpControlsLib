using System;
using System.Runtime.InteropServices;

namespace WindowsApiLib.Shell
{
    // Not needed in .Net - use Marshal Class
    [ComImport()]
    [Guid("00000002-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMalloc
    {
        // Allocates a block of memory.
        // Return value: a pointer to the allocated memory block.
        [PreserveSig()]
        IntPtr Alloc(int cb);
        // Size, in bytes, of the memory block to be allocated.

        // Changes the size of a previously allocated memory block.
        // Return value:  Reallocated memory block 
        [PreserveSig()]
        IntPtr Realloc(IntPtr pv, int cb);

        // Frees a previously allocated block of memory.
        [PreserveSig()]
        void Free(IntPtr pv); // Pointer to the memory block to be freed.

        // This method returns the size (in bytes) of a memory block previously allocated with 
        // IMalloc::Alloc or IMalloc::Realloc.
        // Return value: The size of the allocated memory block in bytes 
        [PreserveSig()]
        int GetSize(IntPtr pv); // Pointer to the memory block for which the size is requested.

        // This method determines whether this allocator was used to allocate the specified block of memory.
        // Return value: 1 - allocated 0 - not allocated by this IMalloc instance. 
        [PreserveSig()]
        short DidAlloc(IntPtr pv);
        // Pointer to the memory block

        // This method minimizes the heap as much as possible by releasing unused memory to the operating system, 
        // coalescing adjacent free blocks and committing free pages.
        [PreserveSig()]
        void HeapMinimize();
    }
}