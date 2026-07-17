using System.Collections;
using WindowsApiLib.Shell;

namespace WindowsApiLib
{
    /// <summary>
    /// Not all of these methods are needed for testing.  Only implment the methods you require.
    /// </summary>
    public interface ICPidl
    {
        int ItemCount { get; }
        int Length { get; }
        byte[] PidlBytes { get; }

        static abstract bool AreBytesEqual(nint pidl1, nint pidl2);
        static abstract bool AreEqual(IShellFolder parent, nint pidl1, nint pidl2);
        static abstract nint BytesToPidl(byte[] b);
        static abstract nint Clone(nint pidl);
        static abstract nint Concatenate(nint pidl1, nint pidl2);
        static abstract nint Copy(nint pidl);
        static abstract nint[] Decompose(nint pidl);
        static abstract void Dump(nint pidl);
        static abstract void DumpHex(byte[] b, int sPos = 0, int ePos = 0);
        static abstract string? GetDisplayName(nint pidl);
        static abstract string? GetDisplayNameFull(nint pidl);
        static abstract string? GetFileSystemPath(nint pidl);
        static abstract string? GetFileSystemPathFromShellParsingName(string parsingName);
        static abstract string? GetFullName(nint pidl1);
        static abstract string? GetParsingName(nint pidl);
        static abstract string? GetParsingPath(nint pidl);
        static abstract uint HashPidlFastLastFull(nint pidl);
        static abstract string HexNum(int num, int nrChrs);
        static abstract nint ILFindLastID(nint pidl);
        static abstract bool IsAncestorOf(CShellItem Item1, CShellItem Item2, bool ImmediateOnly = false);
        static abstract bool IsAncestorOf(nint pidl1, nint pidl2, bool ImmediateOnly = false);
        static abstract bool IsBinaryEqual(nint pidl1, nint pidl2);
        static abstract bool IsShellNamespaceRoot(nint pidl);
        static abstract bool IsValid(byte[] b);
        static abstract int GetPidlLength(nint pidl);
        static abstract byte[] JoinPidlBytes(byte[] b1, byte[] b2);
        static abstract nint PathToPidl(string path);
        static abstract string? ResolveShellGUID(string guidPath);
        static abstract bool ResolvesToSamePathOrName(nint pidl1, nint pidl2);
        static abstract int SegmentCount(nint pidl);
        static abstract PidlSplitResult Split(nint pidl);
        static abstract bool StartsWith(CPidl A, CPidl B);
        static abstract bool StartsWith(nint pidl1, nint pidl2);
        static abstract nint ToPidl(string path);
        static abstract string? ToString(nint pidl, bool absolute = true);
        static abstract nint TrimLast(nint pidl);
        object[] Decompose();
        IEnumerator GetEnumerator();
        bool IsBinaryEqual(CPidl other);
        bool StartsWith(CPidl cp);
        nint ToPIDL();
    }
}