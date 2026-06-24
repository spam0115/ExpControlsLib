using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WindowsApiLib;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLibTest
{
    public class MockPidl : ICPidl, IEnumerable
    {
        private readonly byte[] m_bytes;

        public MockPidl(nint pidl)
        {
            m_bytes = PidlToBytes(pidl);
        }

        public MockPidl(string path)
        {
            m_bytes = MockPidlFactory.CreateMockPidlFromPath(path);
        }

        public MockPidl(byte[] bytes)
        {
            m_bytes = bytes;
        }

        public int ItemCount => MockPidlFactory.GetItemCount(m_bytes);
        public int Length => m_bytes.Length;
        public byte[] PidlBytes => m_bytes;

        public static byte[] PidlToBytes(nint pidl)
        {
            if (pidl == IntPtr.Zero) return new byte[2];
            
            // Walk the memory to find the size
            int size = 0;
            while (true)
            {
                ushort cb = unchecked((ushort)Marshal.ReadInt16(pidl, size));
                if (cb == 0)
                {
                    size += 2; // Include terminator
                    break;
                }
                size += cb;
            }
            
            byte[] bytes = new byte[size];
            Marshal.Copy(pidl, bytes, 0, size);
            return bytes;
        }

        public static nint BytesToPidl(byte[] b)
        {
            if (b == null || b.Length == 0) return IntPtr.Zero;
            nint ptr = Marshal.AllocCoTaskMem(b.Length);
            Marshal.Copy(b, 0, ptr, b.Length);
            return ptr;
        }

        public static bool AreBytesEqual(nint pidl1, nint pidl2)
        {
            return MockPidlFactory.ArePidlsEqual(PidlToBytes(pidl1), PidlToBytes(pidl2));
        }

        public static bool AreEqual(IShellFolder parent, nint pidl1, nint pidl2)
        {
            return ResolvesToSamePathOrName(pidl1, pidl2);
        }

        nint ICPidl.ToPIDL() => BytesToPidl(m_bytes);

        public static nint Clone(nint pidl)
        {
            var b = PidlToBytes(pidl);
            return BytesToPidl(b);
        }

        public static nint Concatenate(nint pidl1, nint pidl2)
        {
            var b1 = PidlToBytes(pidl1);
            var b2 = PidlToBytes(pidl2);
            var res = MockPidlFactory.Combine(b1, b2);
            return BytesToPidl(res);
        }

        public static nint Copy(nint pidl)
        {
            return Clone(pidl);
        }

        public static nint[] Decompose(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            var result = new List<nint>();
            int offset = 0;
            while (offset + 2 <= bytes.Length)
            {
                ushort cb = unchecked((ushort)(bytes[offset] | (bytes[offset + 1] << 8)));
                if (cb == 0) break;
                byte[] item = new byte[cb + 2];
                Buffer.BlockCopy(bytes, offset, item, 0, cb);
                item[cb] = 0;
                item[cb + 1] = 0;
                result.Add(BytesToPidl(item));
                offset += cb;
            }
            return result.ToArray();
        }

        public static void Dump(nint pidl)
        {
            Console.WriteLine(MockPidlFactory.DumpPidl(PidlToBytes(pidl)));
        }

        public static void DumpHex(byte[] b, int sPos = 0, int ePos = 0)
        {
        }

        public static string? GetDisplayName(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            var last = MockPidlFactory.GetLastItem(bytes);
            return MockPidlFactory.GetDisplayPathFromPidl(last);
        }

        public static string? GetFileSystemPath(nint pidl)
        {
            return MockPidlFactory.GetDisplayPathFromPidl(PidlToBytes(pidl));
        }

        public static string? GetFileSystemPathFromShellParsingName(string parsingName)
        {
            return parsingName;
        }

        public static string? GetFullName(nint pidl1)
        {
            return MockPidlFactory.GetDisplayPathFromPidl(PidlToBytes(pidl1));
        }

        public static string? GetParsingName(nint pidl)
        {
            return MockPidlFactory.GetDisplayPathFromPidl(PidlToBytes(pidl));
        }

        public static string? GetParsingPath(nint pidl)
        {
            return MockPidlFactory.GetDisplayPathFromPidl(PidlToBytes(pidl));
        }

        public static uint HashPidlFastLastFull(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            uint hash = 0;
            foreach (var b in bytes) hash = hash * 31 + b;
            return hash;
        }

        public static string HexNum(int num, int nrChrs)
        {
            return num.ToString("X" + nrChrs);
        }

        public static nint ILFindLastID(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            var last = MockPidlFactory.GetLastItem(bytes);
            return BytesToPidl(last);
        }

        public static bool IsAncestorOf(CShellItem Item1, CShellItem Item2, bool ImmediateOnly = false)
        {
            return IsAncestorOf(Item1.PIDL, Item2.PIDL, ImmediateOnly);
        }

        public static bool IsAncestorOf(nint pidl1, nint pidl2, bool ImmediateOnly = false)
        {
            var b1 = PidlToBytes(pidl1);
            var b2 = PidlToBytes(pidl2);
            if (ImmediateOnly)
            {
                return MockPidlFactory.IsAncestor(b1, b2) && MockPidlFactory.GetItemCount(b1) + 1 == MockPidlFactory.GetItemCount(b2);
            }
            return MockPidlFactory.IsAncestor(b1, b2);
        }

        public static bool IsBinaryEqual(nint pidl1, nint pidl2)
        {
            return MockPidlFactory.ArePidlsEqual(PidlToBytes(pidl1), PidlToBytes(pidl2));
        }

        public bool IsBinaryEqual(CPidl other)
        {
            return MockPidlFactory.ArePidlsEqual(m_bytes, other.PidlBytes);
        }

        public static bool IsShellNamespaceRoot(nint pidl)
        {
            return MockPidlFactory.IsDesktopPidl(PidlToBytes(pidl));
        }

        public static bool IsValid(byte[] b)
        {
            return MockPidlFactory.IsValidPidl(b);
        }

        public static int GetPidlLength(nint pidl)
        {
            return PidlToBytes(pidl).Length - 2;
        }

        public static byte[] JoinPidlBytes(byte[] b1, byte[] b2)
        {
            return MockPidlFactory.Combine(b1, b2);
        }

        public static nint PathToPidl(string path)
        {
            return BytesToPidl(MockPidlFactory.CreateMockPidlFromPath(path));
        }

        public static string? ResolveShellGUID(string guidPath)
        {
            return guidPath;
        }

        public static bool ResolvesToSamePathOrName(nint pidl1, nint pidl2)
        {
            return MockPidlFactory.ArePidlsEqual(PidlToBytes(pidl1), PidlToBytes(pidl2));
        }

        public static int SegmentCount(nint pidl)
        {
            return MockPidlFactory.GetItemCount(PidlToBytes(pidl));
        }

        public static PidlSplitResult Split(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            var parentBytes = MockPidlFactory.RemoveLastItem(bytes);
            var childBytes = MockPidlFactory.GetLastItem(bytes);
            return new PidlSplitResult(BytesToPidl(parentBytes), BytesToPidl(childBytes));
        }

        public static bool StartsWith(CPidl A, CPidl B)
        {
            return MockPidlFactory.IsAncestor(B.PidlBytes, A.PidlBytes);
        }

        public static bool StartsWith(nint pidl1, nint pidl2)
        {
            return MockPidlFactory.IsAncestor(PidlToBytes(pidl2), PidlToBytes(pidl1));
        }

        public bool StartsWith(CPidl cp)
        {
            return MockPidlFactory.IsAncestor(cp.PidlBytes, m_bytes);
        }

        public static nint ToPidl(string path)
        {
            return PathToPidl(path);
        }

        public static string? ToString(nint pidl, bool absolute = true)
        {
            var bytes = PidlToBytes(pidl);
            if (!absolute)
            {
                var last = MockPidlFactory.GetLastItem(bytes);
                return MockPidlFactory.GetDisplayPathFromPidl(last);
            }
            return MockPidlFactory.GetDisplayPathFromPidl(bytes);
        }

        public static nint TrimLast(nint pidl)
        {
            var bytes = PidlToBytes(pidl);
            var parentBytes = MockPidlFactory.RemoveLastItem(bytes);
            return BytesToPidl(parentBytes);
        }

        public object[] Decompose()
        {
            var result = new List<object>();
            int offset = 0;
            while (offset + 2 <= m_bytes.Length)
            {
                ushort cb = unchecked((ushort)(m_bytes[offset] | (m_bytes[offset + 1] << 8)));
                if (cb == 0) break;
                byte[] item = new byte[cb + 2];
                Buffer.BlockCopy(m_bytes, offset, item, 0, cb);
                item[cb] = 0;
                item[cb + 1] = 0;
                result.Add(item);
                offset += cb;
            }
            return result.ToArray();
        }

        public IEnumerator GetEnumerator()
        {
            return Decompose().GetEnumerator();
        }
    }
}
