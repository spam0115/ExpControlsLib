using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsApiLib.Shell.ShellAPI;
using WindowsApiLib.Shell;

namespace WindowsApiLibTest
{
    /// <summary>
    /// The purpose of this class is to provide utilities to facilitate unit testing needing PIDLs.
    /// </summary>
    public static class MockPidlFactory
    {
        // -------------------------------------------------------------------------
        // Well-known GUIDs used in virtual folder SHITEMIDs
        // -------------------------------------------------------------------------
        // Desktop root:    {00021400-0000-0000-C000-000000000046}
        // My Computer:     {20D04FE0-3AEA-1069-A2D7-08002B30309D}
        // My Documents:    {450D8FBA-AD25-11D0-98A8-0800361B1103}
        // My Pictures:     {33E28130-4E1E-4676-835A-98395C3BC3BB}
        // User Profile:    {59031A47-3F72-44A7-89C5-5595FE6B30EE}

        // -------------------------------------------------------------------------
        // Public entry point
        // -------------------------------------------------------------------------
        /// <summary>
        /// Creates a mock PIDL byte array for the given CSIDL.
        /// The returned buffer is a packed array of SHITEMIDs followed by a
        /// two-byte null terminator, exactly as the real Shell would produce.
        /// </summary>
        public static byte[] CreateMockPidl(CSIDL csidl)
        {
            switch (csidl)
            {
                // ------------------------------------------------------------------
                // DESKTOP  →  empty PIDL (just the null terminator)
                // The desktop IS the root, so there are zero SHITEMIDs.
                // ------------------------------------------------------------------
                case CSIDL.DESKTOP:
                    return BuildPidl(/* no items */);

                // ------------------------------------------------------------------
                // DRIVES (My Computer)  →  one virtual-folder SHITEMID
                //   type byte 0x1F, sort byte 0x50, then CLSID_MyComputer
                // ------------------------------------------------------------------
                case CSIDL.DRIVES:
                    return BuildPidl(
                        MakeVirtualFolderItem(0x1F, 0x50, ShellNamespaceGuids.MyComputer));

                // ------------------------------------------------------------------
                // The new version of "My Computer" is called "This PC" in Windows 8.1/10/11.
                // ------------------------------------------------------------------
                case CSIDL.THISPC:
                    return BuildPidl(
                        MakeVirtualFolderItem(0x1F, 0x50, ShellNamespaceGuids.ComputerFolder));

                // ------------------------------------------------------------------
                // MY DOCUMENTS  →  virtual-folder SHITEMID
                //   type byte 0x1F, sort byte 0x50, then CLSID_MyDocuments
                // ------------------------------------------------------------------
                case CSIDL.MYDOCUMENTS:
                    return BuildPidl(
                        MakeVirtualFolderItem(0x1F, 0x50, ShellNamespaceGuids.MyDocuments));

                // ------------------------------------------------------------------
                // MY PICTURES  →  virtual-folder SHITEMID
                // ------------------------------------------------------------------
                case CSIDL.MYPICTURES:
                    return BuildPidl(
                        MakeVirtualFolderItem(0x1F, 0x50, ShellNamespaceGuids.MyPictures));

                // ------------------------------------------------------------------
                // C_DRIVE (C:\)
                // ------------------------------------------------------------------
                case CSIDL.C_DRIVE:
                    return BuildPidl(
                        MakeDriveItem("C:\\"));

                // ------------------------------------------------------------------
                // PROFILE (C:\Users\MockUser)
                //   Drive item for C:\  +  folder item for "Users"
                //                       +  folder item for "MockUser"
                // ------------------------------------------------------------------
                case CSIDL.PROFILE:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Users", "Users", 0x10),
                        MakeFolderItem("MockUser", "MockUser", 0x10));

                // ------------------------------------------------------------------
                // DESKTOPDIRECTORY (C:\Users\MockUser\Desktop)
                // ------------------------------------------------------------------
                case CSIDL.DESKTOPDIRECTORY:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Users", "Users", 0x10),
                        MakeFolderItem("MockUser", "MockUser", 0x10),
                        MakeFolderItem("Desktop", "Desktop", 0x10));

