using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowsApiLib;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;


namespace WindowsApiLib.Shell
{
    /// <summary>
    /// The purpose of this class is to maintain and manipulate a collection of CShellItems 
    /// and to do so in a hierachical structure.  The hierachical structure is to enable 
    /// navigation and updating of the shell items which have a hierachical relationship
    /// with each other in the Windows Shell namespace.
    /// </summary>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CShellItemHierachyManager
    {
        public object Lock = new object();

        public CShellItem Root { get; set; }
        public CShellItem? CurrentFolder { get; set; }
        public string? CurrentPath { get {
                if (CurrentFolder?.PIDL == null) return string.Empty;
                return CPidl.ToString(CurrentFolder.PIDL);
            } }

        public CShellItemHierachyManager(CShellItem? root = null) {
            this.Root = root;

            //todo: move the item hierarchy code from cshellitem to over here.
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="ptr">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 - most code in this function replaced by a call to FindCShItem(BaseItem as CShellItem, Abs as IntPtr)</remarks>
        public CShellItem? FindItem(IntPtr ptr)
        {
            return FindCShItem(Root, ptr);
        }

        public CShellItem? FindCShItem(string fullFileName)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(fullFileName);
            return FindCShItem(Root, pidl);
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="absPidl">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 -Function added to replace algorithm used in FindCShItem(ptr as IntPtr) which now only calls this routine.</remarks>
        public CShellItem? FindCShItem(CShellItem BaseItem, IntPtr absPidl)
        {
            CShellItem? target = null;

            if (CPidl.ResolvesToSamePathOrName(BaseItem.PIDL, absPidl))
                return BaseItem;

            if (BaseItem.DirectoryList is not null) //problem: if you jump multiple folders deep when navigating, you will have Folders that are not initialized and this search can fail.  This function isn't supposed to fill in the tree but not doing so makes it hard to navigate
            {
                foreach (CShellItem DItem in BaseItem.DirectoryList)
                {
                    if (CPidl.ResolvesToSamePathOrName(DItem.PIDL, absPidl))
                        return DItem;
                    if (CPidl.IsAncestorOf(DItem.PIDL, absPidl, false)) //note that items are considered to be ancestors of themselves which is kinda weird
                        return FindCShItem(DItem, absPidl);
                }
            }

            if (BaseItem.FileList is not null && CPidl.IsAncestorOf(BaseItem.PIDL, absPidl, true))
            {
                //var name = CPidl.GetFileSystemPath(Abs);//doesn't work with dlna media servers
                //if (name is null) return null;
                var name = CPidl.GetDisplayName(absPidl);//doesn't work with dlna media servers
                if (name is null) return null;

                if (BaseItem.FilesDic.TryGetValue(name, out CShellItem fileItem)) 
                {
                    return fileItem;
                }
                else return null;
                //foreach (CShellItem FItem in BaseItem.FileList)
                //{
                //    //if (CPidl.IsEqual(FItem.PIDL, Abs)) //too slow
                //    //    return FItem;

                //    if (FItem.FullPath == fullPath)
                //        return FItem;
                //}
            }

            return null;
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="b">A Byte array representation of a Full or Absolute PIDL 
        /// referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks></remarks>
        public CShellItem FindCShItem(byte[] b)
        {
            CShellItem FindCShItemRet = default;
            if (!CPidl.IsValid(b))
                return null;
            var thisPidl = Marshal.AllocCoTaskMem(b.Length);
            if (thisPidl.Equals(IntPtr.Zero))
                return null;
            Marshal.Copy(b, 0, thisPidl, b.Length);
            FindCShItemRet = FindCShItem(Root, thisPidl);
            Marshal.FreeCoTaskMem(thisPidl);
            return FindCShItemRet;
        }

        public CShellItem Add(CShellItem csi)
        {
            var result = FindOrAdd(csi.PIDL, out CShellItem parent);
            return result;
        }

        public CShellItem? FindOrAdd(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero)
            {
                Debug.WriteLine("Invalid path provided to FindInShellHierarchy(): '" + path + "'");
                return null;
            }
            try
            {
                return FindOrAdd(pidl, out _);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        /// <summary>
        /// This is the "engine" that maintains the hierarchical relationship between items.
        /// - Method: internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        /// - Logic: It traverses the cached tree from the Desktop down to the target PIDL.  If an item 
        /// doesn't exist in the cache, it expands the parent folders to find or place the item. This ensures 
        /// that every CShellItem is correctly linked to its parent in the internal structure.
        /// 
        /// BrowseTo locates the desired item and places it in its proper location on the internal tree.
        /// Any and all sub-directories that need to be populated in the tree in order to properly place
        /// the desired item, are populated. This is the programmatic equivalent of Browsing to a node in 
        /// <code>ExpTree's</code> TreeView.<br /> 
        /// BrowseTo also returns the Parent CShellItem. 
        /// If the desired CShellItem does not exist, the returned Parent is the CShellItem that would be the
        /// Immediate ancestor (containing CShellItem or Parent) of the desired item should it be created.
        /// </summary>
        /// <param name="absPidl">A Absolute PIDL whose CShellItem is to be found</param>
        /// <param name="Parent">Output parameter -- Immediate Ancestor CShellItem of the found item OR 
        /// the CShellItem that would contain the item if it existed OR Nothing if NO Immediate ancestor found 
        /// in the Shell namespace. </param>
        /// <returns>The desired CShellItem or, if not found, Nothing.</returns>
        /// <remarks>A by-product of this search is that any sub-dirs of the tree along the path will be 
        /// populated with their sub directories.
        /// It is logically possible that NO Immediate ancestor can be found.
        /// For Example: GetCShItem(Path) may be given a string specifying a non-existant directory.
        /// (eg -- C:\Test\NonExistant\junk.txt). 
        /// In that case, and that case only, Parent may be returned as Nothing.</remarks>
        public CShellItem? FindOrAdd(IntPtr absPidl, out CShellItem? Parent)
        {
            Parent = null;

            var currentFolder = Root;
            if (currentFolder == null) throw new Exception("The root of the shell hierarchy was null.");

            if (CPidl.AreEqual(currentFolder.IShlFolder, currentFolder.PIDL, absPidl))  // we found the desired item
            {
                Parent = null;
                return currentFolder;
            }

            bool foundFinalExtantParentDirectory = false;
            while (!foundFinalExtantParentDirectory)
            { //todo: I don't like how reading of folder contents is hidden inside the Directories and Files properties rather than being explicit
                foreach (var currentCSI in currentFolder.Directories) //check directories before files because there tend to be fewer directories and there's no point checking files if we haven't delved deeply enough into the tree yet
                {
                    if (IsAncestorOf(currentCSI.PIDL, absPidl))
                    {
                        if (CPidl.AreEqual(currentFolder.IShlFolder, currentCSI.PIDL, absPidl))  // we found the desired item
                        {
                            Parent = currentFolder;
                            return currentCSI;
                        }
                        else // Found an ancestor and must delve into it
                        {
                            currentFolder = currentCSI;
                            goto NEXTWHILE;
                        }
                    }
                }

                foundFinalExtantParentDirectory = true; //can't delve any deeper.  currentFolder is the parent folder
            NEXTWHILE:;
            }

            //Test for invalid paths and mismatched path lengths
            if (CPidl.SegmentCount(currentFolder.PIDL) + 1 != CPidl.SegmentCount(absPidl)) //the root folder plus 1 final pidl should be the same length as the given pidl if the given pidl is real
            {
                Debug.WriteLine("Invalid pidl provided to FindInShellHierarchy(): '" + CPidl.ToString(absPidl) + "'");
                return null;
            }

            var name = CPidl.GetDisplayName(absPidl);
            
            // Check for files in the current folder
            if (currentFolder.FilesDic.TryGetValue(name, out CShellItem fileItem))
            {
                Parent = currentFolder;
                return fileItem;
            }

            Debug.WriteLine("Could not find file in the current folder: '" + CPidl.ToString(absPidl) + "'");
            return null;
        }

        /// <summary>True if parameter "ancestor" is an ancestor of parameter "current" 
        /// </summary>
        /// <returns>IsAncestorOf returns True if input CShellItem ancestor is an ancestor of input CShellItem current</returns>
        /// <remarks>if OS is Win2K or above, uses the ILIsParent API, otherwise uses the
        /// cPidl function StartsWith.  This is necessary since ILIsParent in only available
        /// in Win2K or above systems AND StartsWith fails on some folders on XP systems (most
        /// obviously some Network Folder Shortcuts, but also Control Panel. Note, StartsWith
        /// always works on systems prior to XP.<br />
        /// NOTE: if ancestor and current reference the same Item, both
        /// methods return True</remarks>
        public static bool IsAncestorOf(CShellItem ancestor, CShellItem current, bool fParent = false)
        {
            return IsAncestorOf(ancestor.PIDL, current.PIDL, fParent);
        }

        /// <summary> Compares a candidate Ancestor PIDL with a Child PIDL and
        /// returns True if Ancestor is an ancestor of the child.
        /// if fParent is True, then only return True if Ancestor is the immediate
        /// parent of the Child</summary>
        /// <param name="AncestorPidl">The Absolute PIDL that is the candidate for being an Ancestor of ChildPidl.</param>
        /// <param name="ChildPidl">The Absolute PIDL whose ancestory is being searched for.</param>
        /// <param name="fParent">A flag. If True, then only return True if AncestorPidl is the immediate Parent of ChildPidl.</param>
        /// <returns>True if AncestorPidl is an ancestor of ChildPidl.
        ///          If fParent is False then will also return True if AncestorPidl and ChildPidl are equal. 
        ///          If fParent is True, <i>only</i> returns True if AncestorPidl is the Parent of ChildPidl</returns>
        ///          
        public static bool IsAncestorOf(IntPtr AncestorPidl, IntPtr ChildPidl, bool fParent = false)
        {
            return ShellAPI.ILIsParent(AncestorPidl, ChildPidl, fParent);
        }


        /// <summary>
        /// Removes an item from the hierarchy if it is found.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(CShellItem item)
        {
            CShellItem? target = null;

            try
            {
                lock (this.Lock)
                {
                    target = FindItem(item.PIDL);
                }
                
                if (target == null) return false;

                lock (target)
                {
                    if (target.IsFolder)
                    {
                        if (target.FilesInitialized)
                        {
                            foreach (var child in target.m_Files)
                            {
                                child.m_Parent = null; //should we delete all children?
                            }

                            target.m_Files.Clear();
                        }
                        if (target.FoldersInitialized)
                        {
                            foreach (var child in target.m_Files)
                            {
                                child.m_Parent = null;
                            }

                            target.m_Directories.Clear(); //should we recursively unlink items?
                        }

                        target.Parent.m_Directories.Remove(target);
                    }
                    else
                    {
                        target.Parent.m_Files.Remove(target);
                    }
                }               
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in CShellItemHierarchyManager.RemoveItem: " + ex.ToString());
            }

            if (target != null)
            {
                CShellItemUpdater.RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(target, CShItemUpdateType.Deleted));
            }

            return true;
        }
    }


}
