using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowsApiLib;
using WindowsApiLib.Shell.Interfaces;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib.Shell
{
    /// <summary>
    /// Contains a number of utility routines used by and with WindowsApiLib.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ShellHelper
    {
        #region        Low/High Word

        /// <summary>
        /// Retrieves the High Word of a WParam of a WindowMessage
        /// </summary>
        /// <param name="ptr">The pointer to the WParam</param>
        /// <returns>The unsigned integer for the High Word</returns>
        public static uint HiWord(IntPtr ptr)
        {
            if (((uint)ptr & 0x80000000L) == 0x80000000L)
            {
                return (uint)ptr >> 16;
            }
            else
            {
                return (uint)((uint)ptr >> 16 & 0xFFFFL);
            }
        }

        /// <summary>
        /// Retrieves the Low Word of a WParam of a WindowMessage
        /// </summary>
        /// <param name="ptr">The pointer to the WParam</param>
        /// <returns>The unsigned integer for the Low Word</returns>
        public static uint LoWord(IntPtr ptr)
        {
            return (uint)((uint)ptr & 0xFFFFL);
        }

        #endregion

        #region        SzToString
        /// <summary>
        /// SzToString accepts an array of bytes representing an Default Encoded string and
        /// converts it to a .Net Unicode String.  SzToString Truncates the String at the first
        /// nul (0) byte in the input array.  
        /// </summary>
        /// <param name="arb">A Byte() to be translated using the Default codepage</param>
        /// <param name="iPos">Start index in the Array - Defaults to 0</param>
        /// <param name="len">Number of Bytes to translate - Defaults to entire Array</param>
        /// <returns>A .Net String. If errors, returns the empty string ("")</returns>
        /// <remarks></remarks>
        public static string SzToString(byte[] arb, int ipos = 0, int len = 0)
        {
            int UB = arb.Length - 1;
            if (ipos > UB)
            {
                return "";
            }
            else
            {
                if (len == 0)
                    len = UB - ipos + 1;
                if (ipos + len > UB + 1)
                {
                    return "";
                }
                else
                {
                    int i = ipos;
                    while (i < ipos + len)
                    {
                        if (arb[i] == 0)
                        {
                            len = i - ipos;
                            break;
                        }
                        i += 1;
                    }
                    char[] uChars = Encoding.Unicode.GetChars(Encoding.Convert(Encoding.Default, Encoding.Unicode, arb, ipos, len));
                    return new string(uChars);
                }
            }

        }

        #endregion

        #region        IStream/IStorage
        /// <summary>
        /// Obtains an IStream Interface for the input CShellItem
        /// </summary>
        /// <param name="item">The CShellItem for whom an IStream Interface is desired.</param>
        /// <param name="streamPtr"></param>
        /// <param name="stream">Returned Interface</param>
        /// <returns>An IStream Interface for the input CShellItem</returns>
        /// <remarks>Not used by WindowsApiLib or its' Demo</remarks>
        /// 
        public static bool GetIStream(CShellItem item, IntPtr streamPtr, out IStream stream)
        {
            var ishellfolder = item.Parent.GetIShellFolder();
            try
            {
                if (ishellfolder.BindToStorage(CPidl.ILFindLastID(item.PIDL), IntPtr.Zero, ShellAPI.IID_IStream, streamPtr) == S_OK)
                {
                    stream = (IStream)Marshal.GetTypedObjectForIUnknown(streamPtr, typeof(IStream));
                    return true;
                }
                else
                {
                    stream = null;
                    streamPtr = IntPtr.Zero;
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ishellfolder);
            }
        }
        /// <summary>
        /// Obtains an IStorage Interface for the input CShellItem
        /// </summary>
        /// <param name="item">The CShellItem for whom an IStorage Interface is desired.</param>
        /// <param name="storagePtr"></param>
        /// <param name="storage">Returned Interface</param>
        /// <returns>An IStorage Interface for the input CShellItem</returns>
        /// <remarks>Not used by WindowsApiLib or its' Demo</remarks>        
        public static bool GetIStorage(CShellItem item, IntPtr storagePtr, out IStorage storage)
        {
            var ishellfolder = item.Parent.GetIShellFolder();
            try
            {
                if (ishellfolder.BindToStorage(CPidl.ILFindLastID(item.PIDL), IntPtr.Zero, ShellAPI.IID_IStorage, storagePtr) == S_OK)
                {
                    storage = (IStorage)Marshal.GetTypedObjectForIUnknown(storagePtr, typeof(IStorage));
                    return true;
                }
                else
                {
                    storage = null;
                    storagePtr = IntPtr.Zero;
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ishellfolder);
            }
        }

        #endregion

        #region        GetIDropTarget
        /// <summary>
        /// This method uses the GetUIObjectOf method of IShellFolder to obtain the IDropTarget of a
        /// CShellItem. 
        /// </summary>
        /// <param name="item">The item for which to obtain the IDropTarget</param>
        /// <param name="dropTarget">The IDropTarget interface of the input Folder</param>
        /// <returns>True if successful in obtaining the IDropTarget Interface.</returns>
        /// <remarks>The original FileBrowser version of this returned the IntPtr which points to
        /// the interface. This is not needed since GetTypedObjectForIUnknown manages that IntPtr.
        /// For all purposes, the CShellItem.GetDropTargetOf routine is more efficient and provides
        /// the same interface.</remarks>
        /// 
        public static bool GetIDropTarget(CShellItem item, out IDropTarget dropTarget)
        {
            dropTarget = null;
            IntPtr dropTargetPtr = IntPtr.Zero;
            var parent = item.Parent;

            if (parent == null)
                parent = item;

            IShellFolder folder;
            if (ReferenceEquals(item, ShellController.DesktopCSI))
            {
                folder = item.GetIShellFolder();
            }
            else
            {
                folder = item.Parent.GetIShellFolder();
            }

            if (folder == null) //some virtual locations don't provide an IShellFolder
                return false;

            var relpidl = item.LastPIDL;
            IntPtr rgfReserved = IntPtr.Zero; //unused

            try
            {
                if (folder.GetUIObjectOf(IntPtr.Zero, 1, new IntPtr[] { relpidl }, ShellAPI.IID_IDropTarget, rgfReserved, out dropTargetPtr) == 0)
                {
                    dropTarget = (IDropTarget)Marshal.GetTypedObjectForIUnknown(dropTargetPtr, typeof(IDropTarget));
                    Marshal.Release(dropTargetPtr); // RCW has its own ref; release the raw COM ref
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ERROR: Exception: " + ex.ToString());
                return false;
            }
            finally
            {
                Marshal.ReleaseComObject(folder);
            }
        }

        #endregion

        #region        GetIDataObject
        /// <summary>
        /// This method will use the GetUIObjectOf method of IShellFolder to obtain the IDataObject of a
        /// ShellItem. 
        /// </summary>
        /// <param name="items">An array of CShellItem for which to obtain the IDataObject</param>
        /// <returns>the IDataObject the ShellItem</returns>
        /// <remarks>All CShItems in the array are ASSUMED to have the same parent folder.</remarks>
        public static IntPtr GetIDataObject(CShellItem[] items)
        {
            CShellItem parent;
            if (items[0].Parent is not null)
            {
                parent = items[0].Parent;
            }
            else
            {
                parent = items[0];
            }

            var pidls = new IntPtr[items.Length];
            int i = 0;
            while (i < items.Length)
            {
                pidls[i] = CPidl.ILFindLastID(items[i].PIDL);
                i += 1;
            }

            IntPtr dataObjectPtr = IntPtr.Zero;
            IntPtr rgfReserved = IntPtr.Zero; //unused
            var ishellfolder = parent.GetIShellFolder();
            try
            {
                var uiObject = ishellfolder.GetUIObjectOf(IntPtr.Zero, (uint)pidls.Length, pidls, ShellAPI.IID_IDataObject, rgfReserved, out dataObjectPtr);
                if (uiObject == S_OK)
                {
                    return dataObjectPtr;
                }
                else
                {
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ishellfolder);
            }
        }
        #endregion

        #region        GetIDropTargetHelper
        /// <summary>
        /// Obtains an IDropTargetHelper Interface
        /// </summary>
        /// <param name="helperPtr">Returns a pointer to the Interface</param>
        /// <param name="dropHelper">Returns the Interface itself.</param>
        /// <returns>True if successful, False otherwise.</returns>
        /// <remarks>This interface is used by drop targets to enable the drag-image manager to display the drag image while the image is over the target window. </remarks>
        /// 
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static bool GetIDropTargetHelper(out IntPtr helperPtr, out IDropTargetHelper dropHelper)
        {
            var CLSID_DragDropHelper = ShellAPI.CLSID_DragDropHelper;
            var IID_IDropTargetHelper = ShellAPI.IID_IDropTargetHelper;
            if (CoCreateInstance(ref CLSID_DragDropHelper, IntPtr.Zero, CLSCTX.INPROC_SERVER, ref IID_IDropTargetHelper, out helperPtr) == S_OK)
            {
                dropHelper = (IDropTargetHelper)Marshal.GetObjectForIUnknown(helperPtr);
                return true;
            }
            else
            {
                dropHelper = null;
                helperPtr = IntPtr.Zero;
                return false;
            }
        }
        #endregion

        #region        CanDropClipboard
        /// <summary>
        /// It obtains a DragDropEffects flag variable indicating the input CShellItem's ability to accept a Paste from the Clipboard.
        /// </summary>
        /// <param name="item">The item whose ability to accept a Paste is to be queried.</param>
        /// <returns>A DragDropEffect indicating what actions the input CShellItem is willing to do.</returns>
        /// <remarks>Used to determine if Paste is a valid menu item.</remarks>
        /// 
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static DragDropEffects CanDropClipboard(CShellItem item)
        {
            IntPtr dataObject;
            OleGetClipboard(out dataObject);

            IDropTarget target = null;

            var retVal = DragDropEffects.None;
            try
            {
                if (GetIDropTarget(item, out target))
                {

                    var effects = DragDropEffects.Copy;
                    if (target.DragEnter(dataObject, MK.CONTROL, new POINT(0, 0), ref effects) == S_OK)
                    {
                        if (effects == DragDropEffects.Copy)
                        {
                            retVal = retVal | DragDropEffects.Copy;
                        }

                        target.DragLeave();
                    }

                    effects = DragDropEffects.Move;
                    if (target.DragEnter(dataObject, MK.SHIFT, new POINT(0, 0), ref effects) == S_OK)
                    {
                        if (effects == DragDropEffects.Move)
                        {
                            retVal = retVal | DragDropEffects.Move;
                        }

                        target.DragLeave();
                    }

                    effects = DragDropEffects.Link;
                    if (target.DragEnter(dataObject, MK.ALT, new POINT(0, 0), ref effects) == S_OK)
                    {
                        if (effects == DragDropEffects.Link)
                        {
                            retVal = retVal | DragDropEffects.Link;
                        }

                        target.DragLeave();
                    }

                    Marshal.ReleaseComObject(target);
                }
            }
            finally
            {
                if (dataObject != IntPtr.Zero)
                    Marshal.Release(dataObject);
            }

            return retVal;
        }
        #endregion

        #region        QueryInfo
        /// <summary>
        /// Obtains an IQueryInfo Interface for the input CShellItem.
        /// </summary>
        /// <param name="item">The Item to obtain the Interface for.</param>
        /// <param name="iQueryInfoPtr">The pointer to the obtained Interface</param>
        /// <param name="iQueryInfo">The actual Interface</param>
        /// <returns>True if successful, False otherwise.</returns>
        /// <remarks>Not used by ExpTree or its' Demo.</remarks>
        /// 
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static bool GetIQueryInfo(CShellItem item, ref IntPtr iQueryInfoPtr, out IQueryInfo iQueryInfo)
        {
            CShellItem parent;
            if (item.Parent is not null)
            {
                parent = item.Parent;
            }
            else
            {
                parent = item;
            }

            IntPtr rgfReserved = IntPtr.Zero; //unused
            var ishellfolder = parent.GetIShellFolder();
            try
            {
                var ret = ishellfolder.GetUIObjectOf(IntPtr.Zero, 1, new IntPtr[] { CPidl.ILFindLastID(item.PIDL) }, ShellAPI.IID_IQueryInfo, rgfReserved, out iQueryInfoPtr);

                if (ret == S_OK)
                {
                    iQueryInfo = (IQueryInfo)Marshal.GetTypedObjectForIUnknown(iQueryInfoPtr, typeof(IQueryInfo));
                    return true;
                }
                else
                {
                    iQueryInfo = null;
                    iQueryInfoPtr = IntPtr.Zero;
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ishellfolder);
            }
        }

        #endregion

        #region        Make Shell ID Array (CIDA)
        /// <summary>
        /// Shell Folders prefer their IDragData to contain this format which is
        /// NOT directly supported by .Net.  The underlying structure is the CIDA structure
        /// which is basically VB, VB.Net, and C# Hostile.
        /// If "Make ShortCut(s) here" is the desired or
        /// POSSIBLE effect of the drag, then this format is REQUIRED -- otherwise the
        /// Folder will interpret the DragDropEffects.Link to be "Create Document Shortcut"
        /// which is NEVER the desired effect in this case
        /// The normal CIDA contains the Absolute PIDL of the source Folder and 
        /// Relative PIDLs for each Item in the Drag. 
        /// I cheat a bit an provide the Absolute PIDL of the Desktop (00, a short)
        /// and the Absolute PIDLs for the Items (all such Absolute PIDLS ar 
        /// relative to the Desktop.
        /// </summary>
        /// <param name="CSIList">A List of CShItems to be included in the CIDA MemoryStream</param>
        /// <returns>A MemoryStream which contains a CIDA containing the PIDLs of all Items in CSIList</returns>
        /// <remark>
        /// <para>The overall concept and much code taken from</para>
        /// http://www.dotnetmonster.com/Uwe/Forum.aspx/dotnet-interop/3482/Drag-and-Drop
        /// <para>Dave Anderson's response, translated from C# to VB.Net, was the basis
        /// of this routine</para>
        /// <para>An AHA momemnt and a ref to the above url came from</para>
        /// http://www.Planet-Source-Code.com/vb/scripts/ShowCode.asp?txtCodeId=61324%26lngWId=1
        /// </remark>
        /// 
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static System.IO.MemoryStream MakeShellIDArray(List<CShellItem> CSIList)
        {
            System.IO.MemoryStream MakeShellIDArrayRet = default;
            // ensure at least one item
            if (CSIList.Count < 1)
                return null;

            // bArrays is an Array of Byte() each containing the bytes of a PIDL
            var bArrays = new byte[CSIList.Count][];
            int i = 0;
            foreach (CShellItem CSI in CSIList)
            {
                bArrays[i] = new CPidl(CSI.PIDL).PidlBytes;
                i += 1;
            }

            MakeShellIDArrayRet = new System.IO.MemoryStream();
            var BW = new System.IO.BinaryWriter(MakeShellIDArrayRet);

            BW.Write(Convert.ToUInt32(CSIList.Count));   // we don't count the parent (Desktop)
            var Desktop = default(int);  // we only use the lowval 2 bytes (VB lacks meaninful uint)
            int Offset;   // offset into Structure of a PIDL

            // Calculate and write the offset to each pidl (defined as an array of uint32)
            // The first pidl is 2 bytes long (0 0) and represents the desktop
            // The 2 in the statement below is for the offset to the 
            // folder pidl and the count field in the CIDA structure
            Offset = Marshal.SizeOf(typeof(uint)) * (bArrays.Length + 2);
            BW.Write(Convert.ToUInt32(Offset));       // offset to desktop pidl
            Offset += 2; // Marshal.SizeOf(GetType(UInt16)) 'point to the next one
            var loopTo = bArrays.Length - 1;
            for (i = 0; i <= loopTo; i++)
            {
                BW.Write(Convert.ToUInt32(Offset));
                Offset += bArrays[i].Length;
            }
            // done with the array of offsets, write the parent pidl (0 0) = Desktop

            // Write the pidl bytes
            BW.Write(Convert.ToUInt16(Desktop));
            foreach (byte[] b in bArrays)
                BW.Write(b);
            return MakeShellIDArrayRet;

            // done, returning the memorystream
            // Debug.WriteLine("Done MakeShellIDArray")
        }
        #endregion

        #region        MakeDragListFromPtr 

        /// <summary>Builds a List of the CShItems being dragged from m_StreamCIDA</summary>
        /// <param name="ptr">IntPtr pointing to a CIDA</param>
        /// <returns>A List of the CShItems being dragged or nothing on failure</returns>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        internal static List<CShellItem> MakeDragListFromPtr(IntPtr ptr)
        {
            List<CShellItem> MakeDragListFromPtrRet = default;
            var streamCIDA = MakeStreamFromCIDA(ptr);
            var BR = new System.IO.BinaryReader(streamCIDA);
            var offsets = new int[BR.ReadInt32() + 1 + 1];   // 0=parent, last = total length
            offsets[offsets.Length - 1] = (int)BR.BaseStream.Length;
            int i;
            var loopTo = offsets.Length - 2;
            for (i = 0; i <= loopTo; i++)
                offsets[i] = BR.ReadInt32();
            var bArrays = new byte[offsets.Length - 2 + 1][];   // my objects are byte()
            var loopTo1 = bArrays.Length - 1;
            for (i = 0; i <= loopTo1; i++)
            {
                int thisLen = offsets[i + 1] - offsets[i];
                bArrays[i] = BR.ReadBytes(thisLen);
            }
            MakeDragListFromPtrRet = new List<CShellItem>();
            var loopTo2 = bArrays.Length - 1;
            for (i = 1; i <= loopTo2; i++)
            {
                bool isOK = true;
                try   // if GetCShitem returns Nothing(it's failure marker) then catch it
                {
                    MakeDragListFromPtrRet.Add(CShellItemFactory.Create(bArrays[0], bArrays[i]));
                }
                catch (Exception ex)
                {
                    Debug.Write("Error in making CShellItem from CIDA: " + ex.ToString());
                    isOK = false;
                }
                if (!isOK)
                    goto ERRXIT;
            }
            // on fall thru, all is done OK
            return MakeDragListFromPtrRet;

            // Error cleanup and Exit
        ERRXIT:
            ;
            MakeDragListFromPtrRet = new List<CShellItem>();
            Debug.WriteLine("MakeDragListFromCIDA failed");
            return MakeDragListFromPtrRet;
        }

        /// <summary>Given an IntPtr pointing to a CIDA,
        /// copy the CIDA to a new MemoryStream</summary>
        private static System.IO.MemoryStream MakeStreamFromCIDA(IntPtr ptr)
        {
            System.IO.MemoryStream MakeStreamFromCIDARet = default;
            MakeStreamFromCIDARet = null;    // assume failure
            if (ptr.Equals(IntPtr.Zero))
                return MakeStreamFromCIDARet;
            int nrItems = Marshal.ReadInt32(ptr, 0);
            if (!(nrItems > 0))
                return MakeStreamFromCIDARet;
            var offsets = new int[nrItems + 1];
            int curB = 4; // already read first 4
            int i;
            var loopTo = nrItems;
            for (i = 0; i <= loopTo; i++)
            {
                offsets[i] = Marshal.ReadInt32(ptr, curB);
                curB += 4;
            }
            var pidlLen = default(int);
            var pidlobjs = new byte[nrItems + 1][];
            var loopTo1 = nrItems;
            for (i = 0; i <= loopTo1; i++)
            {
                var ipt = new IntPtr(ptr.ToInt32() + offsets[i]);
                var cp = new CPidl(ipt);
                pidlobjs[i] = cp.PidlBytes;
                pidlLen += pidlobjs[i].Length;
            }
            MakeStreamFromCIDARet = new System.IO.MemoryStream(pidlLen + 4 * offsets.Length + 4);
            var BW = new System.IO.BinaryWriter(MakeStreamFromCIDARet);
            BW.Write(nrItems);
            var loopTo2 = nrItems;
            for (i = 0; i <= loopTo2; i++)
                BW.Write(offsets[i]);
            var loopTo3 = nrItems;
            for (i = 0; i <= loopTo3; i++)
                BW.Write(pidlobjs[i]);
            // DumpHex(MakeStreamFromCIDA.ToArray)
            MakeStreamFromCIDARet.Seek(0L, System.IO.SeekOrigin.Begin);
            return MakeStreamFromCIDARet;
        }

        #endregion

        #region        DataObjectContainsCShItems 

        /// <summary>
        /// Determines if input ShellDll.IDataObject will provide a Shell IDList Array (CIDA).
        /// </summary>
        /// <param name="dataObj">The ShellDll.IDataObject to be queried.</param>
        /// <returns>True if ShellDll.IDataObject will provide a CIDA.</returns>
        /// <remarks>Normally not needed.</remarks>
        public static bool DataObjectContainsCShItems(IDataObject dataObj)
        {
            var fmtEtc = new FORMATETC();
            string arglpszFormat = "Shell IDList Array";
            int cf = ShellAPI.RegisterClipboardFormat(arglpszFormat);
            if (cf != 0)
            {
                {
                    ref var withBlock = ref fmtEtc;
                    withBlock.cfFormat = (CF)cf;
                    withBlock.lindex = -1;
                    withBlock.dwAspect = DVASPECT.CONTENT;
                    withBlock.ptd = IntPtr.Zero;
                    withBlock.Tymd = TYMED.HGLOBAL;
                }

                int hr = dataObj.QueryGetData(ref fmtEtc);
                if (hr == S_OK)
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region        GetCShItemsFromDataObject 

        /// <summary>
        /// Given an IDataObject, return a list of CShItems corresponding to the PIDLs in
        /// the Shell IDList Array (CIDA) contained in the IDataObject.
        /// </summary>
        /// <param name="dataObj">A well formed ShellDll.IDataObject from which to extract the CShItems.</param>
        /// <returns>List(Of CShItems) with all CShItems represented by the PIDLs in the CIDA.</returns>
        /// <remarks>Used by ExplorerControls for standalone ExpList.</remarks>
        [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
        public static List<CShellItem> GetCShItemsFromDataObject(IDataObject dataObj)
        {
            var items = new List<CShellItem>();
            FORMATETC fmtEtc;
            STGMEDIUM stg;

            string arglpszFormat = "Shell IDList Array";
            int cf = ShellAPI.RegisterClipboardFormat(arglpszFormat);
            if (cf != 0)
            {
                fmtEtc.cfFormat = (CF)cf;
                fmtEtc.lindex = -1;
                fmtEtc.dwAspect = DVASPECT.CONTENT;
                fmtEtc.ptd = IntPtr.Zero;
                fmtEtc.Tymd = TYMED.HGLOBAL;
                 
                stg.hGlobal = IntPtr.Zero;
                stg.pUnkForRelease = IntPtr.Zero;
                stg.tymed = (int)TYMED.HGLOBAL;

                int HR = dataObj.GetData(ref fmtEtc, ref stg);
                #if DEBUG
                if (HR < 0)
                    Marshal.ThrowExceptionForHR(HR);
                #endif
                items = MakeDragListFromPtr(GlobalLock(stg.hGlobal));
                GlobalUnlock(stg.hGlobal);
                ReleaseStgMedium(ref stg);       // done with this
                return items;
            }
            return null;
        }
        #endregion

        /// <summary>
        /// Gets a shell parsing name from an IShellFolder object (works for virtual and filesystem folders).
        /// Returns null if unavailable.
        /// </summary>
        public static string? GetShellFolderDisplayName(IShellFolder shellFolder)
        {
            if (shellFolder is not IPersistFolder2 pf2)
                return null;

            IntPtr pidl = IntPtr.Zero;
            IntPtr pszName = IntPtr.Zero;

            try
            {
                int hr = pf2.GetCurFolder(out pidl);
                if (hr < 0 || pidl == IntPtr.Zero)
                    return null;

                hr = SHGetNameFromIDList(pidl, SIGDN.DESKTOPABSOLUTEPARSING, out pszName);
                if (hr < 0 || pszName == IntPtr.Zero)
                    return null;

                return Marshal.PtrToStringUni(pszName);
            }
            finally
            {
                if (pszName != IntPtr.Zero) Marshal.FreeCoTaskMem(pszName); 
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }
        }

        /// <summary>
        /// Gets filesystem path from an IShellFolder object if it maps to disk.
        /// Returns null for virtual folders (e.g., Control Panel).
        /// </summary>
        public static string? GetShellFolderPathIfFilesystem(IShellFolder shellFolder)
        {
            if (shellFolder is not IPersistFolder2 pf2)
                return null;

            IntPtr pidl = IntPtr.Zero;

            try
            {
                int hr = pf2.GetCurFolder(out pidl);
                if (hr < 0 || pidl == IntPtr.Zero)
                    return null;

                // MAX_PATH
                var buffer = new char[WinSDK.MAX_PATH];
                if (!SHGetPathFromIDListW(pidl, buffer))
                    return null;

                int len = Array.IndexOf(buffer, '\0');
                if (len < 0) len = buffer.Length;
                return new string(buffer, 0, len);
            }
            finally
            {
                if (pidl != IntPtr.Zero) WinSDK.CoTaskMemFree(pidl);
            }
        }

        /// <summary>
        /// GetFolder returns the IShellFolder interface of the Folder designated by the input Parent and 
        /// relative PIDL.
        /// </summary>
        /// <param name="parent">The CShellItem of the Folder containing the folder for which the 
        /// IShellFolder interface is desired.</param>
        /// <param name="relPidl">The relative Pidl of the folder for which the interface is desired.</param>
        /// <returns>The desired interface or Nothing if error.</returns>
        /// <remarks></remarks>
        public static IShellFolder GetIShellFolder(IShellFolder parent, IntPtr relPidl)
        {
            IntPtr ptr = IntPtr.Zero;
            IShellFolder iShFolder = null;
            int HR = parent.BindToObject(relPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref ptr);
            if (HR >= S_OK && ptr != IntPtr.Zero)   // New code (12/12/09)
            {
                // The ASUS fix is slightly modified from its' original as per a suggestion from Calum 4/8/2010
                try 
                {
                    iShFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(ptr, typeof(IShellFolder));
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine("GetFolder: " + ex.Message);
                    throw;
#endif
                }
                finally
                {
                    Marshal.Release(ptr); // Must do this in all cases
                }
            }
            else
            {
                if (ptr != IntPtr.Zero)
                    Marshal.Release(ptr);
#if DEBUG
                CPidl.Dump(relPidl);
                Debug.WriteLine($"pidl path = '{ CPidl.ToString(relPidl) }'");
                HResultLogger.LogHResult(HR);
#endif
                return null;
            }
            return iShFolder;
        }

        public static IShellFolder GetIShellFolder(IntPtr absPidl)
        {
            IShellFolder desktop = null;
            int hr = SHGetDesktopFolder(ref desktop);
            if (hr < S_OK) return null;

            if (CPidl.IsShellNamespaceRoot(absPidl)) return desktop;

            IntPtr ptr = IntPtr.Zero;
            IShellFolder iShFolder = null;
            hr = desktop.BindToObject(absPidl, IntPtr.Zero, ShellAPI.IID_IShellFolder, ref ptr);
            if (hr >= S_OK && ptr != IntPtr.Zero)
            {
                try
                {
                    iShFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(ptr, typeof(IShellFolder));
                }
                finally
                {
                    Marshal.Release(ptr);
                }
            }

            Marshal.ReleaseComObject(desktop);
            return iShFolder;
        }

        /// <summary>
        /// Get's the last write time for a PIDL by calling SHGetDataFromIDListW with the SHGDFIL_FINDDATA flag 
        /// and extracting the FILETIME from the returned WIN32_FIND_DATAW structure.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="childPidl">relative PIDL (child of folder)</param>
        /// <param name="lastWriteTime"></param>
        /// <returns></returns>
        public static bool TryGetLastWriteTimeForPidl(IShellFolder folder, IntPtr childPidl, out FILETIME lastWriteTime)
        {
            lastWriteTime = default;

            if (folder == null || childPidl == IntPtr.Zero)
                return false;

            int hr = SHGetDataFromIDListW(
                folder,
                childPidl,
                SHGDFIL_FINDDATA,
                out WIN32_FIND_DATAW fd,
                Marshal.SizeOf<WIN32_FIND_DATAW>());

            // SUCCEEDED(hr)
            if (hr < 0)
                return false;

            lastWriteTime = fd.ftLastWriteTime;

            // optional: treat zero FILETIME as "no value"
            return lastWriteTime.dwLowDateTime != 0 || lastWriteTime.dwHighDateTime != 0;
        }

        public static long FileTimeToLong(FILETIME ft)
        {
            // FILETIME is an unsigned 64-bit value split into two 32-bit parts.
            // Cast through uint to avoid sign-extension issues.
            return ((long)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }

        #region        Shell Navigation and PIDL Utilities

        /// <summary>The WalkAllCallBack delegate defines the signature of 
        /// the routine to be passed to AllFolderWalk which returns the CShellItem of each
        /// file and directory in and below an Folder CShellItem.
        /// </summary>
        /// <example>Dim DWalk as New CShellItem.WalkAllCallBack(addressof yourroutine)</example>
        public delegate bool WalkAllCallBack(CShellItem info, int UserLevel, int Tag);

        /// <summary>
        /// Returns a List containing the CShItems of all Folders in the entire internal tree.
        /// </summary>
        /// <returns>A List containing the CShItems of all Folders in the entire internal tree.</returns>
        /// <remarks>The sort order is determined by standard tree traversal (Depth First).</remarks>
        public static List<CShellItem> AllFolderWalk()
        {
            var rVal = new List<CShellItem>();
            var desktop = ShellController.DesktopCSI;
            rVal.Add(desktop);
            WalkHelper(desktop, rVal);
            return rVal;
        }

        private static void WalkHelper(CShellItem item, List<CShellItem> list)
        {
            if (item.DirectoriesInitialized)
            {
                foreach (CShellItem CSI in item.Directories)
                {
                    list.Add(CSI);
                    WalkHelper(CSI, list);
                }
            }
        }

        #endregion

    }
}