                // ------------------------------------------------------------------
                // LOCAL_APPDATA (C:\Users\MockUser\AppData\Local)
                // ------------------------------------------------------------------
                case CSIDL.LOCAL_APPDATA:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Users", "Users", 0x10),
                        MakeFolderItem("MockUser", "MockUser", 0x10),
                        MakeFolderItem("AppData", "AppData", 0x10),
                        MakeFolderItem("Local", "Local", 0x10));

                // ------------------------------------------------------------------
                // WINDOWS (C:\Windows)
                // ------------------------------------------------------------------
                case CSIDL.WINDOWS:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Windows", "Windows", 0x10));

                // ------------------------------------------------------------------
                // SYSTEM (C:\Windows\System32)
                // ------------------------------------------------------------------
                case CSIDL.SYSTEM:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Windows", "Windows", 0x10),
                        MakeFolderItem("System32", "System32", 0x10));

                // ------------------------------------------------------------------
                // PROGRAM_FILES (C:\Program Files)
                // ------------------------------------------------------------------
                case CSIDL.PROGRAM_FILES:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("Program Files", "Program Files", 0x10));

                // ------------------------------------------------------------------
                // PROGRAM_FILESX86 (C:\Program Files (x86))
                // ------------------------------------------------------------------
                case CSIDL.PROGRAM_FILESX86:
                    return BuildPidl(
                        MakeDriveItem("C:\\"),
                        MakeFolderItem("PROGRA~2", "Program Files (x86)", 0x10));


                default:
                    throw new ArgumentOutOfRangeException(nameof(csidl),
                        $"No mock PIDL defined for CSIDL 0x{(int)csidl:X2}");
            }
        }

        /// <summary>
        /// Creates a mock PIDL for an arbitrary string path.
        /// The PIDL will share a common prefix with any known CSIDL ancestor,
        /// then append additional SHITEMIDs for each remaining path segment.
        /// </summary>
        public static byte[] CreateMockPidlFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return BuildPidl(); // empty = desktop

            // Normalize separators and trim trailing slashes
            path = path.Replace('/', '\\').TrimEnd('\\');

            // Split into segments, e.g. "C:\Users\MockUser\Desktop"
            // → ["C:", "Users", "MockUser", "Desktop"]
            string[] segments = path.Split(new[] { '\\' },
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                return BuildPidl();

            // -----------------------------------------------------------------
            // 1. Find the longest known CSIDL ancestor that matches this path
            // -----------------------------------------------------------------
            // Map each CSIDL to its canonical mock path (lower-case for matching)
            var csidlPaths = new Dictionary<CSIDL, string[]>()
            {
                { CSIDL.DESKTOP,          new string[0] },                                              // root
                { CSIDL.DRIVES,           new[] { "C:" } },                                             // My Computer → treat C: as its child
                { CSIDL.WINDOWS,          new[] { "C:", "Windows" } },
                { CSIDL.SYSTEM,           new[] { "C:", "Windows", "System32" } },
                { CSIDL.PROGRAM_FILES,    new[] { "C:", "Program Files" } },
                { CSIDL.PROGRAM_FILESX86, new[] { "C:", "Program Files (x86)" } },
                { CSIDL.PROFILE,          new[] { "C:", "Users", "MockUser" } },
                { CSIDL.DESKTOPDIRECTORY, new[] { "C:", "Users", "MockUser", "Desktop" } },
                { CSIDL.LOCAL_APPDATA,    new[] { "C:", "Users", "MockUser", "AppData", "Local" } },
                { CSIDL.MYDOCUMENTS,      new[] { "C:", "Users", "MockUser", "Documents" } },
                { CSIDL.MYPICTURES,       new[] { "C:", "Users", "MockUser", "Pictures" } },
            };

            CSIDL bestCsidl = CSIDL.DESKTOP;
            int bestMatchDepth = 0;

            foreach (var kvp in csidlPaths)
            {
                string[] csidlSegs = kvp.Value;
                if (csidlSegs.Length == 0) continue; // skip DESKTOP — it matches everything

                // Check whether the input path starts with this CSIDL's segments
                if (csidlSegs.Length > segments.Length) continue;

                bool matches = true;
                for (int i = 0; i < csidlSegs.Length; i++)
                {
                    if (!string.Equals(csidlSegs[i], segments[i],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches && csidlSegs.Length > bestMatchDepth)
                {
                    bestMatchDepth = csidlSegs.Length;
                    bestCsidl = kvp.Key;
                }
            }

            // -----------------------------------------------------------------
            // 2. Collect the SHITEMIDs from the best matching ancestor PIDL
            // -----------------------------------------------------------------
            byte[] ancestorPidl = CreateMockPidl(bestCsidl);
            List<byte[]> items = ExtractItems(ancestorPidl);

            // -----------------------------------------------------------------
            // 3. Append SHITEMIDs for each remaining segment beyond the match
            // -----------------------------------------------------------------
            // Special case: if the best match was DRIVES and the first segment
            // is a drive letter, we need to emit a drive item for it rather than
            // a folder item, and we haven't consumed it yet.
            int startSegment = bestMatchDepth;

            if (bestCsidl == CSIDL.DRIVES && segments.Length > 0)
            {
                // The DRIVES PIDL represents "My Computer" itself.
                // The first segment (e.g. "C:") is the drive — emit a drive item.
                string driveSeg = segments[0];
                if (driveSeg.Length >= 1 && driveSeg.EndsWith(":"))
                {
                    items.Add(MakeDriveItem(driveSeg + "\\"));
                    startSegment = 1; // consumed the drive letter
                }
            }
            else if (bestCsidl == CSIDL.DESKTOP && segments.Length > 0)
            {
                // No ancestor matched — start from scratch.
                // If the first segment looks like a drive letter, emit a drive item.
                string first = segments[0];
                if (first.Length == 2 && first[1] == ':')
                {
                    // Prepend My Computer virtual folder, then the drive item
                    items.Add(MakeVirtualFolderItem(0x1F, 0x50, ShellNamespaceGuids.MyComputer));
                    items.Add(MakeDriveItem(first + "\\"));
                    startSegment = 1;
                }
            }

            // Append remaining segments as folder or file items
            for (int i = startSegment; i < segments.Length; i++)
            {
                string seg = segments[i];

                // Heuristic: does this segment look like a file?
                bool isFile = IsLikelyFile(seg, i == segments.Length - 1);

                if (isFile)
                {
                    items.Add(MakeFileItem(seg));
                }
                else
                {
                    // Derive a plausible 8.3 short name from the long name
                    string shortName = MakeShortName(seg);
                    items.Add(MakeFolderItem(shortName, seg, 0x10));
                }
            }

            return BuildPidl(items.ToArray());
        }

        /// <summary>
        /// Returns a human-readable dump of a PIDL's structure, one line per SHITEMID.
        /// Useful for Assert failure messages and debug output.
        /// </summary>
        public static string DumpPidl(byte[] pidl)
        {
            var sb = new StringBuilder();
            var items = ExtractItems(pidl);

            if (items.Count == 0)
            {
                sb.AppendLine("[PIDL: Desktop/Root — empty, terminator only]");
                return sb.ToString();
            }

            sb.AppendLine($"[PIDL: {items.Count} item(s), total {pidl.Length} bytes]");
            for (int i = 0; i < items.Count; i++)
            {
                byte[] item = items[i];
                ushort cb = (ushort)(item[0] | (item[1] << 8));
                byte type = item[2];
                string typeDesc = DescribeItemType(type);

                sb.AppendLine($"  [{i}] cb={cb,3}  type=0x{type:X2} ({typeDesc})  hex={ToHexString(item)}");

                // Try to extract a display name for known types
                string name = TryExtractName(item);
                if (name != null)
                    sb.AppendLine($"       name=\"{name}\"");
            }
            sb.AppendLine($"  [terminator: 00 00]");
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if two PIDLs are byte-for-byte identical.
        /// </summary>
        public static bool ArePidlsEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Returns true if 'child' starts with the same SHITEMIDs as 'ancestor'.
        /// Equivalent to ILIsParent() in the Shell API.
        /// </summary>
        public static bool IsAncestor(byte[] ancestor, byte[] child)
        {
            if (ancestor == null || child == null) return false;

            var ancestorItems = ExtractItems(ancestor);
            var childItems = ExtractItems(child);

            if (ancestorItems.Count > childItems.Count) return false;

            for (int i = 0; i < ancestorItems.Count; i++)
            {
                if (!ArePidlsEqual(ancestorItems[i], childItems[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the relative PIDL of 'child' with respect to 'ancestor'.
        /// Equivalent to ILFindChild() in the Shell API.
        /// Returns null if ancestor is not actually an ancestor of child.
        /// </summary>
        public static byte[] GetRelativePidl(byte[] ancestor, byte[] child)
        {
            if (!IsAncestor(ancestor, child)) return null;

            var ancestorItems = ExtractItems(ancestor);
            var childItems = ExtractItems(child);

            var relativeItems = childItems.Skip(ancestorItems.Count).ToList();
            return BuildPidl(relativeItems.ToArray());
        }

        /// <summary>
        /// Returns the number of SHITEMIDs in the PIDL (excluding the terminator).
        /// Equivalent to ILGetSize / walking the list.
        /// </summary>
        public static int GetItemCount(byte[] pidl)
            => ExtractItems(pidl).Count;

        /// <summary>
        /// Returns the last SHITEMID as a single-item PIDL.
        /// Equivalent to ILFindLastID().
        /// </summary>
        public static byte[] GetLastItem(byte[] pidl)
        {
            var items = ExtractItems(pidl);
            if (items.Count == 0) return BuildPidl();
            return BuildPidl(items[items.Count - 1]);
        }

        /// <summary>
        /// Returns a new PIDL with the last SHITEMID removed.
        /// Equivalent to ILRemoveLastID() — i.e. navigate to parent.
        /// </summary>
        public static byte[] RemoveLastItem(byte[] pidl)
        {
            var items = ExtractItems(pidl);
            if (items.Count == 0) return BuildPidl();
            return BuildPidl(items.Take(items.Count - 1).ToArray());
        }

        /// <summary>
        /// Concatenates two PIDLs.
        /// Equivalent to ILCombine() in the Shell API.
        /// </summary>
        public static byte[] Combine(byte[] parent, byte[] child)
        {
            var items = ExtractItems(parent);
            items.AddRange(ExtractItems(child));
            return BuildPidl(items.ToArray());
        }

        /// <summary>
        /// Returns the PIDL for the Nth item (0-based), as a single-item PIDL.
        /// </summary>
        public static byte[] GetItemAt(byte[] pidl, int index)
        {
            var items = ExtractItems(pidl);
            if (index < 0 || index >= items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return BuildPidl(items[index]);
        }

        /// <summary>
        /// Returns true if the PIDL is structurally valid:
        ///   - Not null or empty
        ///   - All cb values are consistent with the buffer length
        ///   - Ends with a null terminator
        /// </summary>
        public static bool IsValidPidl(byte[] pidl)
        {
            if (pidl == null || pidl.Length < 2) return false;

            int offset = 0;
            while (offset + 2 <= pidl.Length)
            {
                ushort cb = (ushort)(pidl[offset] | (pidl[offset + 1] << 8));
                if (cb == 0)
                    return offset + 2 == pidl.Length; // terminator must be the last 2 bytes
                if (offset + cb > pidl.Length)
                    return false; // cb points past end of buffer
                offset += cb;
            }
            return false; // never found a terminator
        }

        /// <summary>
        /// Returns true if the PIDL represents the desktop (empty PIDL).
        /// </summary>
        public static bool IsDesktopPidl(byte[] pidl)
            => pidl != null && pidl.Length == 2 && pidl[0] == 0 && pidl[1] == 0;

        /// <summary>
        /// Returns true if the PIDL is DWORD-aligned at every SHITEMID boundary.
        /// The real Shell requires this for performance.
        /// </summary>
        public static bool IsDwordAligned(byte[] pidl)
        {
            foreach (byte[] item in ExtractItems(pidl))
                if (item.Length % 4 != 0) return false;
            return true;
        }

        /// <summary>
        /// Get's the display name for a given pidl.  The display name is simply the name of the last
        /// segment of the pidl - ie, the pidl without the preceding path.
        /// </summary>
        /// <param name="pidl"></param>
        /// <returns></returns>
        public static string GetDisplayName(byte[] pidl)
        {
            var last = GetLastItem(pidl);
            return GetDisplayPathFromPidl(last);
        }

        public static string GetDisplayName(IntPtr pidl)
        {
            var bytes = PidlToBytes(pidl);
            return GetDisplayName(bytes);
        }

        /// <summary>
        /// Attempts to reconstruct a display path string from a mock PIDL.
        /// Useful for round-trip assertions in tests.
        /// </summary>
        public static string GetDisplayPathFromPidl(byte[] pidl)
        {
            var items = ExtractItems(pidl);
            var parts = new List<string>();

            foreach (byte[] item in items)
            {
                string name = TryExtractName(item);
                if (name != null)
                    parts.Add(name);
            }

            if (parts.Count == 0) return "Desktop";

            // If first item is a GUID it's a virtual folder — use a friendly name
            if (parts[0].StartsWith("{"))
                parts[0] = GuidToFriendlyName(parts[0]);

            return string.Join("\\", parts);
        }

        public static string GetDisplayPathFromPidl(IntPtr pidl)
        { 
            var bytes = PidlToBytes(pidl);
            return GetDisplayPathFromPidl(bytes);
        }

        /// <summary>
        /// Reads a PIDL from an unmanaged memory pointer and returns it as a managed byte array.
        /// The pointer must point to a valid PIDL structure (one or more SHITEMIDs followed
        /// by a two-byte null terminator).
        /// </summary>
        /// <param name="pidlPtr">An IntPtr pointing to an unmanaged PIDL.</param>
        /// <returns>A byte array containing the full PIDL including the null terminator.</returns>
        /// <exception cref="ArgumentException">Thrown if the pointer is null/zero.</exception>
        public static byte[] PidlToBytes(IntPtr pidlPtr)
        {
            if (pidlPtr == IntPtr.Zero)
                throw new ArgumentException("PIDL pointer is null.", nameof(pidlPtr));

            // ------------------------------------------------------------------
            // Pass 1: Walk the PIDL to calculate the total byte length.
            // We read cb from each SHITEMID header (2 bytes) and advance until
            // we hit a zero cb (the null terminator).
            // ------------------------------------------------------------------
            int totalLength = 0;
            IntPtr current = pidlPtr;

            while (true)
            {
                // Read the cb field (USHORT, little-endian) from unmanaged memory
                ushort cb = (ushort)Marshal.ReadInt16(current);

                if (cb == 0)
                {
                    // This is the null terminator — include its 2 bytes then stop
                    totalLength += 2;
                    break;
                }

                // Guard against obviously corrupt PIDLs (cb should be at least 3:
                // 2 bytes for cb itself + at least 1 byte of abID data)
                if (cb < 3)
                    throw new InvalidOperationException(
                        $"Corrupt PIDL: SHITEMID at offset {totalLength} has cb={cb}, " +
                        $"which is too small to be valid.");

                totalLength += cb;

                // Advance pointer by cb bytes to the next SHITEMID
                current = IntPtr.Add(current, cb);
            }

            // ------------------------------------------------------------------
            // Pass 2: Now that we know the exact size, copy the whole buffer
            // from unmanaged memory into a managed byte array in one shot.
            // ------------------------------------------------------------------
            byte[] result = new byte[totalLength];
            Marshal.Copy(pidlPtr, result, 0, totalLength);
            return result;
        }

        private static string GuidToFriendlyName(string guidStr)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "{20D04FE0-3AEA-1069-A2D7-08002B30309D}", "My Computer" },
                { "{0AC0837C-BBF8-452A-850D-79D08E667CA7}", "This PC" },
                { "{450D8FBA-AD25-11D0-98A8-0800361B1103}", "My Documents" },
                { "{33E28130-4E1E-4676-835A-98395C3BC3BB}", "My Pictures" },
                { "{59031A47-3F72-44A7-89C5-5595FE6B30EE}", "User Profile" },
                { "{00021400-0000-0000-C000-000000000046}", "Desktop" },
            };
            return map.TryGetValue(guidStr, out string name) ? name : guidStr;
        }

        private static string DescribeItemType(byte type) => type switch
        {
            0x1F => "Virtual Folder (CLSID)",
            0x2F => "Drive Root",
            0x31 => "Folder",
            0x32 => "File",
            0x41 => "Network Share",
            0x42 => "Network Server",
            0x46 => "Network Location",
            _ => "Unknown"
        };

        private static string TryExtractName(byte[] item)
        {
            if (item.Length < 3) return null;
            byte type = item[2];

            try
            {
                switch (type)
                {
                    case 0x2F: // Drive — ASCII string at offset 3
                        return Encoding.ASCII.GetString(item, 3, Math.Min(4, item.Length - 3))
                                             .TrimEnd('\0');

                    case 0x31: // Folder — short name at offset 16, long name at offset 32
                    case 0x32: // File
                        if (item.Length > 32)
                        {
                            // Try long Unicode name first (offset 32)
                            int maxLen = item.Length - 32;
                            // Find null terminator in UTF-16
                            int nameLen = 0;
                            while (nameLen + 1 < maxLen &&
                                   !(item[32 + nameLen] == 0 && item[33 + nameLen] == 0))
                                nameLen += 2;
                            if (nameLen > 0)
                                return Encoding.Unicode.GetString(item, 32, nameLen);
                        }
                        // Fall back to short name at offset 16
                        if (item.Length > 16)
                            return Encoding.ASCII.GetString(item, 16, Math.Min(14, item.Length - 16))
                                                 .TrimEnd('\0');
                        return null;

                    case 0x1F: // Virtual folder — extract GUID
                        if (item.Length >= 20)
                        {
                            byte[] guidBytes = new byte[16];
                            Buffer.BlockCopy(item, 4, guidBytes, 0, 16);
                            return new Guid(guidBytes).ToString("B").ToUpperInvariant();
                        }
                        return null;

                    default:
                        return null;
                }
            }
            catch { return null; }
        }

        private static string ToHexString(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 3);
            foreach (byte b in data)
                sb.Append($"{b:X2} ");
            return sb.ToString().TrimEnd();
        }

        // =========================================================================
        // Helper: extract individual SHITEMIDs from an existing PIDL byte array
        // =========================================================================
        private static List<byte[]> ExtractItems(byte[] pidl)
        {
            var result = new List<byte[]>();
            int offset = 0;

            while (offset + 2 <= pidl.Length)
            {
                ushort cb = (ushort)(pidl[offset] | (pidl[offset + 1] << 8));
                if (cb == 0) break; // null terminator

                if (offset + cb > pidl.Length) break; // malformed

                byte[] item = new byte[cb];
                Buffer.BlockCopy(pidl, offset, item, 0, cb);
                result.Add(item);
                offset += cb;
            }

            return result;
        }

        // =========================================================================
        // Helper: build a file SHITEMID (type 0x32 = file, not folder)
        // Layout mirrors MakeFolderItem but with type 0x32 and a non-zero file size
        // =========================================================================
        private static byte[] MakeFileItem(string fileName)
        {
            // Use a mock file size based on the hash of the name so it looks varied
            uint mockFileSize = (uint)(Math.Abs(fileName.GetHashCode()) % 0x00FFFFFF) + 1024;

            byte[] shortBytes = Encoding.ASCII.GetBytes(MakeShortName(fileName));
            byte[] longBytes = Encoding.Unicode.GetBytes(fileName);

            int rawSize = 32 + longBytes.Length + 2;
            int cb = (rawSize + 3) & ~3;

            byte[] item = new byte[cb];
            WriteUInt16LE(item, 0, (ushort)cb);
            item[2] = 0x32;  // file type indicator
            item[3] = 0x20;  // FILE_ATTRIBUTE_ARCHIVE

            // Mock FILETIME: 2024-06-15 12:00:00 UTC
            ulong mockFt = (ulong)(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
                                   .ToFileTimeUtc());
            WriteUInt64LE(item, 4, mockFt);

            WriteUInt32LE(item, 12, mockFileSize);

            int snCopy = Math.Min(shortBytes.Length, 13);
            Buffer.BlockCopy(shortBytes, 0, item, 16, snCopy);

            WriteUInt16LE(item, 30, 0x0003);
            Buffer.BlockCopy(longBytes, 0, item, 32, longBytes.Length);

            return item;
        }

        // =========================================================================
        // Helper: derive a plausible 8.3 short name from a long name
        // e.g. "Program Files (x86)" → "PROGRA~1"
        // =========================================================================
        private static string MakeShortName(string longName)
        {
            // Strip extension for now; we'll re-add it for files
            int dotIndex = longName.LastIndexOf('.');
            string baseName = dotIndex >= 0 ? longName.Substring(0, dotIndex) : longName;
            string extension = dotIndex >= 0 ? longName.Substring(dotIndex) : string.Empty;

            // Remove spaces and special characters
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                baseName, @"[^A-Za-z0-9]", "").ToUpperInvariant();

            if (cleaned.Length == 0) cleaned = "ITEM";

            string shortBase;
            if (cleaned.Length <= 8 && baseName == cleaned)
            {
                // Already short and clean — use as-is
                shortBase = cleaned;
            }
            else
            {
                // Truncate to 6 chars and append ~1
                shortBase = (cleaned.Length > 6 ? cleaned.Substring(0, 6) : cleaned) + "~1";
            }

            // Re-attach extension, truncated to 3 chars
            if (extension.Length > 0)
            {
                string shortExt = extension.Length > 4
                    ? extension.Substring(0, 4)  // includes the dot
                    : extension;
                return (shortBase + shortExt).ToUpperInvariant();
            }

            return shortBase.ToUpperInvariant();
        }

        // =========================================================================
        // Helper: heuristic to decide if a path segment is a file vs a folder
        // =========================================================================
        private static bool IsLikelyFile(string segment, bool isLastSegment)
        {
            if (!isLastSegment) return false; // intermediate segments are always folders

            // If it has a known file extension, treat it as a file
            string[] fileExtensions =
            {
                ".txt", ".exe", ".dll", ".bat", ".cmd", ".ps1", ".log",
                ".csv", ".xml", ".json", ".ini", ".cfg", ".zip", ".pdf",
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".mp3", ".mp4",
                ".cs",  ".cpp",  ".h",   ".py",  ".js",  ".ts",  ".html"
            };

            string lower = segment.ToLowerInvariant();
            foreach (string ext in fileExtensions)
                if (lower.EndsWith(ext)) return true;

            return false;
        }

        // =========================================================================
        // SHITEMID builders
        // =========================================================================

        /// <summary>
        /// Builds a virtual-folder SHITEMID (type 0x1F).
        /// Layout:  cb(2) | typeByte(1) | sortByte(1) | GUID(16)  = 20 bytes
        /// </summary>
        private static byte[] MakeVirtualFolderItem(byte typeByte, byte sortByte, Guid clsid)
        {
            // A virtual-folder SHITEMID is always 20 bytes (0x14):
            //   2  bytes  cb
            //   1  byte   type indicator  (0x1F for root/CLSID items)
            //   1  byte   sort order hint (0x50 is common)
            //  16  bytes  CLSID in little-endian wire format
            const ushort cb = 20;
            byte[] item = new byte[cb];
            WriteUInt16LE(item, 0, cb);
            item[2] = typeByte;
            item[3] = sortByte;
            byte[] guidBytes = clsid.ToByteArray(); // already in COM/little-endian order
            Buffer.BlockCopy(guidBytes, 0, item, 4, 16);
            return item;
        }

        /// <summary>
        /// Builds a drive-root SHITEMID (type 0x2F).
        /// Layout:  cb(2) | 0x2F(1) | driveString(4, e.g. "C:\") | padding
        /// Real Shell drive items are 23 bytes, DWORD-aligned.
        /// </summary>
        private static byte[] MakeDriveItem(string driveRoot)
        {
            // Real drive SHITEMIDs are 23 bytes (padded to 24 for DWORD alignment).
            // Structure:
            //   2  bytes  cb  (0x17 = 23, but we round to 24 for alignment)
            //   1  byte   type indicator 0x2F
            //  20  bytes  drive string (ASCII, null-padded)
            //   1  byte   padding to reach DWORD boundary
            const ushort cb = 24; // DWORD-aligned
            byte[] item = new byte[cb];
            WriteUInt16LE(item, 0, cb);
            item[2] = 0x2F; // drive type indicator
                            // Write drive root as ASCII (e.g. "C:\")
            byte[] driveBytes = Encoding.ASCII.GetBytes(driveRoot);
            int copyLen = Math.Min(driveBytes.Length, cb - 3);
            Buffer.BlockCopy(driveBytes, 0, item, 3, copyLen);
            // Remaining bytes are already zero (null-padded)
            return item;
        }

        /// <summary>
        /// Builds a file-system folder SHITEMID (type 0x31 for folders).
        /// Mimics the real Shell structure which stores both the short (8.3)
        /// name and the long Unicode name in an extension block.
        ///
        /// Simplified layout used here:
        ///   cb(2) | type(1) | fileAttributes(1) | FILETIME(8) | fileSize(4) |
        ///   shortName(14, ASCII 8.3 null-padded) | extHeader(2) | longName(variable, UTF-16)
        /// Total is DWORD-aligned.
        /// </summary>
        private static byte[] MakeFolderItem(string shortName, string longName, byte fileAttributes)
        {
            // Layout (offsets):
            //  0  USHORT  cb
            //  2  BYTE    type indicator (0x31 = folder)
            //  3  BYTE    file attributes (e.g. 0x10 = FILE_ATTRIBUTE_DIRECTORY)
            //  4  FILETIME last-write time (8 bytes) — mocked as a fixed value
            // 12  DWORD   file size (0 for folders)
            // 16  CHAR[]  short name, null-terminated, padded to 14 bytes
            // 30  USHORT  extension block marker (0x0003 = has Unicode name)
            // 32  WCHAR[] long name, null-terminated

            byte[] shortBytes = Encoding.ASCII.GetBytes(shortName.Length > 12
                ? shortName.Substring(0, 12) : shortName);
            byte[] longBytes = Encoding.Unicode.GetBytes(longName);

            // Fixed-size header portion = 32 bytes
            // Then long name (UTF-16) + 2-byte null terminator
            int longNameLen = longBytes.Length + 2; // +2 for null terminator
            int rawSize = 32 + longNameLen;
            // DWORD-align
            int cb = (rawSize + 3) & ~3;

            byte[] item = new byte[cb];
            WriteUInt16LE(item, 0, (ushort)cb);
            item[2] = 0x31;           // folder type indicator
            item[3] = fileAttributes; // FILE_ATTRIBUTE_DIRECTORY = 0x10

            // Mock FILETIME: 2024-01-01 00:00:00 UTC
            // FILETIME = 100-nanosecond intervals since 1601-01-01
            ulong mockFt = (ulong)(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                                   .ToFileTimeUtc());
            WriteUInt64LE(item, 4, mockFt);

            // File size = 0 for folders (offset 12)
            WriteUInt32LE(item, 12, 0);

            // Short name at offset 16, ASCII, null-padded to 14 bytes
            int snCopy = Math.Min(shortBytes.Length, 13);
            Buffer.BlockCopy(shortBytes, 0, item, 16, snCopy);
            // item[16 + snCopy] is already 0 (null terminator)

            // Extension block marker at offset 30: 0x0003 signals Unicode name follows
            WriteUInt16LE(item, 30, 0x0003);

            // Long name (UTF-16) at offset 32
            Buffer.BlockCopy(longBytes, 0, item, 32, longBytes.Length);
            // 2-byte null terminator already zero from array initialization

            return item;
        }

        // =========================================================================
        // PIDL assembler — packs SHITEMIDs and appends the null terminator
        // =========================================================================
        private static byte[] BuildPidl(params byte[][] items)
        {
            int totalSize = 2; // always ends with a 2-byte null terminator
            foreach (byte[] item in items)
                totalSize += item.Length;

            byte[] pidl = new byte[totalSize];
            int offset = 0;
            foreach (byte[] item in items)
            {
                Buffer.BlockCopy(item, 0, pidl, offset, item.Length);
                offset += item.Length;
            }
            // Write null terminator (cb = 0) — already zero from array init,
            // but be explicit for clarity.
            pidl[offset] = 0x00;
            pidl[offset + 1] = 0x00;
            return pidl;
        }

        // =========================================================================
        // Little-endian write helpers
        // =========================================================================
        private static void WriteUInt16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32LE(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUInt64LE(byte[] buf, int offset, ulong value)
        {
            WriteUInt32LE(buf, offset, (uint)(value & 0xFFFFFFFF));
            WriteUInt32LE(buf, offset + 4, (uint)(value >> 32));
        }
    }
}
